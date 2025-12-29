#if ANDROID && !WINDOWS
using Android.Content;
using Android.Database;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Uri = Android.Net.Uri;
#elif WINDOWS
using Windows.Storage;
#endif
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltimateVideoBrowser.Models;
using ModelMediaType = UltimateVideoBrowser.Models.MediaType;
using IOPath = System.IO.Path;

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public async IAsyncEnumerable<MediaItem> StreamSourceAsync(MediaSource source, ModelMediaType indexedTypes,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var sourceId = source.Id;
        var rootPath = source.LocalFolderPath ?? "";
#if ANDROID && !WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            foreach (var item in ScanMediaStore(sourceId, indexedTypes, ct))
                yield return item;
            yield break;
        }

        foreach (var item in ScanAndroidFolder(rootPath, sourceId, indexedTypes, ct))
            yield return item;
#elif WINDOWS
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            foreach (var defaultRoot in GetWindowsDefaultRoots(indexedTypes))
                await foreach (var item in ScanWindowsAsync(defaultRoot, sourceId, source.AccessToken, indexedTypes, ct))
                    yield return item;
        }
        else
        {
            await foreach (var item in ScanWindowsAsync(rootPath, sourceId, source.AccessToken, indexedTypes, ct))
                yield return item;
        }
#else
        _ = sourceId;
        _ = rootPath;
        await Task.CompletedTask;
#endif
    }

