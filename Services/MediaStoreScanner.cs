using AndroidX.DocumentFile.Provider;
using UltimateVideoBrowser.Models;
using Application = Android.App.Application;
using Uri = Android.Net.Uri;
#if ANDROID
using Android.Provider;

#elif WINDOWS
using Windows.Storage;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public Task<List<VideoItem>> ScanSourceAsync(MediaSource source)
    {
        var sourceId = source.Id;
        var rootPath = source.LocalFolderPath ?? "";
#if ANDROID
        if (string.IsNullOrWhiteSpace(rootPath))
            // On Android 9 we can read file paths from MediaStore DATA column.
            return Task.Run(() => ScanMediaStore(sourceId));

        return Task.Run(() => ScanAndroidFolder(rootPath, sourceId));
#elif WINDOWS
        return ScanWindowsAsync(rootPath, sourceId);
#else
        _ = sourceId;
        _ = rootPath;
        return Task.FromResult(new List<VideoItem>());
#endif
    }

#if WINDOWS
    static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v"
    };

    static bool IsVideoFile(string path)
        => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    static async Task<List<VideoItem>> ScanWindowsAsync(string? rootPath, string? sourceId)
    {
        var list = new List<VideoItem>();
        var root = string.IsNullOrWhiteSpace(rootPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            : rootPath;

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return list;

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(IsVideoFile))
            {
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

                list.Add(new VideoItem
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    DurationMs = durationMs,
                    DateAddedSeconds = new DateTimeOffset(info.CreationTimeUtc).ToUnixTimeSeconds(),
                    SourceId = sourceId
                });
            }
        }
        catch
        {
            // Ignore inaccessible paths.
        }

        return list;
    }
#endif

#if ANDROID
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v"
    };

    private static bool IsVideoFileName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
               && VideoExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    }

    private static List<VideoItem> ScanMediaStore(string? sourceId)
    {
        var list = new List<VideoItem>();
        var ctx = Application.Context;
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
            return list;

        var nameCol = cursor.GetColumnIndex(projection[0]);
        var pathCol = cursor.GetColumnIndex(projection[1]);
        var durCol = cursor.GetColumnIndex(projection[2]);
        var addCol = cursor.GetColumnIndex(projection[3]);

        while (cursor.MoveToNext())
        {
            var path = cursor.GetString(pathCol) ?? "";
            if (string.IsNullOrWhiteSpace(path))
                continue;

            list.Add(new VideoItem
            {
                Path = path,
                Name = cursor.GetString(nameCol) ?? Path.GetFileName(path),
                DurationMs = cursor.IsNull(durCol) ? 0 : cursor.GetLong(durCol),
                DateAddedSeconds = cursor.IsNull(addCol) ? 0 : cursor.GetLong(addCol),
                SourceId = sourceId
            });
        }

        return list;
    }

    private static List<VideoItem> ScanAndroidFolder(string rootPath, string? sourceId)
    {
        if (rootPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return ScanAndroidTreeUri(rootPath, sourceId);

        return ScanAndroidFileSystem(rootPath, sourceId);
    }

    private static List<VideoItem> ScanAndroidTreeUri(string rootPath, string? sourceId)
    {
        var list = new List<VideoItem>();
        var ctx = Application.Context;
        var uri = Uri.Parse(rootPath);
        var root = DocumentFile.FromTreeUri(ctx, uri);

        if (root == null)
            return list;

        TraverseDocumentTree(root, list, sourceId);
        return list;
    }

    private static void TraverseDocumentTree(DocumentFile doc, List<VideoItem> list, string? sourceId)
    {
        foreach (var child in doc.ListFiles())
        {
            if (child.IsDirectory)
            {
                TraverseDocumentTree(child, list, sourceId);
                continue;
            }

            var name = child.Name ?? "";
            if (!IsVideoFileName(name))
                continue;

            var lastModified = child.LastModified();
            var added = lastModified > 0 ? lastModified / 1000 : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            list.Add(new VideoItem
            {
                Path = child.Uri?.ToString() ?? "",
                Name = name,
                DurationMs = 0,
                DateAddedSeconds = added,
                SourceId = sourceId
            });
        }
    }

    private static List<VideoItem> ScanAndroidFileSystem(string rootPath, string? sourceId)
    {
        var list = new List<VideoItem>();
        if (!Directory.Exists(rootPath))
            return list;

        try
        {
            foreach (var path in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                         .Where(p => IsVideoFileName(p)))
                list.Add(new VideoItem
                {
                    Path = path,
                    Name = Path.GetFileName(path),
                    DurationMs = 0,
                    DateAddedSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    SourceId = sourceId
                });
        }
        catch
        {
            // Ignore inaccessible paths.
        }

        return list;
    }
#endif
}