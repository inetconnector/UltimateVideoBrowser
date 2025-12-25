using UltimateVideoBrowser.Models;

#if ANDROID && !WINDOWS
using Android.Media;
#elif WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.FileProperties;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class ThumbnailService
{
    readonly string cacheDir;

    public ThumbnailService()
    {
        cacheDir = Path.Combine(FileSystem.CacheDirectory, "thumbs");
        Directory.CreateDirectory(cacheDir);
    }

    public string GetThumbnailPath(VideoItem item)
    {
        var safe = MakeSafeFileName(item.Path);
        return Path.Combine(cacheDir, safe + ".jpg");
    }

    public async Task<string?> EnsureThumbnailAsync(VideoItem item, CancellationToken ct)
    {
        var thumbPath = GetThumbnailPath(item);
        if (File.Exists(thumbPath))
            return thumbPath;

#if ANDROID && !WINDOWS
        return await Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                using var retriever = new MediaMetadataRetriever();
                if (item.Path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = Android.Net.Uri.Parse(item.Path);
                    retriever.SetDataSource(Android.App.Application.Context, uri);
                }
                else
                {
                    retriever.SetDataSource(item.Path);
                }

                // pick ~10% of duration, fallback to 1 second
                var tUs = Math.Max(1_000_000L, (item.DurationMs * 1000L) / 10L);
                using var bmp = retriever.GetFrameAtTime(tUs, Option.ClosestSync);
                if (bmp == null)
                    return null;

                using var fs = File.Open(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None);
                bmp.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 82, fs);
                return thumbPath;
            }
            catch
            {
                return null;
            }
        }, ct);
#elif WINDOWS
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 320);
            if (thumb == null || thumb.Size == 0)
                return null;

            using var input = thumb.AsStreamForRead();
            using var fs = File.Open(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(fs, ct);
            return thumbPath;
        }
        catch
        {
            return null;
        }
#else
        _ = item;
        _ = ct;
        return await Task.FromResult<string?>(null);
#endif
    }

    static string MakeSafeFileName(string input)
    {
        // Hash-like safe filename
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (var ch in input)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash.ToString("X");
        }
    }
}
