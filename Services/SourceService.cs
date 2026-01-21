using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
#if ANDROID && !WINDOWS
using Environment = Android.OS.Environment;
#endif

#if WINDOWS
using Windows.Storage.AccessCache;
#endif

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

#if ANDROID && !WINDOWS
        var defaults = GetAndroidDefaultSources();
        if (defaults.Count == 0)
        {
            await db.Db.InsertAsync(BuildAllDeviceSource());
            return;
        }

        foreach (var src in defaults)
            await db.Db.InsertAsync(src);
        return;
#endif

        // Default "All device media" virtual source (empty path = MediaStore)
        await db.Db.InsertAsync(BuildAllDeviceSource());
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
            try
            {
                StorageApplicationPermissions.FutureAccessList.Remove(src.AccessToken);
            }
            catch
            {
                // Ignore missing/invalid access tokens.
            }
#endif
    }

    private static MediaSource BuildAllDeviceSource()
    {
        return new MediaSource
        {
            Id = "device_all",
            DisplayName = AppResources.AllDeviceVideos,
            LocalFolderPath = "",
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };
    }

#if ANDROID && !WINDOWS
    private static List<MediaSource> GetAndroidDefaultSources()
    {
        var sources = new List<MediaSource>();

        AddSourceIfExists(sources, "android_dcim", "DCIM",
            Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDcim)?.AbsolutePath);
        AddSourceIfExists(sources, "android_dcim_camera", "Camera",
            CombineIfParent(Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDcim)?.AbsolutePath,
                "Camera"));
        AddSourceIfExists(sources, "android_pictures", "Pictures",
            Environment.GetExternalStoragePublicDirectory(Environment.DirectoryPictures)?.AbsolutePath);
        AddSourceIfExists(sources, "android_pictures_screenshots", "Screenshots",
            CombineIfParent(Environment.GetExternalStoragePublicDirectory(Environment.DirectoryPictures)?.AbsolutePath,
                "Screenshots"));
        AddSourceIfExists(sources, "android_dcim_screenshots", "DCIM Screenshots",
            CombineIfParent(Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDcim)?.AbsolutePath,
                "Screenshots"));
        AddSourceIfExists(sources, "android_movies", "Movies",
            Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMovies)?.AbsolutePath);
        AddSourceIfExists(sources, "android_downloads", "Downloads",
            Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads)?.AbsolutePath);
        AddSourceIfExists(sources, "android_documents", "Documents",
            Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDocuments)?.AbsolutePath);

        return sources;
    }

    private static void AddSourceIfExists(List<MediaSource> sources, string id, string displayName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!Directory.Exists(path))
            return;

        sources.Add(new MediaSource
        {
            Id = id,
            DisplayName = displayName,
            LocalFolderPath = path,
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        });
    }

    private static string? CombineIfParent(string? parent, string child)
    {
        if (string.IsNullOrWhiteSpace(parent))
            return null;

        return Path.Combine(parent, child);
    }
#endif
}