#if WINDOWS
    private static IEnumerable<string> GetWindowsDefaultRoots(ModelMediaType indexedTypes)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (indexedTypes.HasFlag(ModelMediaType.Videos))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        if (indexedTypes.HasFlag(ModelMediaType.Photos))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (indexedTypes.HasFlag(ModelMediaType.Documents))
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        return roots.Where(root => !string.IsNullOrWhiteSpace(root));
    }

    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };
    private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic" };
    private static readonly string[] DocumentExtensions =
        { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" };

    private static ModelMediaType GetMediaTypeFromPath(string path)
    {
        var ext = Path.GetExtension(path);
        if (VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Videos;
        if (PhotoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Photos;
        if (DocumentExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Documents;
        return ModelMediaType.None;
    }

    private static async IAsyncEnumerable<MediaItem> ScanWindowsAsync(string? rootPath, string? sourceId,
        string? accessToken, ModelMediaType indexedTypes,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var root = rootPath;

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
            await foreach (var item in ScanWindowsFolderAsync(folder, sourceId, indexedTypes, ct))
                yield return item;
            yield break;
        }

        if (!Directory.Exists(root))
            yield break;

        foreach (var path in EnumerateMediaFilesStreamingWindows(root, indexedTypes, ct))
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

            var mediaType = GetMediaTypeFromPath(path);
            if (mediaType == ModelMediaType.None || !indexedTypes.HasFlag(mediaType))
                continue;

            var durationMs = 0L;
            if (mediaType == ModelMediaType.Videos)
            {
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
            }

            yield return new MediaItem
            {
                Path = path,
                Name = Path.GetFileName(path),
                DurationMs = durationMs,
                DateAddedSeconds = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                SourceId = sourceId,
                MediaType = mediaType
            };
        }
    }

    private static IEnumerable<string> EnumerateMediaFilesStreamingWindows(string root, ModelMediaType indexedTypes,
        CancellationToken ct)
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
                var mediaType = GetMediaTypeFromPath(file);
                if (mediaType != ModelMediaType.None && indexedTypes.HasFlag(mediaType))
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

    private static async IAsyncEnumerable<MediaItem> ScanWindowsFolderAsync(StorageFolder root, string? sourceId,
        ModelMediaType indexedTypes, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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
                var mediaType = GetMediaTypeFromPath(file.Name);
                if (mediaType == ModelMediaType.None || !indexedTypes.HasFlag(mediaType))
                    continue;

                var durationMs = 0L;
                if (mediaType == ModelMediaType.Videos)
                {
                    try
                    {
                        var props = await file.Properties.GetVideoPropertiesAsync();
                        durationMs = (long)props.Duration.TotalMilliseconds;
                    }
                    catch
                    {
                        durationMs = 0;
                    }
                }

                var path = file.Path;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                yield return new MediaItem
                {
                    Path = path,
                    Name = file.Name,
                    DurationMs = durationMs,
                    DateAddedSeconds = new DateTimeOffset(file.DateCreated.UtcDateTime).ToUnixTimeSeconds(),
                    SourceId = sourceId,
                    MediaType = mediaType
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
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };
    private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic" };
    private static readonly string[] DocumentExtensions =
        { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" };

    private static ModelMediaType GetMediaTypeFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ModelMediaType.None;

        var ext = Path.GetExtension(name);
        if (VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Videos;
        if (PhotoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Photos;
        if (DocumentExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return ModelMediaType.Documents;
        return ModelMediaType.None;
    }

    private static IEnumerable<MediaItem> ScanMediaStore(string? sourceId, ModelMediaType indexedTypes,
        CancellationToken ct)
    {
        if (indexedTypes.HasFlag(ModelMediaType.Videos))
            foreach (var item in ScanMediaStoreVideos(sourceId, ct))
                yield return item;

        if (indexedTypes.HasFlag(ModelMediaType.Photos))
            foreach (var item in ScanMediaStoreImages(sourceId, ct))
                yield return item;

        if (indexedTypes.HasFlag(ModelMediaType.Documents))
            foreach (var item in ScanMediaStoreDocuments(sourceId, ct))
                yield return item;
    }

    private static IEnumerable<MediaItem> ScanMediaStoreVideos(string? sourceId, CancellationToken ct)
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
            Debug.WriteLine($"MediaStore query failed: {ex}");
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
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                long durationMs = 0;
                if (durCol >= 0 && !cursor.IsNull(durCol))
                    try
                    {
                        durationMs = cursor.GetLong(durCol);
                    }
                    catch
                    {
                        durationMs = 0;
                    }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"video_{id}";

                yield return new MediaItem
                {
                    Path = path, // content://media/external/video/media/<id>
                    Name = name,
                    DurationMs = durationMs,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = ModelMediaType.Videos
                };
            }
        }
    }

    private static IEnumerable<MediaItem> ScanMediaStoreImages(string? sourceId, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Images.Media.ExternalContentUri;

        if (resolver == null || externalUri == null)
            yield break;

        string[] projection =
        {
            MediaStore.Images.Media.InterfaceConsts.Id,
            MediaStore.Images.Media.InterfaceConsts.DisplayName,
            MediaStore.Images.Media.InterfaceConsts.DateAdded
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.Images.Media.InterfaceConsts.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MediaStore images query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.DisplayName);
            var addCol = cursor.GetColumnIndex(MediaStore.Images.Media.InterfaceConsts.DateAdded);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Image cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"photo_{id}";

                yield return new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = ModelMediaType.Photos
                };
            }
        }
    }

    private static IEnumerable<MediaItem> ScanMediaStoreDocuments(string? sourceId, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var resolver = ctx?.ContentResolver;
        var externalUri = MediaStore.Files.GetContentUri("external");

        if (resolver == null || externalUri == null)
            yield break;

        string[] projection =
        {
            MediaStore.Files.FileColumns.Id,
            MediaStore.Files.FileColumns.DisplayName,
            MediaStore.Files.FileColumns.MimeType,
            MediaStore.Files.FileColumns.DateAdded
        };

        ICursor? cursor = null;
        try
        {
            cursor = resolver.Query(
                externalUri,
                projection,
                null,
                null,
                $"{MediaStore.Files.FileColumns.DateAdded} DESC");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MediaStore documents query failed: {ex}");
            yield break;
        }

        using (cursor)
        {
            if (cursor == null)
                yield break;

            var idCol = cursor.GetColumnIndex(MediaStore.Files.FileColumns.Id);
            var nameCol = cursor.GetColumnIndex(MediaStore.Files.FileColumns.DisplayName);
            var addCol = cursor.GetColumnIndex(MediaStore.Files.FileColumns.DateAdded);

            if (idCol < 0)
                yield break;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool moved;
                try
                {
                    moved = cursor.MoveToNext();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Documents cursor iteration failed: {ex}");
                    yield break;
                }

                if (!moved)
                    yield break;

                long id;
                try
                {
                    id = cursor.GetLong(idCol);
                }
                catch
                {
                    continue;
                }

                var name = "";
                if (nameCol >= 0 && !cursor.IsNull(nameCol))
                    try
                    {
                        name = cursor.GetString(nameCol) ?? "";
                    }
                    catch
                    {
                        name = "";
                    }

                var mediaType = GetMediaTypeFromName(name);
                if (mediaType != ModelMediaType.Documents)
                    continue;

                var itemUri = ContentUris.WithAppendedId(externalUri, id);
                var path = itemUri?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                long addedSeconds = 0;
                if (addCol >= 0 && !cursor.IsNull(addCol))
                    try
                    {
                        addedSeconds = cursor.GetLong(addCol);
                    }
                    catch
                    {
                        addedSeconds = 0;
                    }

                if (string.IsNullOrWhiteSpace(name))
                    name = $"document_{id}";

                yield return new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = addedSeconds,
                    SourceId = sourceId,
                    MediaType = ModelMediaType.Documents
                };
            }
        }
    }

    private static IEnumerable<MediaItem> ScanAndroidFolder(string rootPath, string? sourceId, ModelMediaType indexedTypes,
        CancellationToken ct)
    {
        if (rootPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return ScanAndroidTreeUri(rootPath, sourceId, indexedTypes, ct);

        return ScanAndroidFileSystem(rootPath, sourceId, indexedTypes, ct);
    }

    private static IEnumerable<MediaItem> ScanAndroidTreeUri(string rootPath, string? sourceId,
        ModelMediaType indexedTypes, CancellationToken ct)
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
            Debug.WriteLine($"SAF tree uri parse/open failed: {ex}");
            root = null;
        }

        if (root == null)
            yield break;

        foreach (var item in TraverseDocumentTree(root, sourceId, indexedTypes, ct))
            yield return item;
    }

    private static IEnumerable<MediaItem> TraverseDocumentTree(DocumentFile root, string? sourceId,
        ModelMediaType indexedTypes, CancellationToken ct)
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
                Debug.WriteLine($"SAF ListFiles failed: {ex}");
                continue;
            }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();

                if (child == null)
                    continue;

                bool isDir;
                try
                {
                    isDir = child.IsDirectory;
                }
                catch
                {
                    continue;
                }

                if (isDir)
                {
                    stack.Push(child);
                    continue;
                }

                string name;
                try
                {
                    name = child.Name ?? "";
                }
                catch
                {
                    continue;
                }

                var mediaType = GetMediaTypeFromName(name);
                if (mediaType == ModelMediaType.None || !indexedTypes.HasFlag(mediaType))
                    continue;

                long lastModifiedMs = 0;
                try
                {
                    lastModifiedMs = child.LastModified();
                }
                catch
                {
                    lastModifiedMs = 0;
                }

                var added = lastModifiedMs > 0
                    ? lastModifiedMs / 1000
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                var path = "";
                try
                {
                    path = child.Uri?.ToString() ?? "";
                }
                catch
                {
                    path = "";
                }

                if (string.IsNullOrWhiteSpace(path))
                    continue;

                yield return new MediaItem
                {
                    Path = path,
                    Name = name,
                    DurationMs = 0,
                    DateAddedSeconds = added,
                    SourceId = sourceId,
                    MediaType = mediaType
                };
            }
        }
    }

    private static IEnumerable<MediaItem> ScanAndroidFileSystem(string rootPath, string? sourceId,
        ModelMediaType indexedTypes, CancellationToken ct)
    {
        if (!Directory.Exists(rootPath))
            yield break;

        foreach (var path in EnumerateMediaFilesStreamingAndroid(rootPath, indexedTypes, ct))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path))
                continue;

            var mediaType = GetMediaTypeFromName(path);
            if (mediaType == ModelMediaType.None || !indexedTypes.HasFlag(mediaType))
                continue;

            yield return new MediaItem
            {
                Path = path,
                Name = IOPath.GetFileName(path),
                DurationMs = 0,
                DateAddedSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                SourceId = sourceId,
                MediaType = mediaType
            };
        }
    }

    private static IEnumerable<string> EnumerateMediaFilesStreamingAndroid(string rootPath, ModelMediaType indexedTypes,
        CancellationToken ct)
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

                if (SafeIsMediaFile(file, indexedTypes))
                    yield return file;
            }

            static bool SafeIsMediaFile(string file, ModelMediaType indexedTypes)
            {
                try
                {
                    var mediaType = GetMediaTypeFromName(file);
                    return mediaType != ModelMediaType.None && indexedTypes.HasFlag(mediaType);
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
