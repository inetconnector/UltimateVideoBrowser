using SQLite;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppDb
{
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool isInitialized;

    public AppDb()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ultimatevideobrowser.db");
        Db = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection Db { get; }

    public async Task EnsureInitializedAsync()
    {
        if (isInitialized)
            return;

        await initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (isInitialized)
                return;

            await Db.CreateTableAsync<MediaSource>().ConfigureAwait(false);
            await Db.CreateTableAsync<MediaItem>().ConfigureAwait(false);
            await Db.CreateTableAsync<PersonTag>().ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_name ON MediaItem(Name);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_source ON MediaItem(SourceId);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_type ON MediaItem(MediaType);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_media ON PersonTag(MediaPath);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_name ON PersonTag(PersonName);")
                .ConfigureAwait(false);
            await TryAddMediaSourceAccessTokenAsync().ConfigureAwait(false);
            isInitialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    private async Task TryAddMediaSourceAccessTokenAsync()
    {
        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaSource ADD COLUMN AccessToken TEXT;").ConfigureAwait(false);
        }
        catch
        {
            // Column exists or migration not needed.
        }
    }
}
