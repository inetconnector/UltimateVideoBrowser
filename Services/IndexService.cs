using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class IndexService
{
    readonly AppDb db;
    readonly MediaStoreScanner scanner;

    public IndexService(AppDb db, MediaStoreScanner scanner)
    {
        this.db = db;
        this.scanner = scanner;
    }

    public async Task<int> IndexDeviceMediaStoreAsync(string? sourceId, IProgress<int>? progress, CancellationToken ct)
    {
        // Incremental by Path (primary key)
        var scanned = await scanner.ScanAllVideosAsync(sourceId);
        var inserted = 0;

        foreach (var v in scanned)
        {
            ct.ThrowIfCancellationRequested();

            var exists = await db.Db.FindAsync<VideoItem>(v.Path);
            if (exists == null)
            {
                await db.Db.InsertAsync(v);
                inserted++;
            }
            else
            {
                // Update name/duration if changed (cheap)
                if (exists.Name != v.Name || exists.DurationMs != v.DurationMs || exists.SourceId != v.SourceId)
                {
                    exists.Name = v.Name;
                    exists.DurationMs = v.DurationMs;
                    exists.SourceId = v.SourceId;
                    exists.DateAddedSeconds = v.DateAddedSeconds;
                    await db.Db.UpdateAsync(exists);
                }
            }

            progress?.Report(inserted);
        }

        return inserted;
    }

    public Task<List<VideoItem>> QueryAsync(string search, string? sourceId, string sortKey)
    {
        var q = db.Db.Table<VideoItem>();

        if (!string.IsNullOrWhiteSpace(sourceId))
            q = q.Where(v => v.SourceId == sourceId);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(v => v.Name.Contains(search));

        q = sortKey switch
        {
            "date" => q.OrderByDescending(v => v.DateAddedSeconds),
            "duration" => q.OrderByDescending(v => v.DurationMs),
            _ => q.OrderBy(v => v.Name)
        };

        return q.ToListAsync();
    }
}
