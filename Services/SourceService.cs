using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Services;

public sealed class SourceService : ISourceService
{
    private readonly AppDb db;

    public SourceService(AppDb db)
    {
        this.db = db;
    }

    public async Task<List<MediaSource>> GetSourcesAsync()
    {
        await db.EnsureInitializedAsync();
        return await db.Db.Table<MediaSource>().OrderBy(s => s.DisplayName).ToListAsync();
    }

    public async Task EnsureDefaultSourceAsync()
    {
        await db.EnsureInitializedAsync();
        var existing = await db.Db.Table<MediaSource>().FirstOrDefaultAsync();
        if (existing != null)
            return;

        // Default "All device media" virtual source (empty path = MediaStore)
        var src = new MediaSource
        {
            Id = "device_all",
            DisplayName = AppResources.AllDeviceVideos,
            LocalFolderPath = "",
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };
        await db.Db.InsertAsync(src);
    }

    public async Task UpsertAsync(MediaSource src)
    {
        await db.EnsureInitializedAsync();
        await db.Db.InsertOrReplaceAsync(src);
    }

    public async Task DeleteAsync(MediaSource src)
    {
        await db.EnsureInitializedAsync();
        await DeleteSourceAsync(src);
    }

    private async Task DeleteSourceAsync(MediaSource src)
    {
        await db.Db.ExecuteAsync("DELETE FROM MediaItem WHERE SourceId = ?", src.Id);
        await db.Db.DeleteAsync(src);
#if WINDOWS
        if (!string.IsNullOrWhiteSpace(src.AccessToken))
        {
            try
            {
                Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(src.AccessToken);
            }
            catch
            {
                // Ignore missing/invalid access tokens.
            }
        }
#endif
    }
}
