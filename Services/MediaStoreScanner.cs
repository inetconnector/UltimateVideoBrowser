using Android.Provider;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class MediaStoreScanner
{
    public Task<List<VideoItem>> ScanAllVideosAsync(string? sourceId = null)
    {
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
    }
}
