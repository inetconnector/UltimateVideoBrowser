using System.Collections;
using System.Text;
using System.Threading;
using SQLite;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppDb
{
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool isInitialized;
    private static int sqliteInitialized;

    public AppDb()
    {
        EnsureSqliteInitialized();
        DatabasePath = Path.Combine(AppDataPaths.Root, "ultimatevideobrowser.db");
        Db = CreateConnectionSafe(DatabasePath);
    }

    public SQLiteAsyncConnection Db { get; private set; }

    public string DatabasePath { get; }

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref sqliteInitialized, 1) == 1)
            return;

        SQLitePCL.Batteries_V2.Init();
    }

    private static SQLiteAsyncConnection CreateConnectionSafe(string databasePath)
    {
        try
        {
            return new SQLiteAsyncConnection(databasePath);
        }
        catch (SQLiteException ex)
        {
            ErrorLog.LogException(ex, "AppDb.CreateConnectionSafe",
                $"DatabasePath={databasePath}");

            if (TryBackupCorruptDatabase(databasePath, ex))
                return new SQLiteAsyncConnection(databasePath);

            throw;
        }
    }

    private static bool TryBackupCorruptDatabase(string databasePath, Exception ex)
    {
        var backupRoot = Path.Combine(AppDataPaths.Root, "db_backups");
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupRoot, $"ultimatevideobrowser_openfail_{stamp}.db");

        try
        {
            Directory.CreateDirectory(backupRoot);

            if (File.Exists(databasePath))
                File.Move(databasePath, backupPath, true);

            MoveSidecarIfExists(databasePath + "-wal", backupPath + "-wal");
            MoveSidecarIfExists(databasePath + "-shm", backupPath + "-shm");

            ErrorLog.LogMessage(
                $"Database moved after open failure ({ex.GetType().Name}). Previous DB moved to {backupPath}.",
                "AppDb.TryBackupCorruptDatabase");
            return true;
        }
        catch (Exception moveEx)
        {
            ErrorLog.LogException(moveEx, "AppDb.TryBackupCorruptDatabase", "Failed to move database files.");
            return false;
        }
    }

    public async Task EnsureInitializedAsync()
    {
        if (isInitialized)
            return;

        await initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (isInitialized)
                return;

            try
            {
                await TryConfigurePragmasAsync().ConfigureAwait(false);
                await TryCheckpointAsync().ConfigureAwait(false);
                await InitializeSchemaAsync().ConfigureAwait(false);
                isInitialized = true;
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "AppDb.EnsureInitializedAsync");

                if (ex is SQLiteException && await TryRecoverDatabaseAsync(ex).ConfigureAwait(false))
                    return;

                throw;
            }
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

    public async Task ReplaceDatabaseAsync(string sourceDbPath)
    {
        if (string.IsNullOrWhiteSpace(sourceDbPath) || !File.Exists(sourceDbPath))
            throw new FileNotFoundException("Source database file not found.", sourceDbPath);

        await initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await TryCloseConnectionAsync().ConfigureAwait(false);

            // Copy main DB and related WAL/SHM sidecar files if present.
            File.Copy(sourceDbPath, DatabasePath, true);

            CopySidecarIfExists(sourceDbPath + "-wal", DatabasePath + "-wal");
            CopySidecarIfExists(sourceDbPath + "-shm", DatabasePath + "-shm");

            // Reopen connection so services pick up the new DB.
            Db = new SQLiteAsyncConnection(DatabasePath);
            isInitialized = false;
        }
        finally
        {
            initLock.Release();
        }

        await EnsureInitializedAsync().ConfigureAwait(false);
    }

    private static void CopySidecarIfExists(string source, string target)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Copy(source, target, true);
            }
            else
            {
                if (File.Exists(target))
                    File.Delete(target);
            }
        }
        catch
        {
            // Ignore sidecar copy failures to keep restore resilient.
        }
    }

    private static void MoveSidecarIfExists(string source, string target)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Move(source, target, true);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
        catch
        {
            // Ignore sidecar move failures to keep recovery resilient.
        }
    }

    private async Task TryCloseConnectionAsync()
    {
        // sqlite-net-pcl APIs differ by version. Use reflection to avoid hard dependency.
        try
        {
            var closeAsync = Db.GetType().GetMethod("CloseAsync", Type.EmptyTypes);
            if (closeAsync != null)
            {
                var t = closeAsync.Invoke(Db, null) as Task;
                if (t != null)
                    await t.ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort only.
        }

        try
        {
            var close = Db.GetType().GetMethod("Close", Type.EmptyTypes);
            close?.Invoke(Db, null);
        }
        catch
        {
            // Best-effort only.
        }

        try
        {
            var getConnection = Db.GetType().GetMethod("GetConnection", Type.EmptyTypes);
            var conn = getConnection?.Invoke(Db, null);
            var connClose = conn?.GetType().GetMethod("Close", Type.EmptyTypes);
            connClose?.Invoke(conn, null);
        }
        catch
        {
            // Best-effort only.
        }
    }

    private async Task TryConfigurePragmasAsync()
    {
        try
        {
            // Improve concurrent read/write behavior while indexing.
            await Db.ExecuteAsync("PRAGMA journal_mode=WAL;").ConfigureAwait(false);
            await Db.ExecuteAsync("PRAGMA synchronous=NORMAL;").ConfigureAwait(false);
            await Db.ExecuteAsync("PRAGMA temp_store=MEMORY;").ConfigureAwait(false);
            await Db.ExecuteAsync("PRAGMA busy_timeout=5000;").ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (ex.Message.Contains("not an error", StringComparison.OrdinalIgnoreCase))
        {
            // Some sqlite-net builds throw "not an error" for PRAGMA writes; ignore to keep startup clean.
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "AppDb.TryConfigurePragmasAsync");
        }
    }

    public async Task<bool> TryCreateSnapshotAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        try
        {
            await EnsureInitializedAsync().ConfigureAwait(false);
            await TryCheckpointAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort only.
        }

        try
        {
            var escaped = targetPath.Replace("'", "''");
            await Db.ExecuteAsync($"VACUUM INTO '{escaped}';").ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "AppDb.TryCreateSnapshotAsync", $"Target={targetPath}");
            return false;
        }
    }

    private async Task TryCheckpointAsync()
    {
        try
        {
            await Db.QueryAsync<WalCheckpointResult>("PRAGMA wal_checkpoint(TRUNCATE);").ConfigureAwait(false);
        }
        catch (SQLiteException ex) when (ex.Message.Contains("not an error", StringComparison.OrdinalIgnoreCase))
        {
            // Some sqlite-net builds throw "not an error" for PRAGMA writes; ignore to keep snapshot resilient.
        }
    }

    private async Task InitializeSchemaAsync()
    {
        // Base tables
        await Db.CreateTableAsync<MediaSource>().ConfigureAwait(false);
        await Db.CreateTableAsync<MediaItem>().ConfigureAwait(false);
        await Db.CreateTableAsync<Album>().ConfigureAwait(false);
        await Db.CreateTableAsync<AlbumItem>().ConfigureAwait(false);
        await Db.CreateTableAsync<PersonTag>().ConfigureAwait(false);
        await Db.CreateTableAsync<PersonProfile>().ConfigureAwait(false);
        await Db.CreateTableAsync<PersonAlias>().ConfigureAwait(false);
        await Db.CreateTableAsync<FaceEmbedding>().ConfigureAwait(false);
        await Db.CreateTableAsync<FaceScanJob>().ConfigureAwait(false);

        // Schema migrations (idempotent, column-existence checked to avoid exception spam)
        await EnsureSchemaAsync().ConfigureAwait(false);

        // Indexes (best-effort: app must remain usable even if an index fails)
        await EnsureIndexesAsync().ConfigureAwait(false);
    }

    private async Task<bool> TryRecoverDatabaseAsync(Exception ex)
    {
        try
        {
            await TryCloseConnectionAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort only.
        }

        var backupRoot = Path.Combine(AppDataPaths.Root, "db_backups");
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupRoot, $"ultimatevideobrowser_corrupt_{stamp}.db");

        try
        {
            Directory.CreateDirectory(backupRoot);

            if (File.Exists(DatabasePath))
                File.Move(DatabasePath, backupPath, true);

            MoveSidecarIfExists(DatabasePath + "-wal", backupPath + "-wal");
            MoveSidecarIfExists(DatabasePath + "-shm", backupPath + "-shm");
        }
        catch (Exception moveEx)
        {
            ErrorLog.LogException(moveEx, "AppDb.TryRecoverDatabaseAsync", "Failed to move database files.");
            return false;
        }

        try
        {
            Db = new SQLiteAsyncConnection(DatabasePath);
            await TryConfigurePragmasAsync().ConfigureAwait(false);
            await TryCheckpointAsync().ConfigureAwait(false);
            await InitializeSchemaAsync().ConfigureAwait(false);
            isInitialized = true;

            ErrorLog.LogMessage(
                $"Database recovered after {ex.GetType().Name}. Previous DB moved to {backupPath}.",
                "AppDb.TryRecoverDatabaseAsync");
            return true;
        }
        catch (Exception recoveryEx)
        {
            ErrorLog.LogException(recoveryEx, "AppDb.TryRecoverDatabaseAsync", "Failed to rebuild database.");
            return false;
        }
    }

    private async Task EnsureSchemaAsync()
    {
        // MediaSource
        await EnsureColumnAsync("MediaSource", "AccessToken", "TEXT").ConfigureAwait(false);

        // MediaItem (core)
        await EnsureColumnAsync("MediaItem", "Name", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "MediaType", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "DurationMs", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "DateAddedSeconds", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "SizeBytes", "INTEGER").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "SourceId", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "Latitude", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "Longitude", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "ThumbnailPath", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "PeopleTagsSummary", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "FaceScanModelKey", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("MediaItem", "FaceScanAtSeconds", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);

        // FaceEmbedding (People feature)
        await EnsureColumnAsync("FaceEmbedding", "X", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "Y", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "W", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "H", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "ImageWidth", "INTEGER").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "ImageHeight", "INTEGER").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "DetectionModelKey", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "EmbeddingModelKey", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "FaceQuality", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("FaceEmbedding", "Thumb96Path", "TEXT").ConfigureAwait(false);

        // PersonProfile
        await EnsureColumnAsync("PersonProfile", "MergedIntoPersonId", "TEXT").ConfigureAwait(false);
        await EnsureColumnAsync("PersonProfile", "PrimaryFaceEmbeddingId", "INTEGER").ConfigureAwait(false);
        await EnsureColumnAsync("PersonProfile", "QualityScore", "REAL").ConfigureAwait(false);
        await EnsureColumnAsync("PersonProfile", "IsIgnored", "INTEGER").ConfigureAwait(false);

        // FaceScanJob (queue)
        await EnsureColumnAsync("FaceScanJob", "EnqueuedAtSeconds", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
        await EnsureColumnAsync("FaceScanJob", "LastAttemptSeconds", "INTEGER NOT NULL DEFAULT 0")
            .ConfigureAwait(false);
        await EnsureColumnAsync("FaceScanJob", "AttemptCount", "INTEGER NOT NULL DEFAULT 0").ConfigureAwait(false);
    }

    private async Task EnsureIndexesAsync()
    {
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_name ON MediaItem(Name);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_source ON MediaItem(SourceId);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_type ON MediaItem(MediaType);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_location ON MediaItem(Latitude, Longitude);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_media_facescan_modelkey ON MediaItem(FaceScanModelKey);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);

        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_album_item_album ON AlbumItem(AlbumId);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_album_item_media ON AlbumItem(MediaPath);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);

        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_media ON PersonTag(MediaPath);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_person_tag_name ON PersonTag(PersonName);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);

        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_face_embedding_media ON FaceEmbedding(MediaPath);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_face_embedding_person ON FaceEmbedding(PersonId);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);

        await TryExecuteAsync("CREATE INDEX IF NOT EXISTS idx_face_scan_queue_time ON FaceScanJob(EnqueuedAtSeconds);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);

        await TryExecuteAsync(
                "CREATE INDEX IF NOT EXISTS idx_person_profile_merged_into ON PersonProfile(MergedIntoPersonId);",
                "AppDb.EnsureIndexesAsync")
            .ConfigureAwait(false);
    }

    private async Task<HashSet<string>> GetExistingColumnsAsync(string tableName)
    {
        try
        {
            var rows = await Db.QueryAsync<PragmaTableInfo>($"PRAGMA table_info('{tableName}');").ConfigureAwait(false);
            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.name))
                .Select(r => r.name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "AppDb.GetExistingColumnsAsync", $"Table={tableName}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string sqlType)
    {
        var cols = await GetExistingColumnsAsync(tableName).ConfigureAwait(false);
        if (cols.Contains(columnName))
            return;

        var sql = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {sqlType};";
        await TryExecuteAsync(sql, "AppDb.EnsureColumnAsync", $"Table={tableName}; Column={columnName}")
            .ConfigureAwait(false);
    }

    private async Task<bool> TryExecuteAsync(string sql, string context, string? details = null, params object[] args)
    {
        try
        {
            if (args is { Length: > 0 })
                await Db.ExecuteAsync(sql, args).ConfigureAwait(false);
            else
                await Db.ExecuteAsync(sql).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            var msg = BuildSqlErrorMessage(sql, details, args);
            ErrorLog.LogException(ex, context, msg);
            return false;
        }
    }

    private static string BuildSqlErrorMessage(string sql, string? details, object[]? args)
    {
        // Keep logs readable and safe-ish (avoid giant log entries)
        const int maxSqlLen = 4000;
        const int maxDetailsLen = 2000;
        const int maxArgStringLen = 500;
        const int maxCollectionItems = 50;

        static string Clip(string? s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            if (s.Length <= max)
                return s;

            return s.Substring(0, max) + " …(truncated)";
        }

        static string SafeToString(object? value)
        {
            if (value is null)
                return "null";

            try
            {
                return value.ToString() ?? "<null tostring>";
            }
            catch (Exception tex)
            {
                return $"<ToString() threw {tex.GetType().Name}: {tex.Message}>";
            }
        }

        static bool LooksBinaryString(string s)
        {
            // Basic heuristic to avoid logging large/binary-looking strings
            if (s.Length < 64) return false;

            var control = 0;
            for (var i = 0; i < s.Length && i < 256; i++)
            {
                var c = s[i];
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                    control++;
            }

            return control > 5;
        }

        static string QuoteAndClipString(string s)
        {
            // Escape newlines to keep logs single-line per arg if needed
            var normalized = s.Replace("\r", "\\r").Replace("\n", "\\n");
            if (LooksBinaryString(normalized))
                return $"\"{Clip(normalized, 128)}\" <binary-ish>";

            return $"\"{Clip(normalized, maxArgStringLen)}\"";
        }

        static string FormatArg(object? a)
        {
            if (a is null) return "null";

            switch (a)
            {
                case string s:
                    return QuoteAndClipString(s);

                case char ch:
                    return $"'{ch}'";

                case bool b:
                    return b ? "true" : "false";

                case DateTime dt:
                    return dt.ToString("O");

                case DateTimeOffset dto:
                    return dto.ToString("O");

                case TimeSpan ts:
                    return ts.ToString("c");

                case Guid g:
                    return g.ToString("D");

                case byte[] bytes:
                {
                    // Do not dump binary to logs; include length + first bytes as hex (bounded)
                    var take = Math.Min(bytes.Length, 32);
                    var sb = new StringBuilder();
                    sb.Append("bytes[len=").Append(bytes.Length).Append(", head=");
                    for (var i = 0; i < take; i++)
                    {
                        if (i > 0) sb.Append(' ');
                        sb.Append(bytes[i].ToString("X2"));
                    }

                    if (bytes.Length > take) sb.Append(" …");
                    sb.Append(']');
                    return sb.ToString();
                }

                case Stream stream:
                    // Do not dump stream content into logs
                    try
                    {
                        return
                            $"stream(canRead={stream.CanRead}, canSeek={stream.CanSeek}, length={(stream.CanSeek ? stream.Length.ToString() : "<n/a>")})";
                    }
                    catch
                    {
                        return "stream(<unavailable>)";
                    }

                case IDictionary dict:
                {
                    // Avoid huge logs from dictionaries; keep it bounded
                    var count = 0;
                    var sb = new StringBuilder();
                    sb.Append("dict{");

                    foreach (DictionaryEntry de in dict)
                    {
                        if (count >= maxCollectionItems)
                        {
                            sb.Append(" …(truncated)");
                            break;
                        }

                        if (count > 0) sb.Append(", ");
                        sb.Append(SafeToString(de.Key)).Append(": ").Append(SafeToString(de.Value));
                        count++;
                    }

                    sb.Append('}');
                    return Clip(sb.ToString(), maxArgStringLen);
                }

                case IEnumerable enumerable when a is not string:
                {
                    // Avoid huge logs from enumerables; keep it bounded
                    var count = 0;
                    var sb = new StringBuilder();
                    sb.Append('[');

                    foreach (var item in enumerable)
                    {
                        if (count >= maxCollectionItems)
                        {
                            sb.Append(" …(truncated)");
                            break;
                        }

                        if (count > 0) sb.Append(", ");
                        sb.Append(SafeToString(item));
                        count++;
                    }

                    sb.Append(']');
                    return Clip(sb.ToString(), maxArgStringLen);
                }

                default:
                {
                    // Fallback: include type info + ToString
                    var s = Clip(SafeToString(a), maxArgStringLen);
                    return $"{a.GetType().FullName}: {s}";
                }
            }
        }

        var sbMsg = new StringBuilder(1024);

        if (!string.IsNullOrWhiteSpace(details))
        {
            var d = Clip(details, maxDetailsLen);
            sbMsg.Append("DETAILS=").AppendLine(d);
        }

        sbMsg.Append("SQL=").AppendLine(Clip(sql, maxSqlLen));

        if (args is { Length: > 0 })
        {
            sbMsg.AppendLine("ARGS=");
            for (var i = 0; i < args.Length; i++)
                sbMsg.Append("  [")
                    .Append(i)
                    .Append("] ")
                    .AppendLine(FormatArg(args[i]));
        }
        else
        {
            sbMsg.AppendLine("ARGS=<none>");
        }

        return sbMsg.ToString().TrimEnd();
    }

    private sealed class PragmaTableInfo
    {
        // Property name must match PRAGMA output column name.
        // ReSharper disable once InconsistentNaming
        public string name { get; set; } = string.Empty;
    }

    private sealed class WalCheckpointResult
    {
        // Property names must match PRAGMA output column names.
        // ReSharper disable once InconsistentNaming
        public int busy { get; set; }

        // ReSharper disable once InconsistentNaming
        public int log { get; set; }

        // ReSharper disable once InconsistentNaming
        public int checkpointed { get; set; }
    }
}
