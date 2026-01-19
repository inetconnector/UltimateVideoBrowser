using System.Collections.Concurrent;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using IOPath = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

#if ANDROID && !WINDOWS
using Android.OS;
using Android.Graphics;
using Android.Media;
using Uri = Android.Net.Uri;
using SysStream = System.IO.Stream;

#elif WINDOWS
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.FileProperties;
#endif


namespace UltimateVideoBrowser.Services;

public sealed class ThumbnailService
{
    private const int ThumbMaxSize = 320;
    private const int ThumbQuality = 82;
    private const long DefaultCacheSizeLimitBytes = 512L * 1024 * 1024;

    private readonly ISourceService sourceService;
    private readonly SemaphoreSlim cacheTrimGate = new(1, 1);

    public long CacheSizeLimitBytes { get; set; } = DefaultCacheSizeLimitBytes;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> thumbLocks
        = new(StringComparer.OrdinalIgnoreCase);

#if WINDOWS
    private IReadOnlyDictionary<string, string> sourceTokens = new Dictionary<string, string>();
#endif

    public ThumbnailService(ISourceService sourceService)
    {
        this.sourceService = sourceService;
        ThumbnailsDirectoryPath = IOPath.Combine(FileSystem.CacheDirectory, "thumbs");
        Directory.CreateDirectory(ThumbnailsDirectoryPath);
    }

    public string ThumbnailsDirectoryPath { get; }

    public string GetThumbnailPath(MediaItem item)
    {
        var safe = MakeSafeFileName(item.Path ?? string.Empty);
        return IOPath.Combine(ThumbnailsDirectoryPath, safe + ".png");
    }

    public void DeleteThumbnailForPath(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return;

        var safe = MakeSafeFileName(mediaPath);
        var path = IOPath.Combine(ThumbnailsDirectoryPath, safe + ".png");
        TryDeleteFile(path);
    }

    public Task<string?> EnsureThumbnailAsync(string path, MediaType mediaType, CancellationToken ct)
    {
        // Keep behavior consistent with the MediaItem-based API.
        if (string.IsNullOrWhiteSpace(path))
            return Task.FromResult<string?>(null);

        var item = new MediaItem
        {
            Path = path,
            MediaType = mediaType
        };

        return EnsureThumbnailAsync(item, ct);
    }

    public async Task<string?> EnsureThumbnailAsync(MediaItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
            return null;

        var thumbPath = GetThumbnailPath(item);

        if (IsUsableThumbFile(thumbPath))
        {
            TouchThumbFile(thumbPath);
            return thumbPath;
        }

        // Prevent concurrent writers from producing corrupted/0-byte thumbnails.
        var gate = thumbLocks.GetOrAdd(thumbPath, _ => new SemaphoreSlim(1, 1));
        var lockTaken = false;
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            lockTaken = true;
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            // Re-check after we acquired the lock.
            if (IsUsableThumbFile(thumbPath))
            {
                TouchThumbFile(thumbPath);
                return thumbPath;
            }

#if ANDROID && !WINDOWS
            var tmpPath = GetTempPath(thumbPath);
            return await Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    using var bmp = item.MediaType switch
                    {
                        MediaType.Photos or MediaType.Graphics => LoadImageBitmap(item.Path),
                        MediaType.Videos => LoadVideoBitmap(item),
                        _ => null
                    };

                    if (bmp == null)
                        return null;

                    Directory.CreateDirectory(IOPath.GetDirectoryName(thumbPath) ?? ThumbnailsDirectoryPath);
                    using (var fs = File.Open(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var format = Bitmap.CompressFormat.Png;
                        if (format == null)
                            return null;

                        // Write fully to temp file first to avoid partially written thumbnails being picked up by the UI.
                        bmp.Compress(format, ThumbQuality, fs);
                        fs.Flush();
                    }

                    if (!IsUsableThumbFile(tmpPath))
                        return null;

                    File.Move(tmpPath, thumbPath, true);
                    FinalizeNewThumbnailAsync(thumbPath, ct).GetAwaiter().GetResult();
                    return thumbPath;
                }
                catch (Exception ex)
                {
                    ErrorLog.LogException(ex, "ThumbnailService.EnsureThumbnailAsync(Android)", $"Path={item.Path}");
                    return null;
                }
                finally
                {
                    TryDeleteFile(tmpPath);
                }
            }, ct).ConfigureAwait(false);

