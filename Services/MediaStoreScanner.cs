#if ANDROID && !WINDOWS
using Android.Content;
using Android.Database;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Uri = Android.Net.Uri;
#elif WINDOWS
using Windows.Storage;
#endif
using System.Runtime.CompilerServices;
using UltimateVideoBrowser.Models;
using IOPath = System.IO.Path;

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public async IAsyncEnumerable<VideoItem> StreamSourceAsync(MediaSource source,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sourceId = source.Id;
        var rootPath = source.LocalFolderPath ?? "";
#if ANDROID && !WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            foreach (var item in ScanMediaStore(sourceId, ct))
                yield return item;
            yield break;
        }

        foreach (var item in ScanAndroidFolder(rootPath, sourceId, ct))
            yield return item;
#elif WINDOWS
        await foreach (var item in ScanWindowsAsync(rootPath, sourceId, source.AccessToken, ct))
            yield return item;
#else
        _ = sourceId;
        _ = rootPath;
        await Task.CompletedTask;
#endif
    }

#if WINDOWS
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v"
    };

    private static bool IsVideoFile(string path)
    {
        return VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<VideoItem> ScanWindowsAsync(string? rootPath, string? sourceId,
        string? accessToken, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var root = string.IsNullOrWhiteSpace(rootPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            : rootPath;

        if (string.IsNullOrWhiteSpace(root))
            yield break;

        StorageFolder? folder = null;
        try
        {
            folder = await TryGetStorageFolderAsync(root, accessToken);
        }
        catch
        {
            folder = null;
        }

        if (folder != null)
        {
            await foreach (var item in ScanWindowsFolderAsync(folder, sourceId, ct))
                yield return item;
            yield break;
        }

        if (!Directory.Exists(root))
            yield break;

        foreach (var path in EnumerateVideoFilesStreamingWindows(root, ct))
        {
            ct.ThrowIfCancellationRequested();

            FileInfo? info;
            try
            {
                info = new FileInfo(path);
            }
            catch
            {
                continue;
            }

            var durationMs = 0L;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                var props = await file.Properties.GetVideoPropertiesAsync();
                durationMs = (long)props.Duration.TotalMilliseconds;
            }
            catch
            {
                durationMs = 0;
            }

            yield return new VideoItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                DurationMs = durationMs,
                DateAddedSeconds = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                SourceId = sourceId
            };
        }
    }

    private static IEnumerable<string> EnumerateVideoFilesStreamingWindows(string root, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = queue.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (IsVideoFile(file))
                    yield return file;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                queue.Enqueue(sub);
            }
        }
    }

    private static async Task<StorageFolder?> TryGetStorageFolderAsync(string rootPath, string? accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            try
            {
                return await Windows.Storage.AccessCache.StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(accessToken);
            }
            catch
            {
                // Ignore missing/invalid access tokens.
            }
        }

        try
        {
            return await StorageFolder.GetFolderFromPathAsync(rootPath);
        }
        catch
        {
            return null;
        }
    }

    private static async IAsyncEnumerable<VideoItem> ScanWindowsFolderAsync(StorageFolder root, string? sourceId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var queue = new Queue<StorageFolder>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = queue.Dequeue();
            IReadOnlyList<StorageFile> files;
            try
            {
                files = await folder.GetFilesAsync();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!IsVideoFile(file.Name))
                    continue;

                var durationMs = 0L;
                try
                {
                    var props = await file.Properties.GetVideoPropertiesAsync();
                    durationMs = (long)props.Duration.TotalMilliseconds;
                }
                catch
                {
                    durationMs = 0;
                }

                var path = file.Path;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                yield return new VideoItem
                {
                    Path = path,
                    Name = file.Name,
                    DurationMs = durationMs,
                    DateAddedSeconds = new DateTimeOffset(file.DateCreated.UtcDateTime).ToUnixTimeSeconds(),
                    SourceId = sourceId
                };
            }

            IReadOnlyList<StorageFolder> subfolders;
            try
            {
                subfolders = await folder.GetFoldersAsync();
            }
            catch
            {
                continue;
            }

            foreach (var subfolder in subfolders)
                queue.Enqueue(subfolder);
        }
    }
#endif

