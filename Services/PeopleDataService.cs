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

    public async Task<IReadOnlyList<PersonOverview>> GetPeopleOverviewAsync(string? search, CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var normalizedSearch = (search ?? string.Empty).Trim();

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);

        var counts = await db.Db.QueryAsync<PersonCountRow>(
                "SELECT PersonId, COUNT(DISTINCT MediaPath) AS Cnt FROM FaceEmbedding " +
                "WHERE PersonId IS NOT NULL AND PersonId <> '' GROUP BY PersonId;")
            .ConfigureAwait(false);
        var countMap = counts
            .Where(r => !string.IsNullOrWhiteSpace(r.PersonId))
            .ToDictionary(r => r.PersonId, r => r.Cnt, StringComparer.OrdinalIgnoreCase);

        // Manual tags (PersonTag table) are independent from face recognition. Surface them as well.
        // In addition, keep the People page resilient by also considering MediaItem.PeopleTagsSummary
        // (in case tags exist there but the PersonTag table is out of sync for any reason).
        var tagCountMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var tagCounts = await db.Db.QueryAsync<TagCountRow>(
                    "SELECT PersonName, COUNT(DISTINCT MediaPath) AS Cnt FROM PersonTag " +
                    "WHERE PersonName IS NOT NULL AND PersonName <> '' GROUP BY PersonName;")
                .ConfigureAwait(false);

            foreach (var r in tagCounts)
            {
                if (string.IsNullOrWhiteSpace(r.PersonName))
                    continue;
                tagCountMap[r.PersonName] = r.Cnt;
            }
        }
        catch
        {
            // Keep UI resilient.
        }

        try
        {
            var rows = await db.Db.QueryAsync<MediaTagRow>(
                    "SELECT Path, PeopleTagsSummary FROM MediaItem WHERE PeopleTagsSummary IS NOT NULL AND PeopleTagsSummary <> '';"
                ).ConfigureAwait(false);

            // Count distinct media paths per person name.
            var pathSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Path) || string.IsNullOrWhiteSpace(row.PeopleTagsSummary))
                    continue;

                var parts = row.PeopleTagsSummary
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var p in parts)
                {
                    var name = (p ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!pathSets.TryGetValue(name, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        pathSets[name] = set;
                    }

                    set.Add(row.Path);
                }
            }

            foreach (var kvp in pathSets)
            {
                var cnt = kvp.Value.Count;
                if (cnt <= 0)
                    continue;

                // Merge by taking the max (PersonTag is authoritative when present, but summary keeps us safe).
                if (tagCountMap.TryGetValue(kvp.Key, out var existing))
                    tagCountMap[kvp.Key] = Math.Max(existing, cnt);
                else
                    tagCountMap[kvp.Key] = cnt;
            }
        }
        catch
        {
            // Keep UI resilient.
        }

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

        var facePeople = people
            .Where(p => countMap.ContainsKey(p.Id))
            .Where(p =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .Select(p => new PersonOverview(
                p.Id,
                p.Name,
                countMap.TryGetValue(p.Id, out var c) ? c : 0,
                coverMap.TryGetValue(p.Id, out var cover) ? cover : null))

            .ToList();

        // Create synthetic person entries for manual tags.
        var tagPeople = tagCountMap
            .Select(kvp => new PersonOverview(
                $"tag:{kvp.Key}",
                kvp.Key,
                kvp.Value,
                null))
            .Where(p =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Merge and sort.
        var merged = facePeople
            .Concat(tagPeople)
            .OrderByDescending(p => p.PhotoCount)
            .ThenBy(p => p.Name)
            .ToList();

        return merged;
    }

    private sealed class MediaTagRow
    {
        public string Path { get; set; } = string.Empty;
        public string PeopleTagsSummary { get; set; } = string.Empty;
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
            var sql =
                $"SELECT * FROM MediaItem WHERE Path IN ({placeholders}) AND MediaType = ? ORDER BY DateAddedSeconds DESC;";
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

    public async Task<(string Id, string Name)?> FindPersonByNameAsync(string name, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var people = await db.Db.Table<PersonProfile>().ToListAsync().ConfigureAwait(false);
        var match = people.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return null;

        return (match.Id, match.Name);
    }

    private sealed class PersonCountRow
    {
        public string PersonId { get; set; } = string.Empty;
        public int Cnt { get; set; }
    }

    private sealed class TagCountRow
    {
        public string PersonName { get; set; } = string.Empty;
        public int Cnt { get; set; }
    }

    private sealed class MediaPathRow
    {
        public string MediaPath { get; set; } = string.Empty;
    }
}