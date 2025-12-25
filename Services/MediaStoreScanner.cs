using UltimateVideoBrowser.Models;

#if ANDROID
using Android.Provider;
#elif WINDOWS
using Windows.Storage;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public Task<List<VideoItem>> ScanAllVideosAsync(string? sourceId = null)
    {
#if ANDROID
        // On Android 9 we can read file paths from MediaStore DATA column.
        return Task.Run(() =>
        {
            var list = new List<VideoItem>();
            var ctx = Android.App.Application.Context;
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
            var durCol  = cursor.GetColumnIndex(projection[2]);
            var addCol  = cursor.GetColumnIndex(projection[3]);

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
        });
#elif WINDOWS
        return ScanWindowsAsync(sourceId);
#else
        _ = sourceId;
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

    static async Task<List<VideoItem>> ScanWindowsAsync(string? sourceId)
    {
        var list = new List<VideoItem>();
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return list;

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

        return list;
    }
#endif
}
