using System.Diagnostics;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services.Faces;

public sealed class FaceScanQueueService
{
    private readonly AppDb db;
    private readonly IndexService indexService;
    private readonly PeopleRecognitionService peopleRecognitionService;

    public FaceScanQueueService(
        AppDb db,
        IndexService indexService,
        PeopleRecognitionService peopleRecognitionService)
    {
        this.db = db;
        this.indexService = indexService;
        this.peopleRecognitionService = peopleRecognitionService;
    }

    public async Task EnqueuePhotosForScanAsync(string? sourceId, string sortKey, DateTime? from, DateTime? to,
        CancellationToken ct)
    {
        var offset = 0;
        const int batchSize = 200;

        while (!ct.IsCancellationRequested)
        {
            var page = await indexService
                .QueryPageAsync("", SearchScope.All, sourceId, sortKey, from, to, MediaType.Photos | MediaType.Graphics, offset, batchSize)
                .ConfigureAwait(false);

            if (page.Count == 0)
                break;

            await EnqueueMediaPathsAsync(page.Select(p => p.Path), ct).ConfigureAwait(false);

            offset += page.Count;
            if (page.Count < batchSize)
                break;
        }
    }

    public async Task EnqueueMediaPathsAsync(IEnumerable<string?> mediaPaths, CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var path in mediaPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            await db.Db.ExecuteAsync(
                    "INSERT OR IGNORE INTO FaceScanJob (MediaPath, EnqueuedAtSeconds, LastAttemptSeconds, AttemptCount) " +
                    "VALUES (?, ?, ?, ?);",
                    path,
                    now,
                    0,
                    0)
                .ConfigureAwait(false);
        }
    }

    public async Task<int> ProcessQueueAsync(CancellationToken ct, IProgress<int>? progress = null)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);

        var processed = 0;
        const int batchSize = 50;

        while (!ct.IsCancellationRequested)
        {
            var jobs = await db.Db
                .QueryAsync<FaceScanJob>(
                    "SELECT * FROM FaceScanJob ORDER BY EnqueuedAtSeconds ASC LIMIT ?;",
                    batchSize)
                .ConfigureAwait(false);

            if (jobs.Count == 0)
                break;

            foreach (var job in jobs)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(job.MediaPath))
                {
                    await db.Db.DeleteAsync<FaceScanJob>(job.MediaPath).ConfigureAwait(false);
                    continue;
                }

                var item = await db.Db.FindAsync<MediaItem>(job.MediaPath).ConfigureAwait(false);
                if (item == null)
                {
                    await db.Db.DeleteAsync<FaceScanJob>(job.MediaPath).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await peopleRecognitionService.EnsurePeopleTagsForMediaAsync(item, ct).ConfigureAwait(false);
                    await db.Db.DeleteAsync<FaceScanJob>(job.MediaPath).ConfigureAwait(false);
                    processed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FaceScanQueue] Processing failed for {job.MediaPath}: {ex}");
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    await db.Db.ExecuteAsync(
                            "UPDATE FaceScanJob SET AttemptCount = AttemptCount + 1, LastAttemptSeconds = ? " +
                            "WHERE MediaPath = ?;",
                            now,
                            job.MediaPath)
                        .ConfigureAwait(false);
                }
            }

            progress?.Report(processed);
        }

        return processed;
    }
}
