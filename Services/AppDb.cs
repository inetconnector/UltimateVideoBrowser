using SQLite;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppDb
{
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool isInitialized;

    public AppDb()
    {
        var dbPath = Path.Combine(AppDataPaths.Root, "ultimatevideobrowser.db");
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
            await Db.CreateTableAsync<Album>().ConfigureAwait(false);
            await Db.CreateTableAsync<AlbumItem>().ConfigureAwait(false);
            await Db.CreateTableAsync<PersonTag>().ConfigureAwait(false);
            await Db.CreateTableAsync<PersonProfile>().ConfigureAwait(false);
            await Db.CreateTableAsync<PersonAlias>().ConfigureAwait(false);
            await Db.CreateTableAsync<FaceEmbedding>().ConfigureAwait(false);
            await Db.CreateTableAsync<FaceScanJob>().ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_name ON MediaItem(Name);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_source ON MediaItem(SourceId);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_type ON MediaItem(MediaType);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_album_item_album ON AlbumItem(AlbumId);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_album_item_media ON AlbumItem(MediaPath);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_media ON PersonTag(MediaPath);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_name ON PersonTag(PersonName);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_face_embedding_media ON FaceEmbedding(MediaPath);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_face_embedding_person ON FaceEmbedding(PersonId);")
                .ConfigureAwait(false);
            await Db.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS idx_face_scan_queue_time ON FaceScanJob(EnqueuedAtSeconds);")
                .ConfigureAwait(false);
            await TryAddMediaSourceAccessTokenAsync().ConfigureAwait(false);
            await TryAddMediaItemLocationColumnsAsync().ConfigureAwait(false);
            await TryAddFaceEmbeddingBoxColumnsAsync().ConfigureAwait(false);
            await TryAddPeopleModelKeyColumnsAsync().ConfigureAwait(false);
            await TryAddPersonProfileMergeColumnsAsync().ConfigureAwait(false);
            await TryAddMediaItemPeopleTagsSummaryColumnAsync().ConfigureAwait(false);
            isInitialized = true;
        }
        finally
        {
            initLock.Release();
        }
    }

    public async Task ResetAsync()
    {
        if (!isInitialized)
            await EnsureInitializedAsync().ConfigureAwait(false);

        await initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Db.DropTableAsync<FaceEmbedding>().ConfigureAwait(false);
            await Db.DropTableAsync<PersonAlias>().ConfigureAwait(false);
            await Db.DropTableAsync<PersonProfile>().ConfigureAwait(false);
            await Db.DropTableAsync<PersonTag>().ConfigureAwait(false);
            await Db.DropTableAsync<AlbumItem>().ConfigureAwait(false);
            await Db.DropTableAsync<Album>().ConfigureAwait(false);
            await Db.DropTableAsync<MediaItem>().ConfigureAwait(false);
            await Db.DropTableAsync<MediaSource>().ConfigureAwait(false);
            await Db.DropTableAsync<FaceScanJob>().ConfigureAwait(false);
            isInitialized = false;
        }
        finally
        {
            initLock.Release();
        }

        await EnsureInitializedAsync().ConfigureAwait(false);
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

    private async Task TryAddMediaItemLocationColumnsAsync()
    {
        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaItem ADD COLUMN Latitude REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaItem ADD COLUMN Longitude REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS idx_media_location ON MediaItem(Latitude, Longitude);")
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryAddFaceEmbeddingBoxColumnsAsync()
    {
        // These columns were added later to support the People UI (face crops / boxes).
        // Each ALTER is idempotent via try/catch to keep startup resilient.
        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN X REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN Y REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN W REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN H REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN ImageWidth INTEGER;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN ImageHeight INTEGER;").ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryAddPeopleModelKeyColumnsAsync()
    {
        // Adds model-key bookkeeping columns used to skip face re-scan when models haven't changed.
        // Each ALTER is idempotent via try/catch to keep app startup resilient.
        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaItem ADD COLUMN FaceScanModelKey TEXT;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaItem ADD COLUMN FaceScanAtSeconds INTEGER;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS idx_media_facescan_modelkey ON MediaItem(FaceScanModelKey);")
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN DetectionModelKey TEXT;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN EmbeddingModelKey TEXT;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE FaceEmbedding ADD COLUMN FaceQuality REAL;").ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryAddPersonProfileMergeColumnsAsync()
    {
        // Adds merge + quality + cover columns for PersonProfile.
        try
        {
            await Db.ExecuteAsync("ALTER TABLE PersonProfile ADD COLUMN MergedIntoPersonId TEXT;")
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE PersonProfile ADD COLUMN PrimaryFaceEmbeddingId INTEGER;")
                .ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE PersonProfile ADD COLUMN QualityScore REAL;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync("ALTER TABLE PersonProfile ADD COLUMN IsIgnored INTEGER;").ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            await Db.ExecuteAsync(
                    "CREATE INDEX IF NOT EXISTS idx_person_profile_merged_into ON PersonProfile(MergedIntoPersonId);")
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task TryAddMediaItemPeopleTagsSummaryColumnAsync()
    {
        // Store a denormalized summary for fallback queries and UI resilience.
        try
        {
            await Db.ExecuteAsync("ALTER TABLE MediaItem ADD COLUMN PeopleTagsSummary TEXT;")
                .ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
