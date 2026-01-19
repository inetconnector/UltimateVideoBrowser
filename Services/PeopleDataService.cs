using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.Services;

public sealed record PersonOverview(
    string Id,
    string Name,
    int PhotoCount,
    float QualityScore,
    FaceEmbedding? CoverFace,
    string? CoverMediaPath,
    MediaType? CoverMediaType,
    bool IsIgnored);

public sealed record FaceTagInfo(
    int FaceIndex,
    string PersonId,
    string PersonName,
    FaceEmbedding Embedding);

public sealed class PeopleDataService
{
    private const string TagPrefix = "tag:";
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

        var people = await db.Db.Table<PersonProfile>()
            .Where(p => p.MergedIntoPersonId == null || p.MergedIntoPersonId == "")
            .ToListAsync()
            .ConfigureAwait(false);

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
                var name = (r.PersonName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                tagCountMap[name] = r.Cnt;
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

        var tagCoverMap = new Dictionary<string, TagCoverInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (tagCountMap.Count > 0)
                tagCoverMap = await BuildTagCoverMapAsync(tagCountMap.Keys, ct).ConfigureAwait(false);
        }
        catch
        {
            // Keep UI resilient.
        }

        var coverMap = new Dictionary<string, FaceEmbedding>(StringComparer.OrdinalIgnoreCase);

        var primaryIds = people
            .Where(p => p.PrimaryFaceEmbeddingId.HasValue)
            .Select(p => p.PrimaryFaceEmbeddingId!.Value)
            .Distinct()
            .ToList();

        if (primaryIds.Count > 0)
        {
            var placeholders = string.Join(",", primaryIds.Select(_ => "?"));
            var primarySql = $"SELECT * FROM FaceEmbedding WHERE Id IN ({placeholders});";
            var primaryEmbeddings = await db.Db
                .QueryAsync<FaceEmbedding>(primarySql, primaryIds.Cast<object>().ToArray())
                .ConfigureAwait(false);
            var primaryMap = primaryEmbeddings.ToDictionary(e => e.Id);
            foreach (var p in people)
            {
                if (p.PrimaryFaceEmbeddingId is not { } id)
                    continue;
                if (primaryMap.TryGetValue(id, out var emb))
                    coverMap[p.Id] = emb;
            }
        }

        // Prefer photo-based faces for cover thumbnails.
        var photoEmbeddings = await db.Db.QueryAsync<FaceEmbedding>(
                "SELECT FaceEmbedding.* FROM FaceEmbedding " +
                "INNER JOIN MediaItem ON MediaItem.Path = FaceEmbedding.MediaPath " +
                "WHERE FaceEmbedding.PersonId IS NOT NULL AND FaceEmbedding.PersonId <> '' " +
                "AND MediaItem.MediaType IN (?, ?) " +
                "ORDER BY FaceEmbedding.PersonId, FaceEmbedding.FaceQuality DESC, FaceEmbedding.Score DESC;",
                (int)MediaType.Photos,
                (int)MediaType.Graphics)
            .ConfigureAwait(false);

        foreach (var e in photoEmbeddings)
        {
            if (string.IsNullOrWhiteSpace(e.PersonId))
                continue;
            if (!coverMap.ContainsKey(e.PersonId!))
                coverMap[e.PersonId!] = e;
        }

        // Fallback: use any face embedding if we still don't have a cover.
        var allEmbeddings = await db.Db.QueryAsync<FaceEmbedding>(
                "SELECT * FROM FaceEmbedding WHERE PersonId IS NOT NULL AND PersonId <> '' " +
                "ORDER BY PersonId, FaceQuality DESC, Score DESC;")
            .ConfigureAwait(false);

        foreach (var e in allEmbeddings)
        {
            if (string.IsNullOrWhiteSpace(e.PersonId))
                continue;
            if (!coverMap.ContainsKey(e.PersonId!))
                coverMap[e.PersonId!] = e;
        }

