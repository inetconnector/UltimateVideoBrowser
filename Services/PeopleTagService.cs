using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class PeopleTagService
{
    private readonly AppDb db;

    public PeopleTagService(AppDb db)
    {
        this.db = db;
    }

    public async Task<IReadOnlyList<string>> GetTagsForMediaAsync(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return Array.Empty<string>();

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var tags = await db.Db.Table<PersonTag>()
            .Where(tag => tag.MediaPath == mediaPath)
            .ToListAsync()
            .ConfigureAwait(false);
        return tags.Select(tag => tag.PersonName).ToList();
    }

    public async Task<Dictionary<string, List<string>>> GetTagsForMediaAsync(IEnumerable<string> mediaPaths)
    {
        var paths = mediaPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        var placeholders = string.Join(", ", paths.Select(_ => "?"));
        var query = $"SELECT MediaPath, PersonName FROM PersonTag WHERE MediaPath IN ({placeholders});";
        var results = await db.Db.QueryAsync<PersonTag>(query, paths.Cast<object>().ToArray())
            .ConfigureAwait(false);

        var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in results)
        {
            if (!grouped.TryGetValue(tag.MediaPath, out var list))
            {
                list = new List<string>();
                grouped[tag.MediaPath] = list;
            }

            list.Add(tag.PersonName);
        }

        return grouped;
    }

    public async Task SetTagsForMediaAsync(string mediaPath, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await db.Db.ExecuteAsync("DELETE FROM PersonTag WHERE MediaPath = ?;", mediaPath)
            .ConfigureAwait(false);

        var toInsert = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => new PersonTag
            {
                MediaPath = mediaPath,
                PersonName = tag
            })
            .ToList();

        foreach (var entry in toInsert)
            await db.Db.InsertAsync(entry).ConfigureAwait(false);
    }

    public async Task AddTagsForMediaAsync(string mediaPath, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        var incoming = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (incoming.Count == 0)
            return;

        var existing = await GetTagsForMediaAsync(mediaPath).ConfigureAwait(false);
        foreach (var tag in existing)
            incoming.Add(tag);

        await SetTagsForMediaAsync(mediaPath, incoming).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> FindMediaByPersonAsync(string personName)
    {
        if (string.IsNullOrWhiteSpace(personName))
            return Array.Empty<string>();

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        var results = await db.Db.QueryAsync<PersonTag>(
                "SELECT DISTINCT MediaPath, PersonName, Id FROM PersonTag WHERE PersonName LIKE ?;",
                $"%{personName.Trim()}%")
            .ConfigureAwait(false);

        return results.Select(tag => tag.MediaPath).ToList();
    }
}