#if ANDROID && !WINDOWS
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v"
    };

    private static bool IsVideoFileName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && VideoExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<VideoItem> ScanMediaStore(string? sourceId, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Video.Media.ExternalContentUri;

        if (resolver == null || externalUri == null)
            yield break;

        // Use _ID to build a stable content:// Uri instead of relying on DATA (deprecated / restricted).
        string[] projection =
        {
            MediaStore.Video.Media.InterfaceConsts.Id,
            MediaStore.Video.Media.InterfaceConsts.DisplayName,
            MediaStore.Video.Media.InterfaceConsts.Duration,
            MediaStore.Video.Media.InterfaceConsts.DateAdded
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.Video.Media.InterfaceConsts.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaStore query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.DisplayName);
            var durCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.Duration);
            var addCol = cursor.GetColumnIndex(MediaStore.Video.Media.InterfaceConsts.DateAdded);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try { moved = cursor.MoveToNext(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try { id = cursor.GetLong(idCol); }
                catch { continue; }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                string name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                {
                    try { name = cursor.GetString(nameCol) ?? ""; }
                    catch { name = ""; }
                }

                long durationMs = 0;
                if (durCol >= 0 && !cursor.IsNull(durCol))
                {
                    try { durationMs = cursor.GetLong(durCol); }
                    catch { durationMs = 0; }
                }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                {
                    try { addedSeconds = cursor.GetLong(addCol); }
                    catch { addedSeconds = 0; }
                }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"video_{id}";

                yield return new VideoItem
                {
                    Path = path, // content://media/external/video/media/<id>
                    Name = name,
                    DurationMs = durationMs,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId
                };
            }
        }
    }

    private static IEnumerable<VideoItem> ScanAndroidFolder(string rootPath, string? sourceId, CancellationToken ct)
    {
        if (rootPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return ScanAndroidTreeUri(rootPath, sourceId, ct);

        return ScanAndroidFileSystem(rootPath, sourceId, ct);
    }

    private static IEnumerable<VideoItem> ScanAndroidTreeUri(string rootPath, string? sourceId, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        DocumentFile? root = null;

        try
        {
            var uri = Uri.Parse(rootPath);
            root = DocumentFile.FromTreeUri(ctx, uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SAF tree uri parse/open failed: {ex}");
            root = null;
        }

        if (root == null)
            yield break;

        foreach (var item in TraverseDocumentTree(root, sourceId, ct))
            yield return item;
    }

    private static IEnumerable<VideoItem> TraverseDocumentTree(DocumentFile root, string? sourceId, CancellationToken ct)
    {
        var stack = new Stack<DocumentFile>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = stack.Pop();

            DocumentFile[] children;
            try
            {
                children = dir.ListFiles() ?? Array.Empty<DocumentFile>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SAF ListFiles failed: {ex}");
                continue;
            }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();

                if (child == null)
                    continue;

                bool isDir;
                try { isDir = child.IsDirectory; }
                catch { continue; }

                if (isDir)
                {
                    stack.Push(child);
                    continue;
                }

                string name;
                try { name = child.Name ?? ""; }
                catch { continue; }

                if (!IsVideoFileName(name))
                    continue;

                long lastModifiedMs = 0;
                try { lastModifiedMs = child.LastModified(); }
                catch { lastModifiedMs = 0; }

                var added = lastModifiedMs > 0
                    ? lastModifiedMs / 1000
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                string path = "";
                try { path = child.Uri?.ToString() ?? ""; }
                catch { path = ""; }

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                yield return new VideoItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = added,
                    SourceId = sourceId
                };
            }
        }
    }

    private static IEnumerable<VideoItem> ScanAndroidFileSystem(string rootPath, string? sourceId, CancellationToken ct)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        foreach (var path in EnumerateVideoFilesStreamingAndroid(rootPath, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
                continue;

            yield return new VideoItem
            {
                Path = path,
                Name = IOPath.GetFileName(path),
                DurationMs = 0,
                DateAddedSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SourceId = sourceId
            };
        }
    }

    private static IEnumerable<string> EnumerateVideoFilesStreamingAndroid(string rootPath, CancellationToken ct)
    {
        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dir = queue.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();

                if (SafeIsVideoFile(file))
                    yield return file;
            }

            static bool SafeIsVideoFile(string file)
            {
                try
                {
                    return IsVideoFileName(file);
                }
                catch
                {
                    // Ignore malformed paths/extensions.
                    return false;
                }
            }


            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subdirs)
            {
                ct.ThrowIfCancellationRequested();
                queue.Enqueue(sub);
            }
        }
    }
#endif
}
