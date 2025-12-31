using UltimateVideoBrowser.Models;
using IOPath = System.IO.Path;

#if ANDROID && !WINDOWS
using Android.Graphics;
using Android.Media;
using Uri = Android.Net.Uri;

#elif WINDOWS
using Windows.Storage;
using Windows.Storage.FileProperties;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class ThumbnailService
{
    private readonly string cacheDir;
    private readonly ISourceService sourceService;
#if WINDOWS
    private IReadOnlyDictionary<string, string> sourceTokens = new Dictionary<string, string>();
#endif

    public ThumbnailService(ISourceService sourceService)
    {
        this.sourceService = sourceService;
        cacheDir = IOPath.Combine(FileSystem.CacheDirectory, "thumbs");
        Directory.CreateDirectory(cacheDir);
    }

    public string GetThumbnailPath(MediaItem item)
    {
        var safe = MakeSafeFileName(item.Path);
        return IOPath.Combine(cacheDir, safe + ".jpg");
    }

    public async Task<string?> EnsureThumbnailAsync(MediaItem item, CancellationToken ct)
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

                using var bmp = item.MediaType switch
                {
                    MediaType.Photos => LoadImageBitmap(item.Path),
                    MediaType.Videos => LoadVideoBitmap(item),
                    _ => null
                };

                if (bmp == null)
                    return null;

                using var fs = File.Open(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var format = Bitmap.CompressFormat.Jpeg;
                if (format == null)
                    return null;

                bmp.Compress(format, 82, fs);
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
            var file = await GetStorageFileAsync(item);
            if (file == null)
                return null;

            using var thumb =
                await file.GetThumbnailAsync(GetThumbnailMode(item.MediaType), 320, ThumbnailOptions.UseCurrentScale);
            if (thumb == null || thumb.Size == 0)
            {
                using var fallbackThumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 320);
                if (fallbackThumb == null || fallbackThumb.Size == 0)
                    return null;

                using var fallbackInput = fallbackThumb.AsStreamForRead();
                using var fallbackStream = File.Open(thumbPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await fallbackInput.CopyToAsync(fallbackStream, ct);
                return thumbPath;
            }

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

#if WINDOWS
    private async Task<StorageFile?> GetStorageFileAsync(MediaItem item)
    {
        try
        {
            return await StorageFile.GetFileFromPathAsync(item.Path);
        }
        catch
        {
            var token = await GetSourceTokenAsync(item.SourceId);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                var folder = await Windows.Storage.AccessCache.StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(token);

                if (string.IsNullOrWhiteSpace(folder.Path))
                    return null;

                var relativePath = item.Path.Replace(folder.Path, string.Empty)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relativePath))
                    return null;

                return await GetFileFromFolderAsync(folder, relativePath);
            }
            catch
            {
                return null;
            }
        }
    }

    private async Task<string?> GetSourceTokenAsync(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return null;

        if (!sourceTokens.TryGetValue(sourceId, out var token))
        {
            var sources = await sourceService.GetSourcesAsync();
            sourceTokens = sources
                .Where(s => !string.IsNullOrWhiteSpace(s.AccessToken))
                .ToDictionary(s => s.Id, s => s.AccessToken ?? string.Empty);

            sourceTokens.TryGetValue(sourceId, out token);
        }

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static async Task<StorageFile> GetFileFromFolderAsync(StorageFolder root, string relativePath)
    {
        var segments = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
            current = await current.GetFolderAsync(segments[i]);

        return await current.GetFileAsync(segments[^1]);
    }
#endif

#if ANDROID && !WINDOWS
    private const int ThumbMaxSize = 320;

    private static Stream? OpenPathStream(string path)
    {
        try
        {
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                return Platform.AppContext.ContentResolver?.OpenInputStream(Uri.Parse(path));

            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static int CalculateInSampleSize(BitmapFactory.Options options, int reqWidth, int reqHeight)
    {
        var height = options.OutHeight;
        var width = options.OutWidth;
        var inSampleSize = 1;

        if (height > reqHeight || width > reqWidth)
        {
            var halfHeight = height / 2;
            var halfWidth = width / 2;

            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
                inSampleSize *= 2;
        }

        return Math.Max(1, inSampleSize);
    }

    private static Bitmap? LoadImageBitmap(string path)
    {
        try
        {
            // Pass 1: bounds
            using (var boundsStream = OpenPathStream(path))
            {
                if (boundsStream == null)
                    return null;

                var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                BitmapFactory.DecodeStream(boundsStream, null, bounds);

                // Pass 2: sampled decode
                var sample = CalculateInSampleSize(bounds, ThumbMaxSize, ThumbMaxSize);
                var opts = new BitmapFactory.Options
                {
                    InJustDecodeBounds = false,
                    InSampleSize = sample,
                    InPreferredConfig = Bitmap.Config.Rgb565,
                    InDither = true
                };

                using var decodeStream = OpenPathStream(path);
                if (decodeStream == null)
                    return null;

                return BitmapFactory.DecodeStream(decodeStream, null, opts);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadVideoBitmap(MediaItem item)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();
            if (item.Path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = Uri.Parse(item.Path);
                retriever.SetDataSource(Platform.AppContext, uri);
            }
            else
            {
                retriever.SetDataSource(item.Path);
            }

            var tUs = Math.Max(1_000_000L, item.DurationMs * 1000L / 10L);

            // Prefer scaled extraction when available (API 27+), otherwise scale after extraction.
            Bitmap? frame = null;
            if ((int)Android.OS.Build.VERSION.SdkInt >= 27)
            {
                try
                {
                    frame = retriever.GetScaledFrameAtTime(tUs, Option.ClosestSync, ThumbMaxSize, ThumbMaxSize);
                }
                catch
                {
                    frame = null;
                }
            }

            frame ??= retriever.GetFrameAtTime(tUs, Option.ClosestSync);
            if (frame == null)
                return null;

            // Scale down in-memory if needed.
            if (frame.Width <= ThumbMaxSize && frame.Height <= ThumbMaxSize)
                return frame;

            var scale = Math.Min((double)ThumbMaxSize / frame.Width, (double)ThumbMaxSize / frame.Height);
            var w = Math.Max(1, (int)Math.Round(frame.Width * scale));
            var h = Math.Max(1, (int)Math.Round(frame.Height * scale));
            var scaled = Bitmap.CreateScaledBitmap(frame, w, h, true);
            frame.Dispose();
            return scaled;
        }
        catch
        {
            return null;
        }
    }
#endif

#if WINDOWS
    private static ThumbnailMode GetThumbnailMode(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Photos => ThumbnailMode.PicturesView,
            MediaType.Documents => ThumbnailMode.DocumentsView,
            _ => ThumbnailMode.VideosView
        };
    }
#endif

    private static string MakeSafeFileName(string input)
    {
        // Hash-like safe filename
        unchecked
        {
            var hash = 1469598103934665603UL;
            foreach (var ch in input)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }

            return hash.ToString("X");
        }
    }
}