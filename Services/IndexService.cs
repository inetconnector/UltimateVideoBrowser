using System.Diagnostics;
using System.Threading.Channels;
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

    public IndexService(AppDb db, MediaStoreScanner scanner, LocationMetadataService locationMetadataService,
        ThumbnailService thumbnailService)
    {
        this.db = db;
        this.scanner = scanner;
        this.locationMetadataService = locationMetadataService;
        this.thumbnailService = thumbnailService;
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
        try
        {
            var inserted = 0;
            var processedOverall = 0;
            var totalOverall = 0;
            var locationsEnabled = locationMetadataService.IsEnabled;

            const int BatchSize = 64;

            async Task<int> UpsertBatchAsync(List<MediaItem> items)
            {
                if (items.Count == 0)
                    return 0;

                var insertedRows = 0;

                // Batch DB operations in a single transaction to dramatically reduce async overhead.
                await db.Db.RunInTransactionAsync(conn =>
                {
                    foreach (var item in items)
                    {
                        if (item == null || string.IsNullOrWhiteSpace(item.Path))
                            continue;

                        var rows = conn.Execute(
                            "INSERT OR IGNORE INTO MediaItem(Path, Name, MediaType, DurationMs, DateAddedSeconds, SourceId) VALUES(?, ?, ?, ?, ?, ?);",
                            item.Path,
                            item.Name,
                            (int)item.MediaType,
                            item.DurationMs,
                            item.DateAddedSeconds,
                            item.SourceId);

                        if (rows == 0)
                        {
                            // Keep existing ThumbnailPath/PeopleTagsSummary/etc. intact.
                            conn.Execute(
                                "UPDATE MediaItem SET Name = ?, MediaType = ?, DurationMs = ?, DateAddedSeconds = ?, SourceId = ? WHERE Path = ?;",
                                item.Name,
                                (int)item.MediaType,
                                item.DurationMs,
                                item.DateAddedSeconds,
                                item.SourceId,
                                item.Path);
                        }

                        insertedRows += rows;
                    }
                }).ConfigureAwait(false);

                if (locationsEnabled && locationQueue != null)
                {
                    // Keep location enrichment functional. We allow duplicates; the worker de-duplicates by DB update.
                    foreach (var item in items)
                    {
                        if (item?.MediaType is MediaType.Photos or MediaType.Videos && !string.IsNullOrWhiteSpace(item.Path))
                            await QueueLocationLookupAsync(locationQueue, item.Path, item.MediaType, ct)
                                .ConfigureAwait(false);
                    }
                }

                return insertedRows;
            }

            if (locationsEnabled)
            {
                locationQueue = Channel.CreateBounded<MediaItem>(new BoundedChannelOptions(128)
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });

                var workerCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
                locationWorkers = Enumerable.Range(0, workerCount)
                    .Select(_ => Task.Run(() => ProcessLocationQueueAsync(locationQueue.Reader, ct), ct))
                    .ToList();
            }

            var scheduler = new ProgressScheduler(progress, 150);

            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                var batch = new List<MediaItem>(BatchSize);

                try
                {
                    totalOverall += await scanner.CountSourceAsync(source, indexedTypes, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Indexing count failed for source '{source.DisplayName}': {ex}");
                }

                scheduler.Report(new IndexProgress(
                        processedOverall,
                        totalOverall,
                        inserted,
                        source.DisplayName ?? "",
                        ""),
                    true);

                try
                {
                    await foreach (var v in scanner.StreamSourceAsync(source, indexedTypes, ct).ConfigureAwait(false))
                    {
                        ct.ThrowIfCancellationRequested();

                        // Collect into a batch. We intentionally keep the per-item code minimal.
                        if (!string.IsNullOrWhiteSpace(v.Path))
                        {
                            batch.Add(v);
                            if (batch.Count >= BatchSize)
                            {
                                inserted += await UpsertBatchAsync(batch).ConfigureAwait(false);
                                batch.Clear();
                            }
                        }

                        processedOverall++;

                        // Throttle progress updates to protect UI thread
                        scheduler.Report(new IndexProgress(
                            processedOverall,
                            totalOverall,
                            inserted,
                            source.DisplayName ?? "",
                            v.Path ?? ""));
                    }

                    // Flush remaining items for this source.
                    if (batch.Count > 0)
                    {
                        inserted += await UpsertBatchAsync(batch).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Indexing failed for source '{source.DisplayName}': {ex}");
                }
            }

            // One last report at the end (not throttled)
            scheduler.Report(new IndexProgress(processedOverall, totalOverall, inserted, "", ""), true);
            scheduler.Flush();

            return inserted;
        }
        finally
        {
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

    private async Task ProcessLocationQueueAsync(ChannelReader<MediaItem> reader, CancellationToken ct)
    {
        await foreach (var item in reader.ReadAllAsync(ct).ConfigureAwait(false))
            try
            {
                if (!await locationMetadataService.TryPopulateLocationAsync(item, ct).ConfigureAwait(false))
                    continue;

                if (!item.Latitude.HasValue || !item.Longitude.HasValue || string.IsNullOrWhiteSpace(item.Path))
                    continue;

                await db.Db.ExecuteAsync(
                        "UPDATE MediaItem SET Latitude = ?, Longitude = ? WHERE Path = ?",
                        item.Latitude,
                        item.Longitude,
                        item.Path)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Location metadata update failed for '{item.Path}': {ex}");
            }
    }

    private static ValueTask QueueLocationLookupAsync(Channel<MediaItem>? locationQueue, string path,
        MediaType mediaType, CancellationToken ct)
    {
        if (locationQueue == null)
            return ValueTask.CompletedTask;

        return locationQueue.Writer.WriteAsync(new MediaItem
        {
            Path = path,
            MediaType = mediaType
        }, ct);
    }

    public Task<List<MediaItem>> QueryAsync(string search, SearchScope searchScope, string? sourceId, string sortKey,
        DateTime? from, DateTime? to, MediaType mediaTypes)
    {
        return QueryAsyncInternal(search, searchScope, sourceId, sortKey, from, to, mediaTypes);
    }

    public Task<List<MediaItem>> QueryPageAsync(string search, SearchScope searchScope, string? sourceId,
        string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit)
    {
        return QueryPageAsyncInternal(search, searchScope, sourceId, sortKey, from, to, mediaTypes, offset, limit);
    }

    public Task<int> CountQueryAsync(string search, SearchScope searchScope, string? sourceId, DateTime? from,
        DateTime? to, MediaType mediaTypes)
    {
        return CountQueryAsyncInternal(search, searchScope, sourceId, from, to, mediaTypes);
    }

    public Task<int> CountAsync(MediaType mediaTypes)
    {
        return CountAsyncInternal(mediaTypes);
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
        string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, sortKey, from, to, mediaTypes, null, null,
            false);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<List<MediaItem>> QueryPageAsyncInternal(string search, SearchScope searchScope,
        string? sourceId, string sortKey, DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, sortKey, from, to, mediaTypes, offset, limit,
            false);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private async Task<int> CountQueryAsyncInternal(string search, SearchScope searchScope, string? sourceId,
        DateTime? from, DateTime? to, MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildQuerySql(search, searchScope, sourceId, "name", from, to, mediaTypes, null, null,
            true);
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
        bool countOnly)
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