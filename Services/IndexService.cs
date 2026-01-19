using System.Diagnostics;
using System.Threading.Channels;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class IndexService
{
    private readonly AppDb db;

    // Prevent concurrent indexing runs across the whole app instance
    private readonly SemaphoreSlim indexGate = new(1, 1);
    private readonly LocationMetadataService locationMetadataService;
    private readonly MediaStoreScanner scanner;
    private readonly ThumbnailService thumbnailService;
    private readonly VideoDurationService videoDurationService;

    public IndexService(AppDb db, MediaStoreScanner scanner, LocationMetadataService locationMetadataService,
        ThumbnailService thumbnailService, VideoDurationService videoDurationService)
    {
        this.db = db;
        this.scanner = scanner;
        this.locationMetadataService = locationMetadataService;
        this.thumbnailService = thumbnailService;
        this.videoDurationService = videoDurationService;
    }

    public async Task<int> IndexSourcesAsync(
        IEnumerable<MediaSource> sources,
        MediaType indexedTypes,
        IProgress<IndexProgress>? progress,
        CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await indexGate.WaitAsync(ct).ConfigureAwait(false);

        Channel<MediaItem>? locationQueue = null;
        List<Task>? locationWorkers = null;

        Channel<MediaItem>? thumbnailQueue = null;
        List<Task>? thumbnailWorkers = null;

        Channel<string>? durationQueue = null;
        List<Task>? durationWorkers = null;

        try
        {
            var sourceList = (sources ?? Enumerable.Empty<MediaSource>())
                .Where(s => s != null)
                .ToList();

            var knownFiles = await LoadKnownFileSignaturesAsync(ct).ConfigureAwait(false);

            var inserted = 0;
            // Number of items consumed by the DB writer (used for progress).
            var processedOverall = 0;
            // Number of items discovered during scanning (used as dynamic total).
            var discoveredOverall = 0;

            var counters = new WorkCounters();

            var locationsEnabled = locationMetadataService.IsEnabled;

            // Smaller batches reduce data loss risk if indexing is interrupted.
            const int BatchSize = 128;

            if (locationsEnabled)
            {
                locationQueue = Channel.CreateBounded<MediaItem>(new BoundedChannelOptions(256)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

                var workerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
                locationWorkers = Enumerable.Range(0, workerCount)
                    .Select(_ => Task.Run(() => ProcessLocationQueueAsync(locationQueue.Reader, counters, ct), ct))
                    .ToList();
            }

            // Generate thumbnails in the background while indexing.
            thumbnailQueue = Channel.CreateBounded<MediaItem>(new BoundedChannelOptions(1024)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            var thumbWorkerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
            thumbnailWorkers = Enumerable.Range(0, thumbWorkerCount)
                .Select(_ => Task.Run(() => ProcessThumbnailQueueAsync(thumbnailQueue.Reader, counters, ct), ct))
                .ToList();

            var durationsEnabled = OperatingSystem.IsWindows();
            if (durationsEnabled)
            {
                durationQueue = Channel.CreateBounded<string>(new BoundedChannelOptions(8192)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

                var durationWorkerCount = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);
                durationWorkers = Enumerable.Range(0, durationWorkerCount)
                    .Select(_ => Task.Run(() => ProcessDurationQueueAsync(durationQueue.Reader, counters, ct), ct))
                    .ToList();
            }

            async Task<int> UpsertBatchAsync(List<MediaItem> items)
            {
                if (items.Count == 0)
                    return 0;

                var insertedRows = 0;


                try
                {
                    // Batch DB operations in a single transaction to dramatically reduce async overhead.
                    // NOTE: Avoid reusing SQLiteCommand.Bind() in tight loops. On some platforms/providers this can
                    // accumulate parameter bindings and silently break subsequent executions, leading to missing rows.
                    var insertedLocal = 0;

                    await db.Db.RunInTransactionAsync(conn =>
                    {
                        foreach (var item in items)
                        {
                            if (item == null || string.IsNullOrWhiteSpace(item.Path))
                                continue;

                            try
                            {
                                var rows = conn.Execute(
                                    "INSERT OR IGNORE INTO MediaItem(Path, Name, MediaType, DurationMs, DateAddedSeconds, SizeBytes, SourceId) VALUES(?, ?, ?, ?, ?, ?, ?);",
                                    item.Path,
                                    item.Name,
                                    (int)item.MediaType,
                                    item.DurationMs,
                                    item.DateAddedSeconds,
                                    item.SizeBytes,
                                    item.SourceId);

                                if (rows == 0)
                                    // Keep existing ThumbnailPath/PeopleTagsSummary/etc. intact.
                                    conn.Execute(
                                        "UPDATE MediaItem SET Name = ?, MediaType = ?, DurationMs = CASE WHEN ? > 0 THEN ? ELSE DurationMs END, DateAddedSeconds = ?, SizeBytes = CASE WHEN ? IS NOT NULL AND ? > 0 THEN ? ELSE SizeBytes END, SourceId = ? WHERE Path = ?;",
                                        item.Name,
                                        (int)item.MediaType,
                                        item.DurationMs,
                                        item.DurationMs,
                                        item.DateAddedSeconds,
                                        item.SizeBytes,
                                        item.SizeBytes,
                                        item.SizeBytes,
                                        item.SourceId,
                                        item.Path);
                                else
                                    insertedLocal += rows;
                            }
                            catch (Exception rowEx)
                            {
                                // Do not fail the whole transaction because of a single bad row.
                                ErrorLog.LogException(rowEx, "IndexService.UpsertBatchAsync",
                                    $"BatchUpsert Path={item.Path}");
                            }
                        }
                    }).ConfigureAwait(false);

                    insertedRows += insertedLocal;
                }
                catch (Exception ex)
                {
                    ErrorLog.LogException(ex, "IndexService.UpsertBatchAsync", "BatchUpsert");

                    foreach (var item in items)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.Path))
                            continue;

                        try
                        {
                            var rows = await db.Db.ExecuteAsync(
                                    "INSERT OR IGNORE INTO MediaItem(Path, Name, MediaType, DurationMs, DateAddedSeconds, SizeBytes, SourceId) VALUES(?, ?, ?, ?, ?, ?, ?);",
                                    item.Path,
                                    item.Name,
                                    (int)item.MediaType,
                                    item.DurationMs,
                                    item.DateAddedSeconds,
                                    item.SizeBytes,
                                    item.SourceId)
                                .ConfigureAwait(false);

                            if (rows == 0)
                                // Keep existing ThumbnailPath/PeopleTagsSummary/etc. intact.
                                await db.Db.ExecuteAsync(
                                        "UPDATE MediaItem SET Name = ?, MediaType = ?, DurationMs = CASE WHEN ? > 0 THEN ? ELSE DurationMs END, DateAddedSeconds = ?, SizeBytes = CASE WHEN ? IS NOT NULL AND ? > 0 THEN ? ELSE SizeBytes END, SourceId = ? WHERE Path = ?;",
                                        item.Name,
                                        (int)item.MediaType,
                                        item.DurationMs,
                                        item.DurationMs,
                                        item.DateAddedSeconds,
                                        item.SizeBytes,
                                        item.SizeBytes,
                                        item.SizeBytes,
                                        item.SourceId,
                                        item.Path)
                                    .ConfigureAwait(false);
                            else
                                insertedRows += rows;
                        }
                        catch (Exception itemEx)
                        {
                            ErrorLog.LogException(itemEx, "IndexService.UpsertBatchAsync", $"Path={item.Path}");
                        }
                    }
                }

                if (locationsEnabled && locationQueue != null)
                    foreach (var item in items)
                        if (item?.MediaType is MediaType.Photos or MediaType.Graphics or MediaType.Videos &&
                            !string.IsNullOrWhiteSpace(item.Path))
                        {
                            await QueueLocationLookupAsync(locationQueue, item.Path, item.MediaType, ct)
                                .ConfigureAwait(false);
                            Interlocked.Increment(ref counters.LocationsQueued);
                        }

                if (thumbnailQueue != null)
                    foreach (var item in items)
                        if (item?.MediaType is MediaType.Photos or MediaType.Videos or MediaType.Graphics &&
                            !string.IsNullOrWhiteSpace(item.Path))
                        {
                            await QueueThumbnailAsync(thumbnailQueue, item.Path, item.MediaType, ct)
                                .ConfigureAwait(false);
                            Interlocked.Increment(ref counters.ThumbsQueued);
                        }


                if (durationQueue != null)
                    foreach (var item in items)
                        if (item?.MediaType == MediaType.Videos &&
                            !string.IsNullOrWhiteSpace(item.Path))
                        {
                            await WriteToChannelAsync(durationQueue.Writer, item.Path, ct).ConfigureAwait(false);
                            Interlocked.Increment(ref counters.DurationsQueued);
                        }

                return insertedRows;
            }

            var scheduler = new ProgressScheduler(progress, 150);
            scheduler.Report(new IndexProgress(0, 0, 0, "", "", 0, 0, 0, 0, 0, 0), true);

            // Producers: scan sources in parallel. Consumer: single DB writer.
            var itemQueue = Channel.CreateBounded<(string SourceName, MediaItem Item)>(new BoundedChannelOptions(8192)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            var consumer = Task.Run(async () =>
            {
                var batch = new List<MediaItem>(BatchSize);
                var lastSourceName = string.Empty;
                var lastPath = string.Empty;

                await foreach (var payload in itemQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    lastSourceName = payload.SourceName ?? string.Empty;
                    var item = payload.Item;
                    if (item == null || string.IsNullOrWhiteSpace(item.Path))
                        continue;

                    lastPath = item.Path;
                    Interlocked.Increment(ref processedOverall);
                    batch.Add(item);
                    if (batch.Count >= BatchSize)
                    {
                        var delta = await UpsertBatchAsync(batch).ConfigureAwait(false);
                        if (delta != 0)
                            Interlocked.Add(ref inserted, delta);
                        batch.Clear();

                        scheduler.Report(new IndexProgress(
                            Volatile.Read(ref processedOverall),
                            Volatile.Read(ref discoveredOverall),
                            Volatile.Read(ref inserted),
                            lastSourceName,
                            lastPath,
                            Volatile.Read(ref counters.ThumbsQueued),
                            Volatile.Read(ref counters.ThumbsDone),
                            Volatile.Read(ref counters.LocationsQueued),
                            Volatile.Read(ref counters.LocationsDone),
                            Volatile.Read(ref counters.DurationsQueued),
                            Volatile.Read(ref counters.DurationsDone)));
                    }
                }

                if (batch.Count > 0)
                {
                    var delta = await UpsertBatchAsync(batch).ConfigureAwait(false);
                    if (delta != 0)
                        Interlocked.Add(ref inserted, delta);

                    scheduler.Report(new IndexProgress(
                        Volatile.Read(ref processedOverall),
                        Volatile.Read(ref discoveredOverall),
                        Volatile.Read(ref inserted),
                        lastSourceName,
                        lastPath,
                        Volatile.Read(ref counters.ThumbsQueued),
                        Volatile.Read(ref counters.ThumbsDone),
                        Volatile.Read(ref counters.LocationsQueued),
                        Volatile.Read(ref counters.LocationsDone),
                        Volatile.Read(ref counters.DurationsQueued),
                        Volatile.Read(ref counters.DurationsDone)));

                    batch.Clear();
                }

                scheduler.Report(new IndexProgress(
                    Volatile.Read(ref processedOverall),
                    Volatile.Read(ref discoveredOverall),
                    Volatile.Read(ref inserted),
                    lastSourceName,
                    lastPath,
                    Volatile.Read(ref counters.ThumbsQueued),
                    Volatile.Read(ref counters.ThumbsDone),
                    Volatile.Read(ref counters.LocationsQueued),
                    Volatile.Read(ref counters.LocationsDone),
                    Volatile.Read(ref counters.DurationsQueued),
                    Volatile.Read(ref counters.DurationsDone)), true);
            }, ct);

            var maxProducers = Math.Clamp(Environment.ProcessorCount, 1, 8);
            var producerGate = new SemaphoreSlim(maxProducers, maxProducers);
            var producers = sourceList.Select(async source =>
            {
                await producerGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var sourceName = source.DisplayName ?? string.Empty;
                    await foreach (var v in scanner.StreamSourceAsync(source, indexedTypes, knownFiles, ct)
                                       .ConfigureAwait(false))
                    {
                        ct.ThrowIfCancellationRequested();

                        if (v == null || string.IsNullOrWhiteSpace(v.Path))
                            continue;

                        await itemQueue.Writer.WriteAsync((sourceName, v), ct).ConfigureAwait(false);

                        var total = Interlocked.Increment(ref discoveredOverall);
                        scheduler.Report(new IndexProgress(
                            Volatile.Read(ref processedOverall),
                            total,
                            Volatile.Read(ref inserted),
                            sourceName,
                            v.Path ?? string.Empty,
                            Volatile.Read(ref counters.ThumbsQueued),
                            Volatile.Read(ref counters.ThumbsDone),
                            Volatile.Read(ref counters.LocationsQueued),
                            Volatile.Read(ref counters.LocationsDone),
                            Volatile.Read(ref counters.DurationsQueued),
                            Volatile.Read(ref counters.DurationsDone)));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Indexing failed for source '{source.DisplayName}': {ex}");
                    ErrorLog.LogException(ex, "IndexService.IndexSourcesAsync", $"Source={source.DisplayName}");
                }
                finally
                {
                    producerGate.Release();
                }
            }).ToList();

            await Task.WhenAll(producers).ConfigureAwait(false);
            itemQueue.Writer.TryComplete();

            try
            {
                await consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }

            // Final report at the end (not throttled)
            scheduler.Report(new IndexProgress(
                Volatile.Read(ref processedOverall),
                Volatile.Read(ref discoveredOverall),
                Volatile.Read(ref inserted),
                "",
                "",
                Volatile.Read(ref counters.ThumbsQueued),
                Volatile.Read(ref counters.ThumbsDone),
                Volatile.Read(ref counters.LocationsQueued),
                Volatile.Read(ref counters.LocationsDone),
                Volatile.Read(ref counters.DurationsQueued),
                Volatile.Read(ref counters.DurationsDone)), true);
            scheduler.Flush();

            return inserted;
        }
        finally
        {
            if (thumbnailQueue != null)
            {
                thumbnailQueue.Writer.TryComplete();
                if (thumbnailWorkers != null)
                    try
                    {
                        await Task.WhenAll(thumbnailWorkers).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation.
                    }
            }

            if (durationQueue != null)
            {
                durationQueue.Writer.TryComplete();
                if (durationWorkers != null)
                    try
                    {
                        await Task.WhenAll(durationWorkers).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation.
                    }
            }


            if (locationQueue != null)
            {
                locationQueue.Writer.TryComplete();
                if (locationWorkers != null)
                    try
                    {
                        await Task.WhenAll(locationWorkers).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation while draining location workers.
                    }
            }

            indexGate.Release();
        }
    }

    public async Task UpdateThumbnailPathAsync(string mediaPath, string? thumbnailPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || string.IsNullOrWhiteSpace(thumbnailPath))
            return;

        ct.ThrowIfCancellationRequested();
        await db.EnsureInitializedAsync().ConfigureAwait(false);

        await db.Db.ExecuteAsync(
                "UPDATE MediaItem SET ThumbnailPath = ? WHERE Path = ? AND (ThumbnailPath IS NULL OR ThumbnailPath <> ?);",
                thumbnailPath,
                mediaPath,
                thumbnailPath)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<IndexedFileSignature>> LoadKnownFileSignaturesAsync(CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);

        List<MediaItemSignatureRow> rows;
        try
        {
            rows = await db.Db
                .QueryAsync<MediaItemSignatureRow>("SELECT Path, SizeBytes FROM MediaItem;")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "IndexService.LoadKnownFileSignaturesAsync");
            return new HashSet<IndexedFileSignature>();
        }

        var known = new HashSet<IndexedFileSignature>();
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(row.Path))
                continue;

            if (!row.SizeBytes.HasValue || row.SizeBytes.Value <= 0)
                continue;

            known.Add(new IndexedFileSignature(row.Path, row.SizeBytes.Value));
        }

        return known;
    }

    private async Task<int> CountAllAsync(List<MediaSource> sources, MediaType indexedTypes, CancellationToken ct)
    {
        if (sources.Count == 0)
            return 0;

        var total = 0;
        var max = Math.Clamp(Environment.ProcessorCount, 1, 4);
        var gate = new SemaphoreSlim(max, max);

        var tasks = sources.Select(async source =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await scanner.CountSourceAsync(source, indexedTypes, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Indexing count failed for source '{source.DisplayName}': {ex}");
                ErrorLog.LogException(ex, "IndexService.CountAllAsync", $"Source={source.DisplayName}");
                return 0;
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var r in results)
            total += r;
        return total;
    }

    private async Task ProcessThumbnailQueueAsync(ChannelReader<MediaItem> reader, WorkCounters counters,
        CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
                continue;

            try
            {
                // Best-effort: generate and cache thumbnail file, UI will pick it up on next refresh.
                var thumbPath = await thumbnailService.EnsureThumbnailAsync(item.Path, item.MediaType, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(thumbPath))
                    await UpdateThumbnailPathAsync(item.Path, thumbPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Thumbnail generation failed for '{item.Path}': {ex}");
                ErrorLog.LogException(ex, "IndexService.ProcessThumbnailQueueAsync", $"Path={item.Path}");
            }
            finally
            {
                Interlocked.Increment(ref counters.ThumbsDone);
            }
        }
    }


    private static async ValueTask WriteToChannelAsync<T>(ChannelWriter<T> writer, T item, CancellationToken ct)
    {
        // Avoid per-item await stalls when the channel is not full by trying to write synchronously first.
        // If the channel is full, wait until space is available (backpressure) without dropping items.
        while (!writer.TryWrite(item))
            if (!await writer.WaitToWriteAsync(ct).ConfigureAwait(false))
                return;
    }

    private static async ValueTask QueueThumbnailAsync(
        Channel<MediaItem> thumbnailQueue,
        string path,
        MediaType mediaType,
        CancellationToken ct)
    {
        await WriteToChannelAsync(
                thumbnailQueue.Writer,
                new MediaItem
                {
                    Path = path,
                    MediaType = mediaType
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task ProcessLocationQueueAsync(
        ChannelReader<MediaItem> reader,
        WorkCounters counters,
        CancellationToken ct)
    {
        const int BatchSize = 128;

        var pending = new List<(string Path, double Lat, double Lon)>(BatchSize);

        async Task FlushAsync()
        {
            if (pending.Count == 0)
                return;

            var updates = pending.ToArray();
            pending.Clear();

            try
            {
                await db.Db.RunInTransactionAsync(conn =>
                {
                    // Reuse a prepared UPDATE command for all rows in this batch.
                    var cmd = conn.CreateCommand(
                        "UPDATE MediaItem SET Latitude = ?, Longitude = ? WHERE Path = ?;");

                    foreach (var u in updates)
                    {
                        cmd.Bind(u.Lat, u.Lon, u.Path);
                        cmd.ExecuteNonQuery();
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Location batch update failed: {ex}");
                ErrorLog.LogException(ex, "IndexService.ProcessLocationQueueAsync", "BatchUpdate");
            }
        }

        await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
                continue;

            try
            {
                if (!await locationMetadataService.TryPopulateLocationAsync(item, ct).ConfigureAwait(false))
                    continue;

                if (!item.Latitude.HasValue || !item.Longitude.HasValue)
                    continue;

                pending.Add((item.Path, item.Latitude.Value, item.Longitude.Value));

                if (pending.Count >= BatchSize)
                    await FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Location metadata update failed for '{item.Path}': {ex}");
                ErrorLog.LogException(ex, "IndexService.ProcessLocationQueueAsync", $"Path={item.Path}");
            }
            finally
            {
                Interlocked.Increment(ref counters.LocationsDone);
            }
        }

        await FlushAsync().ConfigureAwait(false);
    }

    private static async ValueTask QueueLocationLookupAsync(
        Channel<MediaItem>? locationQueue,
        string path,
        MediaType mediaType,
        CancellationToken ct)
    {
        if (locationQueue == null)
            return;

        await WriteToChannelAsync(
                locationQueue.Writer,
                new MediaItem
                {
                    Path = path,
                    MediaType = mediaType
                },
                ct)
            .ConfigureAwait(false);
    }

    private async Task ProcessDurationQueueAsync(
        ChannelReader<string> reader,
        WorkCounters counters,
        CancellationToken ct)
    {
        const int BatchSize = 256;

        var pending = new List<(string Path, long DurationMs)>(BatchSize);

        async Task FlushAsync()
        {
            if (pending.Count == 0)
                return;

            var updates = pending.ToArray();
            pending.Clear();

            try
            {
                await db.Db.RunInTransactionAsync(conn =>
                {
                    // Update only when DurationMs is missing/unknown to avoid overwriting better data.
                    var cmd = conn.CreateCommand(
                        "UPDATE MediaItem SET DurationMs = ? WHERE Path = ? AND (DurationMs IS NULL OR DurationMs <= 0);");

                    foreach (var u in updates)
                    {
                        cmd.Bind(u.DurationMs, u.Path);
                        cmd.ExecuteNonQuery();
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Duration batch update failed: {ex}");
                ErrorLog.LogException(ex, "IndexService.ProcessDurationQueueAsync", "BatchUpdate");
            }
        }

        await foreach (var path in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var durationMs = await videoDurationService.TryGetDurationMsAsync(path, ct).ConfigureAwait(false);
                if (durationMs <= 0)
                    continue;

                pending.Add((path, durationMs));

                if (pending.Count >= BatchSize)
                    await FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Duration update failed for '{path}': {ex}");
                ErrorLog.LogException(ex, "IndexService.ProcessDurationQueueAsync", $"Path={path}");
            }
            finally
            {
                Interlocked.Increment(ref counters.DurationsDone);
            }
        }

        await FlushAsync().ConfigureAwait(false);
    }

    public Task<List<MediaItem>> QueryAsync(string search, SearchScope searchScope, string? sourceId, string sortKey,
        DateTime? from, DateTime? to, MediaType mediaTypes, bool includeHidden)
    {
        return QueryAsyncInternal(search, searchScope, sourceId, sortKey, from, to, mediaTypes,
            includeHidden ? null : false);
    }

    public Task<List<MediaItem>> QueryPageAsync(string search, SearchScope searchScope, string? sourceId,
        string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit, bool includeHidden)
    {
        return QueryPageAsyncInternal(search, searchScope, sourceId, sortKey, from, to, mediaTypes, offset, limit,
            includeHidden ? null : false);
    }

    public Task<List<MediaItem>> QueryPageUniqueOldestAsync(string search, SearchScope searchScope, string? sourceId,
        string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit, bool includeHidden)
    {
        return QueryPageUniqueOldestAsyncInternal(search, searchScope, sourceId, sortKey, from, to, mediaTypes, offset,
            limit, includeHidden ? null : false);
    }

    public Task<int> CountQueryAsync(string search, SearchScope searchScope, string? sourceId, DateTime? from,
        DateTime? to, MediaType mediaTypes, bool includeHidden)
    {
        return CountQueryAsyncInternal(search, searchScope, sourceId, from, to, mediaTypes,
            includeHidden ? null : false);
    }

    public Task<int> CountQueryUniqueAsync(string search, SearchScope searchScope, string? sourceId, DateTime? from,
        DateTime? to, MediaType mediaTypes, bool includeHidden)
    {
        return CountQueryUniqueAsyncInternal(search, searchScope, sourceId, from, to, mediaTypes,
            includeHidden ? null : false);
    }

    public Task<int> CountHiddenAsync(string search, SearchScope searchScope, string? sourceId, DateTime? from,
        DateTime? to, MediaType mediaTypes)
    {
        return CountQueryAsyncInternal(search, searchScope, sourceId, from, to, mediaTypes, true);
    }

    public Task<int> CountHiddenUniqueAsync(string search, SearchScope searchScope, string? sourceId, DateTime? from,
        DateTime? to, MediaType mediaTypes)
    {
        return CountQueryUniqueAsyncInternal(search, searchScope, sourceId, from, to, mediaTypes, true);
    }

    public Task<int> CountAsync(MediaType mediaTypes)
    {
        return CountAsyncInternal(mediaTypes);
    }

    public async Task SetHiddenAsync(IEnumerable<MediaItem> items, bool isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var flag = isHidden ? 1 : 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                continue;

            await db.Db.ExecuteAsync(
                    "UPDATE MediaItem SET IsHidden = ? WHERE Path = ?;",
                    flag,
                    item.Path)
                .ConfigureAwait(false);
        }
    }

    public async Task<List<MediaItem>> QueryLocationsAsync(MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var allowed = BuildAllowedTypes(mediaTypes == MediaType.None ? MediaType.All : mediaTypes);
        if (allowed.Count == 0)
            return new List<MediaItem>();

        var placeholders = string.Join(",", allowed.Select(_ => "?"));
        var sql =
            $"SELECT * FROM MediaItem WHERE Latitude IS NOT NULL AND Longitude IS NOT NULL AND MediaType IN ({placeholders}) ORDER BY DateAddedSeconds DESC";
        return await db.Db.QueryAsync<MediaItem>(sql, allowed.Cast<object>().ToArray()).ConfigureAwait(false);
    }

    public async Task<int> CountLocationsAsync(MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var allowed = BuildAllowedTypes(mediaTypes == MediaType.None ? MediaType.All : mediaTypes);
        if (allowed.Count == 0)
            return 0;

        var placeholders = string.Join(",", allowed.Select(_ => "?"));
        var sql =
            $"SELECT COUNT(*) FROM MediaItem WHERE Latitude IS NOT NULL AND Longitude IS NOT NULL AND MediaType IN ({placeholders})";
        return await db.Db.ExecuteScalarAsync<int>(sql, allowed.Cast<object>().ToArray()).ConfigureAwait(false);
    }


    public async Task<int> BackfillLocationsAsync(MediaType mediaTypes, int maxItems, CancellationToken ct)
    {
        if (!locationMetadataService.IsEnabled || maxItems <= 0)
            return 0;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var allowed = BuildAllowedTypes(mediaTypes == MediaType.None ? MediaType.All : mediaTypes);
        if (allowed.Count == 0)
            return 0;

        var placeholders = string.Join(",", allowed.Select(_ => "?"));
        var sql =
            $"SELECT Path, MediaType, Latitude, Longitude FROM MediaItem " +
            $"WHERE (Latitude IS NULL OR Longitude IS NULL) AND MediaType IN ({placeholders}) " +
            "ORDER BY DateAddedSeconds DESC LIMIT ?;";

        var args = allowed.Cast<object>().ToList();
        args.Add(maxItems);

        var candidates = await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
        if (candidates.Count == 0)
            return 0;

        var updated = 0;
        foreach (var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (item == null || string.IsNullOrWhiteSpace(item.Path))
                continue;

            try
            {
                var hasLocation =
                    await locationMetadataService.TryPopulateLocationAsync(item, ct).ConfigureAwait(false);
                if (!hasLocation)
                    continue;

                await db.Db.ExecuteAsync(
                        "UPDATE MediaItem SET Latitude = ?, Longitude = ? WHERE Path = ?;",
                        item.Latitude,
                        item.Longitude,
                        item.Path)
                    .ConfigureAwait(false);

                updated++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "IndexService.BackfillLocationsAsync", $"Path={item.Path}");
            }
        }

        return updated;
    }

    public async Task RemoveAsync(IEnumerable<MediaItem> items)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                continue;

            await db.Db.DeleteAsync<MediaItem>(item.Path).ConfigureAwait(false);
            await db.Db.ExecuteAsync("DELETE FROM PersonTag WHERE MediaPath = ?;", item.Path)
                .ConfigureAwait(false);
            await db.Db.ExecuteAsync("DELETE FROM AlbumItem WHERE MediaPath = ?;", item.Path)
                .ConfigureAwait(false);
            await db.Db.DeleteAsync<FaceScanJob>(item.Path).ConfigureAwait(false);
            thumbnailService.DeleteThumbnailForPath(item.Path);
        }
    }

    public async Task<bool> RenameAsync(MediaItem item, string newPath, string newName)
    {
        if (item == null || string.IsNullOrWhiteSpace(newPath))
            return false;

        try
        {
            await db.EnsureInitializedAsync().ConfigureAwait(false);
            var existing = await db.Db.FindAsync<MediaItem>(newPath).ConfigureAwait(false);
            if (existing != null && !string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase))
                return false;

            var oldPath = item.Path;
            item.Path = newPath;
            item.Name = newName;
            await db.Db.InsertOrReplaceAsync(item).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(oldPath) &&
                !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                await db.Db.DeleteAsync<MediaItem>(oldPath).ConfigureAwait(false);
                await db.Db.ExecuteAsync(
                        "UPDATE PersonTag SET MediaPath = ? WHERE MediaPath = ?;",
                        newPath,
                        oldPath)
                    .ConfigureAwait(false);
                await db.Db.ExecuteAsync(
                        "UPDATE AlbumItem SET MediaPath = ? WHERE MediaPath = ?;",
                        newPath,
                        oldPath)
                    .ConfigureAwait(false);
                await db.Db.ExecuteAsync(
                        "UPDATE FaceScanJob SET MediaPath = ? WHERE MediaPath = ?;",
                        newPath,
                        oldPath)
                    .ConfigureAwait(false);
                thumbnailService.DeleteThumbnailForPath(oldPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<MediaType> BuildAllowedTypes(MediaType mediaTypes)
    {
        var allowed = new List<MediaType>();
        if (mediaTypes.HasFlag(MediaType.Videos))
            allowed.Add(MediaType.Videos);
        if (mediaTypes.HasFlag(MediaType.Photos))
            allowed.Add(MediaType.Photos);
        if (mediaTypes.HasFlag(MediaType.Graphics))
            allowed.Add(MediaType.Graphics);
        if (mediaTypes.HasFlag(MediaType.Documents))
            allowed.Add(MediaType.Documents);
        return allowed;
    }

    private async Task<List<MediaItem>> QueryAsyncInternal(string search, SearchScope searchScope, string? sourceId,
        string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, bool? isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, sortKey, from, to, mediaTypes, null, null,
            false, isHidden);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<List<MediaItem>> QueryPageAsyncInternal(string search, SearchScope searchScope,
        string? sourceId, string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit,
        bool? isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, sortKey, from, to, mediaTypes, offset, limit,
            false, isHidden);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<List<MediaItem>> QueryPageUniqueOldestAsyncInternal(string search, SearchScope searchScope,
        string? sourceId, string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit,
        bool? isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildUniqueOldestQuerySql(search, searchScope, sourceId, sortKey, from, to, mediaTypes,
            offset,
            limit, false, isHidden);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<int> CountQueryAsyncInternal(string search, SearchScope searchScope, string? sourceId,
        DateTime? from, DateTime? to, MediaType mediaTypes, bool? isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, "name", from, to, mediaTypes, null, null,
            true, isHidden);
        return await db.Db.ExecuteScalarAsync<int>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<int> CountQueryUniqueAsyncInternal(string search, SearchScope searchScope, string? sourceId,
        DateTime? from, DateTime? to, MediaType mediaTypes, bool? isHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildUniqueOldestQuerySql(search, searchScope, sourceId, "name", from, to, mediaTypes, null,
            null, true, isHidden);
        return await db.Db.ExecuteScalarAsync<int>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<int> CountAsyncInternal(MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var q = db.Db.Table<MediaItem>();
        var allowedTypes = BuildAllowedTypes(mediaTypes);
        if (allowedTypes.Count > 0)
            q = q.Where(v => allowedTypes.Contains(v.MediaType));
        return await q.CountAsync().ConfigureAwait(false);
    }

    private static (string sql, List<object> args) BuildQuerySql(
        string search,
        SearchScope searchScope,
        string? sourceId,
        string sortKey,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        int? offset,
        int? limit,
        bool countOnly,
        bool? isHidden)
    {
        var (filters, args) = BuildFilters(search, searchScope, sourceId, from, to, mediaTypes, isHidden);

        var sql = countOnly ? "SELECT COUNT(*) FROM MediaItem" : "SELECT MediaItem.* FROM MediaItem";

        if (filters.Count > 0)
            sql += " WHERE " + string.Join(" AND ", filters);

        if (!countOnly)
        {
            var orderBy = sortKey switch
            {
                "date" => "MediaItem.DateAddedSeconds DESC",
                "duration" => "MediaItem.DurationMs DESC",
                _ => "MediaItem.Name"
            };

            sql += $" ORDER BY {orderBy}";

            if (limit.HasValue)
            {
                sql += " LIMIT ?";
                args.Add(limit.Value);
            }

            if (offset.HasValue)
            {
                sql += " OFFSET ?";
                args.Add(offset.Value);
            }
        }

        return (sql, args);
    }

    private static (string sql, List<object> args) BuildUniqueOldestQuerySql(
        string search,
        SearchScope searchScope,
        string? sourceId,
        string sortKey,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        int? offset,
        int? limit,
        bool countOnly,
        bool? isHidden)
    {
        var (filters, args) = BuildFilters(search, searchScope, sourceId, from, to, mediaTypes, isHidden);
        var filterSql = filters.Count > 0 ? " WHERE " + string.Join(" AND ", filters) : string.Empty;

        var orderBy = sortKey switch
        {
            "date" => "DateAddedSeconds DESC",
            "duration" => "DurationMs DESC",
            _ => "Name"
        };

        var selectColumns =
            "Path, Name, MediaType, DurationMs, DateAddedSeconds, SourceId, Latitude, Longitude, ThumbnailPath, PeopleTagsSummary, FaceScanModelKey, FaceScanAtSeconds, IsHidden";

        if (countOnly)
        {
            var countSql = $@"
WITH Filtered AS (
    SELECT {selectColumns} FROM MediaItem{filterSql}
),
Ranked AS (
    SELECT Filtered.*, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY DateAddedSeconds ASC, Path ASC) AS rn
    FROM Filtered
)
SELECT COUNT(*) FROM Ranked WHERE rn = 1";
            return (countSql, args);
        }

        var sql = $@"
WITH Filtered AS (
    SELECT {selectColumns} FROM MediaItem{filterSql}
),
Ranked AS (
    SELECT Filtered.*, ROW_NUMBER() OVER (PARTITION BY Name ORDER BY DateAddedSeconds ASC, Path ASC) AS rn
    FROM Filtered
)
SELECT {selectColumns} FROM Ranked WHERE rn = 1 ORDER BY {orderBy}";

        if (limit.HasValue)
        {
            sql += " LIMIT ?";
            args.Add(limit.Value);
        }

        if (offset.HasValue)
        {
            sql += " OFFSET ?";
            args.Add(offset.Value);
        }

        return (sql, args);
    }

    private static (List<string> filters, List<object> args) BuildFilters(
        string search,
        SearchScope searchScope,
        string? sourceId,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        bool? isHidden)
    {
        var args = new List<object>();
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            filters.Add("MediaItem.SourceId = ?");
            args.Add(sourceId);
        }

        var allowedTypes = BuildAllowedTypes(mediaTypes);
        if (allowedTypes.Count > 0)
        {
            filters.Add($"MediaItem.MediaType IN ({string.Join(",", allowedTypes.Select(_ => "?"))})");
            args.AddRange(allowedTypes.Cast<object>());
        }

        var trimmedSearch = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(trimmedSearch))
        {
            if (searchScope == SearchScope.None)
            {
                filters.Add("1 = 0");
            }
            else
            {
                var searchFilters = new List<string>();
                var like = $"%{trimmedSearch}%";

                if (searchScope.HasFlag(SearchScope.Name))
                {
                    searchFilters.Add("MediaItem.Name LIKE ?");
                    args.Add(like);
                }

                if (searchScope.HasFlag(SearchScope.People))
                {
                    searchFilters.Add(
                        "EXISTS (SELECT 1 FROM PersonTag WHERE PersonTag.MediaPath = MediaItem.Path AND PersonTag.PersonName LIKE ?)");
                    args.Add(like);
                }

                if (searchScope.HasFlag(SearchScope.Albums))
                {
                    searchFilters.Add(
                        "EXISTS (SELECT 1 FROM AlbumItem INNER JOIN Album ON Album.Id = AlbumItem.AlbumId WHERE AlbumItem.MediaPath = MediaItem.Path AND Album.Name LIKE ?)");
                    args.Add(like);
                }

                if (searchFilters.Count > 0)
                    filters.Add("(" + string.Join(" OR ", searchFilters) + ")");
            }
        }

        if (from.HasValue || to.HasValue)
        {
            var fromSeconds = from.HasValue ? new DateTimeOffset(from.Value.Date).ToUnixTimeSeconds() : 0;
            var toSeconds = to.HasValue
                ? new DateTimeOffset(to.Value.Date.AddDays(1).AddTicks(-1)).ToUnixTimeSeconds()
                : long.MaxValue;

            filters.Add("MediaItem.DateAddedSeconds >= ? AND MediaItem.DateAddedSeconds <= ?");
            args.Add(fromSeconds);
            args.Add(toSeconds);
        }

        if (isHidden.HasValue)
        {
            filters.Add("MediaItem.IsHidden = ?");
            args.Add(isHidden.Value ? 1 : 0);
        }

        return (filters, args);
    }

    private sealed class WorkCounters
    {
        public int DurationsDone;
        public int DurationsQueued;
        public int LocationsDone;
        public int LocationsQueued;
        public int ThumbsDone;
        public int ThumbsQueued;
    }

    private sealed class MediaItemSignatureRow
    {
        public string? Path { get; set; }
        public long? SizeBytes { get; set; }
    }

    private sealed class ProgressScheduler
    {
        private readonly int minReportMs;
        private readonly IProgress<IndexProgress>? progress;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private IndexProgress? pending;

        public ProgressScheduler(IProgress<IndexProgress>? progress, int minReportMs)
        {
            this.progress = progress;
            this.minReportMs = Math.Max(1, minReportMs);
        }

        public void Report(IndexProgress value, bool force = false)
        {
            if (progress == null)
                return;

            if (force || stopwatch.ElapsedMilliseconds >= minReportMs)
            {
                pending = null;
                stopwatch.Restart();
                TryReport(value);
            }
            else
            {
                pending = value;
            }
        }

        public void Flush()
        {
            if (pending != null)
                Report(pending, true);
        }

        private void TryReport(IndexProgress value)
        {
            try
            {
                progress?.Report(value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Progress handler crashed: {ex}");
            }
        }
    }
}
