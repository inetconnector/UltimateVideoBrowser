using System.Diagnostics;
using SQLite;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class IndexService
{
    private readonly AppDb db;
    private readonly LocationMetadataService locationMetadataService;

    // Prevent concurrent indexing runs across the whole app instance
    private readonly SemaphoreSlim indexGate = new(1, 1);
    private readonly MediaStoreScanner scanner;

    public IndexService(AppDb db, MediaStoreScanner scanner, LocationMetadataService locationMetadataService)
    {
        this.db = db;
        this.scanner = scanner;
        this.locationMetadataService = locationMetadataService;
    }

    public async Task<int> IndexSourcesAsync(
        IEnumerable<MediaSource> sources,
        MediaType indexedTypes,
        IProgress<IndexProgress>? progress,
        CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await indexGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var inserted = 0;
            var processedOverall = 0;
            var totalOverall = 0;

            var sw = Stopwatch.StartNew();
            const int minReportMs = 150;

            void SafeReport(IndexProgress p)
            {
                if (progress == null) return;
                try
                {
                    progress.Report(p);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Progress handler crashed: {ex}");
                    // Swallow to avoid bringing the app down
                }
            }

            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();

                SafeReport(new IndexProgress(
                    processedOverall,
                    totalOverall,
                    inserted,
                    source.DisplayName ?? "",
                    "")); // never null

                try
                {
                    await foreach (var v in scanner.StreamSourceAsync(source, indexedTypes, ct).ConfigureAwait(false))
                    {
                        ct.ThrowIfCancellationRequested();

                        totalOverall++;

                        // Throttle progress updates to protect UI thread
                        if (sw.ElapsedMilliseconds >= minReportMs)
                        {
                            sw.Restart();
                            SafeReport(new IndexProgress(
                                processedOverall,
                                totalOverall,
                                inserted,
                                source.DisplayName ?? "",
                                v.Path ?? ""));
                        }

                        try
                        {
                            // If v.Path can be null/empty, skip early
                            if (string.IsNullOrWhiteSpace(v.Path))
                                continue;

                            var exists = await db.Db.FindAsync<MediaItem>(v.Path).ConfigureAwait(false);
                            if (exists == null)
                            {
                                await locationMetadataService.TryPopulateLocationAsync(v, ct).ConfigureAwait(false);
                                await db.Db.InsertAsync(v).ConfigureAwait(false);
                                inserted++;
                            }
                            else
                            {
                                if ((!exists.Latitude.HasValue || !exists.Longitude.HasValue) &&
                                    (v.MediaType == MediaType.Photos || v.MediaType == MediaType.Videos))
                                {
                                    await locationMetadataService.TryPopulateLocationAsync(v, ct)
                                        .ConfigureAwait(false);
                                }

                                if (exists.Name != v.Name ||
                                    exists.DurationMs != v.DurationMs ||
                                    exists.MediaType != v.MediaType ||
                                    exists.SourceId != v.SourceId ||
                                    exists.DateAddedSeconds != v.DateAddedSeconds ||
                                    exists.Latitude != v.Latitude ||
                                    exists.Longitude != v.Longitude)
                                {
                                    exists.Name = v.Name;
                                    exists.DurationMs = v.DurationMs;
                                    exists.MediaType = v.MediaType;
                                    exists.SourceId = v.SourceId;
                                    exists.DateAddedSeconds = v.DateAddedSeconds;
                                    if (v.Latitude.HasValue)
                                        exists.Latitude = v.Latitude;
                                    if (v.Longitude.HasValue)
                                        exists.Longitude = v.Longitude;
                                    await db.Db.UpdateAsync(exists).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Indexing skipped item '{v?.Path}': {ex}");
                        }

                        processedOverall++;

                        // Final per-item report can also be throttled; keep it safe
                        if (sw.ElapsedMilliseconds >= minReportMs)
                        {
                            sw.Restart();
                            SafeReport(new IndexProgress(
                                processedOverall,
                                totalOverall,
                                inserted,
                                source.DisplayName ?? "",
                                v.Path ?? ""));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Indexing failed for source '{source.DisplayName}': {ex}");
                }
            }

            // One last report at the end (not throttled)
            SafeReport(new IndexProgress(processedOverall, totalOverall, inserted, "", ""));

            return inserted;
        }
        finally
        {
            indexGate.Release();
        }
    }

    public Task<List<MediaItem>> QueryAsync(string search, string? sourceId, string sortKey, DateTime? from,
        DateTime? to, MediaType mediaTypes)
    {
        return QueryAsyncInternal(search, sourceId, sortKey, from, to, mediaTypes);
    }

    public Task<List<MediaItem>> QueryPageAsync(string search, string? sourceId, string sortKey, DateTime? from,
        DateTime? to, MediaType mediaTypes, int offset, int limit)
    {
        return QueryPageAsyncInternal(search, sourceId, sortKey, from, to, mediaTypes, offset, limit);
    }

    public Task<int> CountQueryAsync(string search, string? sourceId, DateTime? from, DateTime? to,
        MediaType mediaTypes)
    {
        return CountQueryAsyncInternal(search, sourceId, from, to, mediaTypes);
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
        if (mediaTypes.HasFlag(MediaType.Documents))
            allowed.Add(MediaType.Documents);
        return allowed;
    }

    private async Task<List<MediaItem>> QueryAsyncInternal(string search, string? sourceId, string sortKey,
        DateTime? from, DateTime? to, MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var q = BuildQuery(search, sourceId, sortKey, from, to, mediaTypes);
        return await q.ToListAsync().ConfigureAwait(false);
    }

    private async Task<List<MediaItem>> QueryPageAsyncInternal(string search, string? sourceId, string sortKey,
        DateTime? from, DateTime? to, MediaType mediaTypes, int offset, int limit)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var q = BuildQuery(search, sourceId, sortKey, from, to, mediaTypes)
            .Skip(offset)
            .Take(limit);
        return await q.ToListAsync().ConfigureAwait(false);
    }

    private async Task<int> CountQueryAsyncInternal(string search, string? sourceId, DateTime? from, DateTime? to,
        MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var q = BuildQuery(search, sourceId, "name", from, to, mediaTypes);
        return await q.CountAsync().ConfigureAwait(false);
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

    private AsyncTableQuery<MediaItem> BuildQuery(string search, string? sourceId, string sortKey, DateTime? from,
        DateTime? to, MediaType mediaTypes)
    {
        var q = db.Db.Table<MediaItem>();
        var allowedTypes = BuildAllowedTypes(mediaTypes);
        if (allowedTypes.Count > 0)
            q = q.Where(v => allowedTypes.Contains(v.MediaType));

        if (!string.IsNullOrWhiteSpace(sourceId))
            q = q.Where(v => v.SourceId == sourceId);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(v => v.Name.Contains(search));

        if (from.HasValue || to.HasValue)
        {
            var fromSeconds = from.HasValue ? new DateTimeOffset(from.Value.Date).ToUnixTimeSeconds() : 0;
            var toSeconds = to.HasValue
                ? new DateTimeOffset(to.Value.Date.AddDays(1).AddTicks(-1)).ToUnixTimeSeconds()
                : long.MaxValue;

            q = q.Where(v => v.DateAddedSeconds >= fromSeconds && v.DateAddedSeconds <= toSeconds);
        }

        q = sortKey switch
        {
            "date" => q.OrderByDescending(v => v.DateAddedSeconds),
            "duration" => q.OrderByDescending(v => v.DurationMs),
            _ => q.OrderBy(v => v.Name)
        };

        return q;
    }
}