#elif WINDOWS
            var tmpPath = GetTempPath(thumbPath);
            try
            {
                var isPhoto = item.MediaType is MediaType.Photos or MediaType.Graphics;
                var file = await GetStorageFileAsync(item).ConfigureAwait(false);

                if (isPhoto)
                {
                    Directory.CreateDirectory(IOPath.GetDirectoryName(thumbPath) ?? ThumbnailsDirectoryPath);
                    if (await TryWritePhotoThumbnailAsync(item.Path, tmpPath, ct).ConfigureAwait(false))
                    {
                        File.Move(tmpPath, thumbPath, true);
                        await FinalizeNewThumbnailAsync(thumbPath, ct).ConfigureAwait(false);
                        return thumbPath;
                    }

                    if (file != null)
                    {
                        await using var stream = await file.OpenStreamForReadAsync().ConfigureAwait(false);
                        if (await TryWritePhotoThumbnailStreamAsync(stream, tmpPath, ct).ConfigureAwait(false))
                        {
                            File.Move(tmpPath, thumbPath, true);
                            await FinalizeNewThumbnailAsync(thumbPath, ct).ConfigureAwait(false);
                            return thumbPath;
                        }
                    }
                }

                if (file == null)
                {
                    return null;
                }

                using var thumb =
                    await file.GetThumbnailAsync(GetThumbnailMode(item.MediaType), ThumbMaxSize,
                        ThumbnailOptions.UseCurrentScale);
                if (thumb == null || thumb.Size == 0)
                {
                    using var fallbackThumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, ThumbMaxSize);
                    if (fallbackThumb == null || fallbackThumb.Size == 0)
                        return null;

                    using var fallbackInput = fallbackThumb.AsStreamForRead();
                    if (!await TryWriteThumbnailStreamAsync(fallbackInput, tmpPath, ct).ConfigureAwait(false))
                        return null;

                    File.Move(tmpPath, thumbPath, true);
                    await FinalizeNewThumbnailAsync(thumbPath, ct).ConfigureAwait(false);
                    return thumbPath;
                }

                using var input = thumb.AsStreamForRead();
                if (!await TryWriteThumbnailStreamAsync(input, tmpPath, ct).ConfigureAwait(false))
                    return null;

                File.Move(tmpPath, thumbPath, true);
                await FinalizeNewThumbnailAsync(thumbPath, ct).ConfigureAwait(false);
                return thumbPath;
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "ThumbnailService.EnsureThumbnailAsync(Windows)", $"Path={item.Path}");
                return null;
            }
            finally
            {
                TryDeleteFile(tmpPath);
            }
#else
            _ = item;
            _ = ct;
            return await Task.FromResult<string?>(null);
