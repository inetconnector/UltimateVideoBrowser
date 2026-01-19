using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AlbumService
{
    private readonly AppDb db;

    public AlbumService(AppDb db)
    {
        this.db = db;
    }

    public async Task<List<Album>> GetAlbumsAsync()
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        return await db.Db.Table<Album>().OrderBy(a => a.Name).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Album?> GetAlbumByIdAsync(string albumId)
    {
        if (string.IsNullOrWhiteSpace(albumId))
            return null;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        return await db.Db.Table<Album>().FirstOrDefaultAsync(a => a.Id == albumId).ConfigureAwait(false);
    }

    public async Task<List<AlbumListItem>> GetAlbumSummariesAsync()
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        const string sql = @"
            SELECT Album.Id, Album.Name, COUNT(AlbumItem.Id) AS ItemCount
            FROM Album
            LEFT JOIN AlbumItem ON Album.Id = AlbumItem.AlbumId
            GROUP BY Album.Id, Album.Name
            ORDER BY Album.Name;";

        return await db.Db.QueryAsync<AlbumListItem>(sql).ConfigureAwait(false);
    }

    public async Task<Album?> FindByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        var albums = await GetAlbumsAsync().ConfigureAwait(false);
        return albums.FirstOrDefault(a => string.Equals(a.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Album> CreateAlbumAsync(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Album name required.", nameof(name));

        var album = new Album
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmed,
            CreatedUtcSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await db.Db.InsertAsync(album).ConfigureAwait(false);
        return album;
    }

    public async Task UpdateAlbumAsync(Album album)
    {
        if (album == null)
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await db.Db.UpdateAsync(album).ConfigureAwait(false);
    }

    public async Task DeleteAlbumAsync(Album album)
    {
        if (album == null)
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);
        await db.Db.ExecuteAsync("DELETE FROM AlbumItem WHERE AlbumId = ?;", album.Id).ConfigureAwait(false);
        await db.Db.DeleteAsync(album).ConfigureAwait(false);
    }

    public async Task AddItemsAsync(string albumId, IEnumerable<MediaItem> items)
    {
        if (string.IsNullOrWhiteSpace(albumId))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item?.Path))
                continue;

            await db.Db.ExecuteAsync(
                    "INSERT OR IGNORE INTO AlbumItem (AlbumId, MediaPath) VALUES (?, ?);",
                    albumId,
                    item.Path)
                .ConfigureAwait(false);
        }
    }

    public async Task RemoveItemsAsync(string albumId, IEnumerable<MediaItem> items)
    {
        if (string.IsNullOrWhiteSpace(albumId))
            return;

        await db.EnsureInitializedAsync().ConfigureAwait(false);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item?.Path))
                continue;

            await db.Db.ExecuteAsync(
                    "DELETE FROM AlbumItem WHERE AlbumId = ? AND MediaPath = ?;",
                    albumId,
                    item.Path)
                .ConfigureAwait(false);
        }
    }

    public async Task<int> CountAlbumItemsAsync(
        string albumId,
        string search,
        SearchScope searchScope,
        string? sourceId,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        bool includeHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildAlbumQuery(albumId, search, searchScope, sourceId, from, to, mediaTypes, "name", null,
            null, true, includeHidden ? null : false);
        return await db.Db.ExecuteScalarAsync<int>(sql, args.ToArray()).ConfigureAwait(false);
    }

    public async Task<int> CountHiddenAlbumItemsAsync(
        string albumId,
        string search,
        SearchScope searchScope,
        string? sourceId,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) = BuildAlbumQuery(albumId, search, searchScope, sourceId, from, to, mediaTypes, "name", null,
            null, true, true);
        return await db.Db.ExecuteScalarAsync<int>(sql, args.ToArray()).ConfigureAwait(false);
    }

    public async Task<List<MediaItem>> QueryAlbumPageAsync(
        string albumId,
        string search,
        SearchScope searchScope,
        string? sourceId,
        string sortKey,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        int offset,
        int limit,
        bool includeHidden)
    {
        await db.EnsureInitializedAsync().ConfigureAwait(false);
        var (sql, args) =
            BuildAlbumQuery(albumId, search, searchScope, sourceId, from, to, mediaTypes, sortKey, offset, limit,
                false, includeHidden ? null : false);
        return await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
    }

    private static (string sql, List<object> args) BuildAlbumQuery(
        string albumId,
        string search,
        SearchScope searchScope,
        string? sourceId,
        DateTime? from,
        DateTime? to,
        MediaType mediaTypes,
        string sortKey,
        int? offset,
        int? limit,
        bool countOnly,
        bool? isHidden)
    {
        var args = new List<object>();
        var filters = new List<string>
        {
            "AlbumItem.AlbumId = ?"
        };
        args.Add(albumId);

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

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmedSearch = search.Trim();
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
                    searchFilters.Add("Album.Name LIKE ?");
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

        var sql = countOnly
            ? "SELECT COUNT(*) FROM MediaItem INNER JOIN AlbumItem ON AlbumItem.MediaPath = MediaItem.Path INNER JOIN Album ON Album.Id = AlbumItem.AlbumId"
            : "SELECT MediaItem.* FROM MediaItem INNER JOIN AlbumItem ON AlbumItem.MediaPath = MediaItem.Path INNER JOIN Album ON Album.Id = AlbumItem.AlbumId";

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
}
