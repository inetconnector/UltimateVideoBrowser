using System.Diagnostics;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class IndexService
{
    private readonly AppDb db;

    // Prevent concurrent indexing runs across the whole app instance
    private readonly SemaphoreSlim indexGate = new(1, 1);
    private readonly MediaStoreScanner scanner;

    public IndexService(AppDb db, MediaStoreScanner scanner)
    {
        this.db = db;
        this.scanner = scanner;
    }

    public async Task<int> IndexSourcesAsync(
        IEnumerable<MediaSource> sources,
        IProgress<IndexProgress>? progress,
        CancellationToken ct)
    {
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
                    await foreach (var v in scanner.StreamSourceAsync(source, ct).ConfigureAwait(false))
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

                            var exists = await db.Db.FindAsync<VideoItem>(v.Path).ConfigureAwait(false);
                            if (exists == null)
                            {
                                await db.Db.InsertAsync(v).ConfigureAwait(false);
                                inserted++;
                            }
                            else
                            {
                                if (exists.Name != v.Name ||
                                    exists.DurationMs != v.DurationMs ||
                                    exists.SourceId != v.SourceId ||
                                    exists.DateAddedSeconds != v.DateAddedSeconds)
                                {
                                    exists.Name = v.Name;
                                    exists.DurationMs = v.DurationMs;
                                    exists.SourceId = v.SourceId;
                                    exists.DateAddedSeconds = v.DateAddedSeconds;
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

    public Task<List<VideoItem>> QueryAsync(string search, string? sourceId, string sortKey, DateTime? from,
        DateTime? to)
    {
        var q = db.Db.Table<VideoItem>();

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

        return q.ToListAsync();
    }

    public Task<int> CountAsync()
    {
        return db.Db.Table<VideoItem>().CountAsync();
    }

    public async Task RemoveAsync(IEnumerable<VideoItem> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                continue;

            await db.Db.DeleteAsync<VideoItem>(item.Path).ConfigureAwait(false);
        }
    }

    public async Task<bool> RenameAsync(VideoItem item, string newPath, string newName)
    {
        if (item == null || string.IsNullOrWhiteSpace(newPath))
            return false;

        try
        {
            var existing = await db.Db.FindAsync<VideoItem>(newPath).ConfigureAwait(false);
            if (existing != null && !string.Equals(existing.Path, item.Path, StringComparison.OrdinalIgnoreCase))
                return false;

            var oldPath = item.Path;
            item.Path = newPath;
            item.Name = newName;
            await db.Db.InsertOrReplaceAsync(item).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(oldPath) &&
                !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                await db.Db.DeleteAsync<VideoItem>(oldPath).ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
