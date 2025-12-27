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

    public string GetThumbnailPath(VideoItem item)
    {
        var safe = MakeSafeFileName(item.Path);
        return IOPath.Combine(cacheDir, safe + ".jpg");
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
                    var uri = Uri.Parse(item.Path);
                    retriever.SetDataSource(Platform.AppContext, uri);
                }
                else
                {
                    retriever.SetDataSource(item.Path);
                }

                // pick ~10% of duration, fallback to 1 second
                var tUs = Math.Max(1_000_000L, item.DurationMs * 1000L / 10L);
                using var bmp = retriever.GetFrameAtTime(tUs, Option.ClosestSync);
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

            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 320, ThumbnailOptions.UseCurrentScale);
            if (thumb == null || thumb.Size == 0)
            {
                using var fallbackThumb = await file.GetThumbnailAsync(ThumbnailMode.VideosView, 320);
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
    private async Task<StorageFile?> GetStorageFileAsync(VideoItem item)
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
