using SQLite;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.Services;

public sealed record PersonOverview(
    string Id,
    string Name,
    int PhotoCount,
    FaceEmbedding? CoverFace);

public sealed record FaceTagInfo(
    int FaceIndex,
    string PersonId,
    string PersonName,
    FaceEmbedding Embedding);

public sealed class PeopleDataService
{
    private readonly AppDb db;
    private readonly PeopleRecognitionService recognitionService;

    public PeopleDataService(AppDb db, PeopleRecognitionService recognitionService)
    {
        this.db = db;
        this.recognitionService = recognitionService;
    }

    private sealed class PersonCountRow
    {
        public string PersonId { get; set; } = string.Empty;
        public int Cnt { get; set; }
    }

    private sealed class MediaPathRow
    {
        public string MediaPath { get; set; } = string.Empty;
    }

    public async Task<IReadOnlyList<PersonOverview>> GetPeopleOverviewAsync(string? search, CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);

        var counts = await db.Db.QueryAsync<PersonCountRow>(
                "SELECT PersonId, COUNT(DISTINCT MediaPath) AS Cnt FROM FaceEmbedding " +
                "WHERE PersonId IS NOT NULL AND PersonId <> '' GROUP BY PersonId;")
            .ConfigureAwait(false);
        var countMap = counts
            .Where(r => !string.IsNullOrWhiteSpace(r.PersonId))
            .ToDictionary(r => r.PersonId, r => r.Cnt, StringComparer.OrdinalIgnoreCase);

        // Fetch all face embeddings once; pick the highest-score row per person as cover.
        var allEmbeddings = await db.Db.QueryAsync<FaceEmbedding>(
                "SELECT * FROM FaceEmbedding WHERE PersonId IS NOT NULL AND PersonId <> '' ORDER BY PersonId, Score DESC;")
            .ConfigureAwait(false);
        var coverMap = new Dictionary<string, FaceEmbedding>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in allEmbeddings)
        {
            if (string.IsNullOrWhiteSpace(e.PersonId))
                continue;
            if (!coverMap.ContainsKey(e.PersonId!))
                coverMap[e.PersonId!] = e;
        }

        var normalizedSearch = (search ?? string.Empty).Trim();
        var filtered = people
            .Where(p => countMap.ContainsKey(p.Id))
            .Where(p =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .Select(p => new PersonOverview(
                p.Id,
                p.Name,
                countMap.TryGetValue(p.Id, out var c) ? c : 0,
                coverMap.TryGetValue(p.Id, out var cover) ? cover : null))
            .OrderByDescending(p => p.PhotoCount)
            .ThenBy(p => p.Name)
            .ToList();

        return filtered;
    }

    public async Task<IReadOnlyList<MediaItem>> GetMediaForPersonAsync(string personId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return Array.Empty<MediaItem>();

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var rows = await db.Db.QueryAsync<MediaPathRow>(
                "SELECT DISTINCT MediaPath FROM FaceEmbedding WHERE PersonId = ?;",
                personId)
            .ConfigureAwait(false);

        var paths = rows
            .Select(r => r.MediaPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            return Array.Empty<MediaItem>();

        // SQLite has a practical limit on the number of parameters in an IN (...) list.
        // Chunking keeps this fast and robust.
        var items = new List<MediaItem>();
        const int chunkSize = 400;
        for (var i = 0; i < paths.Count; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = paths.Skip(i).Take(chunkSize).ToList();
            var placeholders = string.Join(",", chunk.Select(_ => "?"));
            var sql = $"SELECT * FROM MediaItem WHERE Path IN ({placeholders}) AND MediaType = ? ORDER BY DateAddedSeconds DESC;";
            var args = chunk.Cast<object>().ToList();
            args.Add((int)MediaType.Photos);
            var result = await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
            items.AddRange(result);
        }

        return items;
    }

    public async Task<IReadOnlyList<FaceTagInfo>> GetFacesForMediaAsync(string mediaPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return Array.Empty<FaceTagInfo>();

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var embeddings = await db.Db.Table<FaceEmbedding>()
            .Where(e => e.MediaPath == mediaPath)
            .OrderBy(e => e.FaceIndex)
            .ToListAsync()
            .ConfigureAwait(false);

        if (embeddings.Count == 0)
            return Array.Empty<FaceTagInfo>();

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
        var peopleMap = people.ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);

        var list = embeddings
            .Where(e => !string.IsNullOrWhiteSpace(e.PersonId))
            .Select(e => new FaceTagInfo(
                e.FaceIndex,
                e.PersonId!,
                peopleMap.TryGetValue(e.PersonId!, out var name) ? name : string.Empty,
                e))
            .ToList();

        return list;
    }

    public Task RenamePersonAsync(string personId, string newName, CancellationToken ct)
    {
        return recognitionService.RenamePersonAsync(personId, newName, ct);
    }
}