        var peopleMap = people
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);

        var facePeople = people
            .Where(p =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                coverMap.TryGetValue(p.Id, out var coverFace);
                var tagFallback = coverFace == null ? tagCoverMap : null;
                return new PersonOverview(
                    p.Id,
                    p.Name,
                    ResolvePhotoCount(p, countMap, tagCountMap),
                    p.QualityScore,
                    coverFace,
                    ResolveCoverMediaPath(p.Name, tagFallback),
                    ResolveCoverMediaType(p.Name, tagFallback),
                    p.IsIgnored);
            })
            .ToList();

        foreach (var (personId, count) in countMap)
        {
            if (peopleMap.ContainsKey(personId))
                continue;

            var fallbackName = BuildFallbackName(personId);
            if (!string.IsNullOrWhiteSpace(normalizedSearch) &&
                !fallbackName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            coverMap.TryGetValue(personId, out var coverFace);
            var tagFallback = coverFace == null ? tagCoverMap : null;
            facePeople.Add(new PersonOverview(
                personId,
                fallbackName,
                count,
                0f,
                coverFace,
                ResolveCoverMediaPath(fallbackName, tagFallback),
                ResolveCoverMediaType(fallbackName, tagFallback),
                false));
        }

        var faceNameSet = new HashSet<string>(
            facePeople.Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        // Create synthetic person entries for manual tags (skip ones already represented by face profiles).
        var tagPeople = tagCountMap
            .Where(kvp => !faceNameSet.Contains(kvp.Key))
            .Select(kvp => new PersonOverview(
                $"tag:{kvp.Key}",
                kvp.Key,
                kvp.Value,
                0f,
                null,
                ResolveCoverMediaPath(kvp.Key, tagCoverMap),
                ResolveCoverMediaType(kvp.Key, tagCoverMap),
                false))
            .Where(p =>
                string.IsNullOrWhiteSpace(normalizedSearch) ||
                p.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Merge and sort.
        var merged = facePeople
            .Concat(tagPeople)
            .OrderBy(p => p.IsIgnored ? 1 : 0)
            .ThenByDescending(p => p.PhotoCount)
            .ThenBy(p => p.Name)
            .ToList();

        return merged;
    }

    public async Task<IReadOnlyList<MediaItem>> GetMediaForPersonAsync(string personId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return Array.Empty<MediaItem>();

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var faceRows = await db.Db.QueryAsync<MediaPathRow>(
                "SELECT DISTINCT MediaPath FROM FaceEmbedding WHERE PersonId = ?;",
                personId)
            .ConfigureAwait(false);
        foreach (var row in faceRows)
            if (!string.IsNullOrWhiteSpace(row.MediaPath))
                paths.Add(row.MediaPath);

        var profile = await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == personId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        var personName = profile?.Name;

        if (!string.IsNullOrWhiteSpace(personName))
        {
            var tagPaths = await GetTaggedMediaPathsForNameAsync(personName, ct).ConfigureAwait(false);
            foreach (var path in tagPaths)
                paths.Add(path);
        }

        return await FetchMediaItemsByPathsAsync(paths.ToList(), ct).ConfigureAwait(false);
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

        var people = await db.Db.Table<PersonProfile>()
            .Where(p => p.MergedIntoPersonId == null || p.MergedIntoPersonId == "")
            .ToListAsync()
            .ConfigureAwait(false);
        var peopleMap = people
            .Where(p => !p.IsIgnored)
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.OrdinalIgnoreCase);

        var list = embeddings
            .Where(e => !string.IsNullOrWhiteSpace(e.PersonId))
            .Where(e => peopleMap.ContainsKey(e.PersonId!))
            .Select(e => new FaceTagInfo(
                e.FaceIndex,
                e.PersonId!,
                peopleMap.TryGetValue(e.PersonId!, out var name) ? name : string.Empty,
                e))
            .ToList();

        return list;
    }

    public async Task<MediaItem?> TryGetMediaItemAsync(string mediaPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return null;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return await db.Db.Table<MediaItem>()
            .Where(item => item.Path == mediaPath)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public Task RenamePersonAsync(string personId, string newName, CancellationToken ct)
    {
        return recognitionService.RenamePersonAsync(personId, newName, ct);
    }

    public Task SetPersonIgnoredAsync(string personId, bool isIgnored, CancellationToken ct)
    {
        return recognitionService.SetPersonIgnoredAsync(personId, isIgnored, ct);
    }

    public async Task<(string Id, string Name)?> FindPersonByNameAsync(string name, CancellationToken ct)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var people = await db.Db.Table<PersonProfile>()
            .Where(p => p.MergedIntoPersonId == null || p.MergedIntoPersonId == "")
            .ToListAsync()
            .ConfigureAwait(false);
        var match = people.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return null;

        return (match.Id, match.Name);
    }

    public async Task<IReadOnlyList<PersonOverview>> ListMergeCandidatesAsync(string excludingPersonId,
        CancellationToken ct)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);

        var people = await GetPeopleOverviewAsync(null, ct).ConfigureAwait(false);
        return people
            .Where(p => !p.Id.Equals(excludingPersonId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CoverFace?.FaceQuality ?? 0)
            .ThenByDescending(p => p.PhotoCount)
            .ToList();
    }


    public async Task<PersonProfile?> GetPersonProfileAsync(string personId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return null;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        return await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == personId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task MergePersonsAsync(string sourcePersonId, string targetPersonId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourcePersonId) || string.IsNullOrWhiteSpace(targetPersonId))
            return;

        if (sourcePersonId.Equals(targetPersonId, StringComparison.OrdinalIgnoreCase))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        // Load profiles to preserve the source name as an alias (default behavior).
        var sourceProfile = await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == sourcePersonId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        var targetProfile = await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == targetPersonId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        await db.Db.RunInTransactionAsync(conn =>
        {
            // Mark source as merged (soft redirect).
            conn.Execute("UPDATE PersonProfile SET MergedIntoPersonId = ? WHERE Id = ?;", targetPersonId,
                sourcePersonId);

            // Move face embeddings to target.
            conn.Execute("UPDATE FaceEmbedding SET PersonId = ? WHERE PersonId = ?;", targetPersonId, sourcePersonId);

            // Preserve old name as alias on the target (best-effort).
            if (sourceProfile != null && targetProfile != null)
            {
                var alias = sourceProfile.Name?.Trim();
                if (!string.IsNullOrWhiteSpace(alias) &&
                    !alias.Equals(targetProfile.Name, StringComparison.OrdinalIgnoreCase))
                    conn.Execute("INSERT INTO PersonAlias(PersonId, AliasName) VALUES(?, ?);", targetPersonId, alias);

                // Keep manual tags consistent by renaming source name occurrences to target name.
                if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(targetProfile.Name))
                    conn.Execute("UPDATE PersonTag SET PersonName = ? WHERE PersonName = ?;", targetProfile.Name,
                        alias);
            }
        }).ConfigureAwait(false);

        // Recompute quality/cover after merge.
        await UpdatePersonQualityAsync(targetPersonId, ct).ConfigureAwait(false);
    }

    public async Task UpdatePersonQualityAsync(string personId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        // Use top-N face qualities to produce a stable person quality score.
        var faces = await db.Db.Table<FaceEmbedding>()
            .Where(f => f.PersonId == personId)
            .OrderByDescending(f => f.FaceQuality)
            .Take(10)
            .ToListAsync()
            .ConfigureAwait(false);

        if (faces.Count == 0)
            return;

        var top = faces.Take(5).ToList();
        var avgTop = top.Average(f => f.FaceQuality);
        var count = faces.Count;

        // Small bonus for more evidence.
        var bonus = MathF.Min(0.1f, MathF.Log10(1 + count) / 20f);
        var score = Math.Clamp(avgTop + bonus, 0, 1);

        var best = top.FirstOrDefault();

        var profile = await db.Db.Table<PersonProfile>()
            .Where(p => p.Id == personId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (profile == null)
            return;

        profile.QualityScore = score;
        profile.PrimaryFaceEmbeddingId = best?.Id;
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.Db.UpdateAsync(profile).ConfigureAwait(false);
    }

    private static string? TryExtractTagName(string personId)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return null;

        if (!personId.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var name = personId[TagPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static int ResolvePhotoCount(PersonProfile profile, Dictionary<string, int> countMap,
        Dictionary<string, int> tagCountMap)
    {
        if (countMap.TryGetValue(profile.Id, out var c) && c > 0)
            return c;

        if (!string.IsNullOrWhiteSpace(profile.Name) && tagCountMap.TryGetValue(profile.Name, out var tagCount))
            return tagCount;

        return 0;
    }

    private static string? ResolveCoverMediaPath(string personName, Dictionary<string, TagCoverInfo>? tagCoverMap)
    {
        if (string.IsNullOrWhiteSpace(personName) || tagCoverMap == null)
            return null;

        return tagCoverMap.TryGetValue(personName, out var info) ? info.Path : null;
    }

    private static MediaType? ResolveCoverMediaType(string personName, Dictionary<string, TagCoverInfo>? tagCoverMap)
    {
        if (string.IsNullOrWhiteSpace(personName) || tagCoverMap == null)
            return null;

        return tagCoverMap.TryGetValue(personName, out var info) ? info.MediaType : null;
    }

    private async Task<Dictionary<string, TagCoverInfo>> BuildTagCoverMapAsync(IEnumerable<string> names,
        CancellationToken ct)
    {
        var list = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, TagCoverInfo>(StringComparer.OrdinalIgnoreCase);
        if (list.Count == 0)
            return map;

        ct.ThrowIfCancellationRequested();

        var placeholders = string.Join(",", list.Select(_ => "?"));
        var sql =
            "SELECT PersonTag.PersonName AS PersonName, MediaItem.Path AS Path, MediaItem.MediaType AS MediaType, " +
            "MediaItem.DateAddedSeconds AS DateAddedSeconds " +
            "FROM PersonTag INNER JOIN MediaItem ON MediaItem.Path = PersonTag.MediaPath " +
            $"WHERE PersonTag.PersonName IN ({placeholders}) AND MediaItem.MediaType IN (?, ?) " +
            "ORDER BY MediaItem.DateAddedSeconds DESC;";
        var args = list.Cast<object>().ToList();
        args.Add((int)MediaType.Photos);
        args.Add((int)MediaType.Graphics);

        var rows = await db.Db.QueryAsync<TagCoverRow>(sql, args.ToArray()).ConfigureAwait(false);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.PersonName) || string.IsNullOrWhiteSpace(row.Path))
                continue;

            if (map.ContainsKey(row.PersonName))
                continue;

            if (!Enum.IsDefined(typeof(MediaType), row.MediaType))
                continue;

            map[row.PersonName] = new TagCoverInfo(row.Path, (MediaType)row.MediaType);
        }

        var summaryRows = await db.Db.QueryAsync<MediaSummaryRow>(
                "SELECT Path, PeopleTagsSummary, DateAddedSeconds, MediaType FROM MediaItem " +
                "WHERE PeopleTagsSummary IS NOT NULL AND PeopleTagsSummary <> '' AND MediaType IN (?, ?) " +
                "ORDER BY DateAddedSeconds DESC;",
                (int)MediaType.Photos,
                (int)MediaType.Graphics)
            .ConfigureAwait(false);

        foreach (var row in summaryRows)
        {
            if (string.IsNullOrWhiteSpace(row.Path) || string.IsNullOrWhiteSpace(row.PeopleTagsSummary))
                continue;

            if (!Enum.IsDefined(typeof(MediaType), row.MediaType))
                continue;

            var people = row.PeopleTagsSummary
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var person in people)
            {
                if (string.IsNullOrWhiteSpace(person))
                    continue;

                if (!list.Contains(person, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (map.ContainsKey(person))
                    continue;

                map[person] = new TagCoverInfo(row.Path, (MediaType)row.MediaType);
            }
        }

        return map;
    }

    private async Task<IReadOnlyList<string>> GetTaggedMediaPathsForNameAsync(string personName, CancellationToken ct)
    {
        var trimmed = (personName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Array.Empty<string>();

        ct.ThrowIfCancellationRequested();

        var results = await db.Db.QueryAsync<MediaPathRow>(
                "SELECT DISTINCT MediaPath FROM PersonTag WHERE PersonName LIKE ?;",
                trimmed)
            .ConfigureAwait(false);

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in results)
            if (!string.IsNullOrWhiteSpace(row.MediaPath))
                paths.Add(row.MediaPath);

        var summaryRows = await db.Db.QueryAsync<MediaTagRow>(
                "SELECT Path, PeopleTagsSummary FROM MediaItem " +
                "WHERE PeopleTagsSummary IS NOT NULL AND PeopleTagsSummary <> '' AND PeopleTagsSummary LIKE ?;",
                $"%{trimmed}%")
            .ConfigureAwait(false);

        foreach (var row in summaryRows)
        {
            if (string.IsNullOrWhiteSpace(row.Path) || string.IsNullOrWhiteSpace(row.PeopleTagsSummary))
                continue;

            var people = row.PeopleTagsSummary
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (people.Any(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase)))
                paths.Add(row.Path);
        }

        return paths.ToList();
    }

    private async Task<IReadOnlyList<MediaItem>> FetchMediaItemsByPathsAsync(IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var distinct = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
            return Array.Empty<MediaItem>();

        var items = new List<MediaItem>();
        const int chunkSize = 400;
        for (var i = 0; i < distinct.Count; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = distinct.Skip(i).Take(chunkSize).ToList();
            var placeholders = string.Join(",", chunk.Select(_ => "?"));
            var sql =
                $"SELECT * FROM MediaItem WHERE Path IN ({placeholders}) AND MediaType IN (?, ?) ORDER BY DateAddedSeconds DESC;";
            var args = chunk.Cast<object>().ToList();
            args.Add((int)MediaType.Photos);
            args.Add((int)MediaType.Graphics);
            var result = await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
            items.AddRange(result);
        }

        return items;
    }

    private static string BuildFallbackName(string personId)
    {
        if (string.IsNullOrWhiteSpace(personId))
            return "Unknown";

        var trimmed = personId.Trim();
        var shortId = trimmed.Length <= 6 ? trimmed : trimmed[..6];
        return $"Unknown {shortId}";
    }

    private sealed class MediaTagRow
    {
        public string Path { get; } = string.Empty;
        public string PeopleTagsSummary { get; } = string.Empty;
    }

    private sealed class MediaSummaryRow
    {
        public string Path { get; } = string.Empty;
        public string PeopleTagsSummary { get; } = string.Empty;
        public long DateAddedSeconds { get; set; }
        public int MediaType { get; set; }
    }

    private sealed class TagCoverRow
    {
        public string PersonName { get; } = string.Empty;
        public string Path { get; } = string.Empty;
        public long DateAddedSeconds { get; set; }
        public int MediaType { get; set; }
    }

    private sealed class TagCoverInfo
    {
        public TagCoverInfo(string path, MediaType mediaType)
        {
            Path = path;
            MediaType = mediaType;
        }

        public string Path { get; }
        public MediaType MediaType { get; }
    }

    private sealed class PersonCountRow
    {
        public string PersonId { get; } = string.Empty;
        public int Cnt { get; set; }
    }

    private sealed class TagCountRow
    {
        public string PersonName { get; } = string.Empty;
        public int Cnt { get; set; }
    }

    private sealed class MediaPathRow
    {
        public string MediaPath { get; } = string.Empty;
    }
}