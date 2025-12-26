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

    public Task<List<MediaSource>> GetSourcesAsync()
    {
        return db.Db.Table<MediaSource>().OrderBy(s => s.DisplayName).ToListAsync();
    }

    public async Task EnsureDefaultSourceAsync()
    {
        var existing = await db.Db.Table<MediaSource>().FirstOrDefaultAsync();
        if (existing != null)
            return;

        // Default "All device videos" virtual source (empty path = MediaStore)
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

    public Task UpsertAsync(MediaSource src)
    {
        return db.Db.InsertOrReplaceAsync(src);
    }

    public Task DeleteAsync(MediaSource src)
    {
        return DeleteSourceAsync(src);
    }

    private async Task DeleteSourceAsync(MediaSource src)
    {
        await db.Db.ExecuteAsync("DELETE FROM VideoItem WHERE SourceId = ?", src.Id);
        await db.Db.DeleteAsync(src);
    }
}
