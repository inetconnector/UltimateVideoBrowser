using SQLite;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppDb
{
    public AppDb()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ultimatevideobrowser.db");
        Db = new SQLiteAsyncConnection(dbPath);
        Db.CreateTableAsync<MediaSource>().Wait();
        Db.CreateTableAsync<MediaItem>().Wait();
        Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_name ON MediaItem(Name);").Wait();
        Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_source ON MediaItem(SourceId);").Wait();
        Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_type ON MediaItem(MediaType);").Wait();
        TryAddMediaSourceAccessToken();
    }

    public SQLiteAsyncConnection Db { get; }

    private void TryAddMediaSourceAccessToken()
    {
        try
        {
            Db.ExecuteAsync("ALTER TABLE MediaSource ADD COLUMN AccessToken TEXT;").Wait();
        }
        catch
        {
            // Column exists or migration not needed.
        }
    }
}