#endif
        }
        finally
        {
            if (lockTaken)
                gate.Release();

            // Best-effort cleanup to keep the dictionary from growing unbounded.
            if (lockTaken && gate.CurrentCount == 1)
                thumbLocks.TryRemove(thumbPath, out _);
        }
    }

    public async Task<string?> EnsureThumbnailWithRetryAsync(
        MediaItem item,
        TimeSpan maxDuration,
        TimeSpan retryDelay,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(maxDuration);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var path = await EnsureThumbnailAsync(item, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(path))
                return path;

            if (DateTime.UtcNow >= deadline)
                return null;

            await Task.Delay(retryDelay, ct).ConfigureAwait(false);
        }
    }

    private void TouchThumbFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(path, now);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.TouchThumbFile", $"Path={path}");
        }
    }

    private async Task FinalizeNewThumbnailAsync(string path, CancellationToken ct)
    {
        TouchThumbFile(path);
        await TrimCacheAsync(ct).ConfigureAwait(false);
    }

    private async Task TrimCacheAsync(CancellationToken ct)
    {
        var limit = CacheSizeLimitBytes;
        if (limit <= 0)
            return;

        if (!await cacheTrimGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            if (!Directory.Exists(ThumbnailsDirectoryPath))
                return;

            var files = Directory
                .EnumerateFiles(ThumbnailsDirectoryPath, "*.png", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(fi => fi.Exists)
                .OrderBy(fi => fi.LastWriteTimeUtc)
                .ToList();

            long total = 0;
            foreach (var file in files)
                total += file.Length;

            if (total <= limit)
                return;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                TryDeleteFile(file.FullName);
                total -= file.Length;
                if (total <= limit)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation during cache trimming.
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.TrimCacheAsync", $"CacheDir={ThumbnailsDirectoryPath}");
        }
        finally
        {
            cacheTrimGate.Release();
        }
    }

#if WINDOWS
    private async Task<StorageFile?> GetStorageFileAsync(MediaItem item)
    {
        try
        {
            return await StorageFile.GetFileFromPathAsync(item.Path);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.GetStorageFileAsync", $"Path={item.Path}");
            var token = await GetSourceTokenAsync(item.SourceId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                var folder = await StorageApplicationPermissions
                    .FutureAccessList
                    .GetFolderAsync(token);

                if (string.IsNullOrWhiteSpace(folder.Path))
                    return null;

                var relativePath = item.Path.Replace(folder.Path, string.Empty)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(relativePath))
                    return null;

                return await GetFileFromFolderAsync(folder, relativePath).ConfigureAwait(false);
            }
            catch (Exception ex1)
            {
                ErrorLog.LogException(ex1, "ThumbnailService.GetStorageFileAsync(Fallback)", $"Path={item.Path}");
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
            var sources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
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
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
            current = await current.GetFolderAsync(segments[i]);

        return await current.GetFileAsync(segments[^1]);
    }

    private static async Task<bool> TryWritePhotoThumbnailAsync(string sourcePath, string tmpPath, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return false;

            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await TryWritePhotoThumbnailStreamAsync(input, tmpPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.TryWritePhotoThumbnailAsync", $"Path={sourcePath}");
            return false;
        }
    }

    private static async Task<bool> TryWritePhotoThumbnailStreamAsync(Stream input, string tmpPath, CancellationToken ct)
    {
        try
        {
            if (input.CanSeek)
                input.Position = 0;

            using var image = await ImageSharpImage.LoadAsync(input, ct).ConfigureAwait(false);
            image.Mutate(ctx =>
            {
                ctx.AutoOrient();
                ctx.Resize(new ResizeOptions
                {
                    Mode = ImageSharpResizeMode.Max,
                    Size = new ImageSharpSize(ThumbMaxSize, ThumbMaxSize)
                });
            });

            var encoder = new PngEncoder();
            await image
                .SaveAsPngAsync(tmpPath, encoder, ct)
                .ConfigureAwait(false);
            return IsUsableThumbFile(tmpPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.TryWritePhotoThumbnailStreamAsync");
            return false;
        }
    }

    private static async Task<bool> TryWriteThumbnailStreamAsync(Stream input, string tmpPath, CancellationToken ct)
    {
        try
        {
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, ct).ConfigureAwait(false);
            if (buffer.Length < 128)
                return false;

            buffer.Position = 0;
            using var image = await ImageSharpImage.LoadAsync(buffer, ct).ConfigureAwait(false);
            image.Mutate(ctx =>
            {
                ctx.AutoOrient();
                ctx.Resize(new ResizeOptions
                {
                    Mode = ImageSharpResizeMode.Max,
                    Size = new ImageSharpSize(ThumbMaxSize, ThumbMaxSize)
                });
            });

            var encoder = new PngEncoder();
            await image.SaveAsPngAsync(tmpPath, encoder, ct).ConfigureAwait(false);
            return IsUsableThumbFile(tmpPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.TryWriteThumbnailStreamAsync");
            return false;
        }
    }

#endif

#if ANDROID && !WINDOWS
    private static SysStream? OpenPathStream(string path)
    {
        try
        {
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                return Platform.AppContext.ContentResolver?.OpenInputStream(Uri.Parse(path));

            return File.Exists(path) ? File.OpenRead(path) : null;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.OpenPathStream", $"Path={path}");
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

            while (halfHeight / inSampleSize >= reqHeight && halfWidth / inSampleSize >= reqWidth)
                inSampleSize *= 2;
        }

        return Math.Max(1, inSampleSize);
    }

    private static Bitmap? LoadImageBitmap(string path)
    {
        try
        {
            // Pass 1: bounds
            var boundsStream = OpenPathStream(path);
            if (boundsStream is null)
                return null;

            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            using (boundsStream)
            {
                BitmapFactory.DecodeStream(boundsStream, null, bounds);
            }

            // Pass 2: sampled decode
            var sample = CalculateInSampleSize(bounds, ThumbMaxSize, ThumbMaxSize);
            var opts = new BitmapFactory.Options
            {
                InJustDecodeBounds = false,
                InSampleSize = sample,
                InPreferredConfig = Bitmap.Config.Rgb565,
                InDither = true
            };

            var decodeStream = OpenPathStream(path);
            if (decodeStream is null)
                return null;

            using (decodeStream)
            {
                return BitmapFactory.DecodeStream(decodeStream, null, opts);
            }
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.LoadImageBitmap", $"Path={path}");
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
            if ((int)Build.VERSION.SdkInt >= 27)
                try
                {
                    frame = retriever.GetScaledFrameAtTime(tUs, Option.ClosestSync, ThumbMaxSize, ThumbMaxSize);
                }
                catch (Exception ex)
                {
                    ErrorLog.LogException(ex, "ThumbnailService.LoadVideoBitmap(ScaledFrame)",
                        $"Path={item.Path}");
                    frame = null;
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
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.LoadVideoBitmap", $"Path={item.Path}");
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
            MediaType.Graphics => ThumbnailMode.PicturesView,
            MediaType.Documents => ThumbnailMode.DocumentsView,
            _ => ThumbnailMode.VideosView
        };
    }
#endif

    private static bool IsUsableThumbFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var fi = new FileInfo(path);
            if (fi.Length < 128)
            {
                TryDeleteFile(path);
                return false;
            }

            // Very cheap sanity checks: PNG signature.
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> sig = stackalloc byte[8];
            if (fs.Read(sig) != 8)
            {
                TryDeleteFile(path);
                return false;
            }

            if (sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47 ||
                sig[4] != 0x0D || sig[5] != 0x0A || sig[6] != 0x1A || sig[7] != 0x0A)
            {
                TryDeleteFile(path);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "ThumbnailService.IsUsableThumbFile", $"Path={path}");
            return false;
        }
    }

    private static string GetTempPath(string finalPath)
    {
        return finalPath + ".tmp_" + Guid.NewGuid().ToString("N");
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
            try
            {
                if (!File.Exists(path))
                    return;

                File.Delete(path);
                return;
            }
            catch (IOException ex) when (IsFileInUse(ex))
            {
                if (attempt == maxAttempts - 1)
                    return;

                Thread.Sleep(50 * (attempt + 1));
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "ThumbnailService.TryDeleteFile", $"Path={path}");
                return;
            }
    }

    private static bool IsFileInUse(IOException ex)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);
        return ex.HResult == sharingViolation || ex.HResult == lockViolation;
    }

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
