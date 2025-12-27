#if ANDROID && !WINDOWS
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Uri = Android.Net.Uri;
#elif WINDOWS
using Windows.Storage;
#endif
using IOPath = System.IO.Path;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public async IAsyncEnumerable<VideoItem> StreamSourceAsync(MediaSource source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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

        var isNetworkPath = root.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = isNetworkPath
                ? System.IO.FileAttributes.System
                : System.IO.FileAttributes.System | System.IO.FileAttributes.ReparsePoint
        };

        List<string> paths;
        // Fix: try/catch darf kein yield enthalten, also try/catch auslagern
        try
        {
            paths = Directory.EnumerateFiles(root, "*.*", options).Where(IsVideoFile).ToList();
        }
        catch
        {
            // Ignore inaccessible paths.
            paths = new List<string>();
        }

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
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
        var resolver = ctx.ContentResolver;

        string[] projection =
        {
            MediaStore.Video.Media.InterfaceConsts.DisplayName,
            MediaStore.Video.Media.InterfaceConsts.Data,
            MediaStore.Video.Media.InterfaceConsts.Duration,
            MediaStore.Video.Media.InterfaceConsts.DateAdded
        };

        using var cursor = resolver.Query(
            MediaStore.Video.Media.ExternalContentUri,
            projection,
            null,
            null,
            $"{MediaStore.Video.Media.InterfaceConsts.DateAdded} DESC");

        if (cursor == null)
            yield break;

        var nameCol = cursor.GetColumnIndex(projection[0]);
        var pathCol = cursor.GetColumnIndex(projection[1]);
        var durCol = cursor.GetColumnIndex(projection[2]);
        var addCol = cursor.GetColumnIndex(projection[3]);

        while (cursor.MoveToNext())
        {
            ct.ThrowIfCancellationRequested();
            var path = cursor.GetString(pathCol) ?? "";
            if (string.IsNullOrWhiteSpace(path))
                continue;

            yield return new VideoItem
            {
                Path = path,
                Name = cursor.GetString(nameCol) ?? IOPath.GetFileName(path),
                DurationMs = cursor.IsNull(durCol) ? 0 : cursor.GetLong(durCol),
                DateAddedSeconds = cursor.IsNull(addCol) ? 0 : cursor.GetLong(addCol),
                SourceId = sourceId
            };
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
        var uri = Uri.Parse(rootPath);
        var root = DocumentFile.FromTreeUri(ctx, uri);

        if (root == null)
            yield break;

        foreach (var item in TraverseDocumentTree(root, sourceId, ct))
            yield return item;
    }

    private static IEnumerable<VideoItem> TraverseDocumentTree(DocumentFile doc, string? sourceId,
        CancellationToken ct)
    {
        foreach (var child in doc.ListFiles())
        {
            ct.ThrowIfCancellationRequested();
            if (child.IsDirectory)
            {
                foreach (var item in TraverseDocumentTree(child, sourceId, ct))
                    yield return item;
                continue;
            }

            var name = child.Name ?? "";
            if (!IsVideoFileName(name))
                continue;

            var lastModified = child.LastModified();
            var added = lastModified > 0 ? lastModified / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            yield return new VideoItem
            {
                Path = child.Uri?.ToString() ?? "",
                Name = name,
                DurationMs = 0,
                DateAddedSeconds = added,
                SourceId = sourceId
            };
        }
    }

    private static IEnumerable<VideoItem> ScanAndroidFileSystem(string rootPath, string? sourceId, CancellationToken ct)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootPath, "*.*", options)
                .Where(IsVideoFileName)
                .ToList();
        }
        catch
        {
            // Ignore inaccessible paths.
            files = new List<string>();
        }

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();
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
#endif
}
