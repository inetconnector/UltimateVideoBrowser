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
        Db.CreateTableAsync<VideoItem>().Wait();
        Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_video_name ON VideoItem(Name);").Wait();
        Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_video_source ON VideoItem(SourceId);").Wait();
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
