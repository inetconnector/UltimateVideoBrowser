using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services.Faces;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRectangle = SixLabors.ImageSharp.Rectangle;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;

#if ANDROID && !WINDOWS
using Uri = Android.Net.Uri;
#endif

#if WINDOWS
using Windows.Storage;
using Windows.Storage.AccessCache;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class FaceThumbnailService
{
    private readonly string cacheDir;

#if WINDOWS
    private readonly ISourceService sourceService;
    private readonly SemaphoreSlim sourcesGate = new(1, 1);
    private IReadOnlyList<MediaSource> cachedSources = Array.Empty<MediaSource>();
    private DateTimeOffset cachedSourcesAt = DateTimeOffset.MinValue;
#endif

    public FaceThumbnailService(
#if WINDOWS
        ISourceService sourceService
#endif
    )
    {
#if WINDOWS
        this.sourceService = sourceService;
#endif
        cacheDir = Path.Combine(FileSystem.CacheDirectory, "faces");
        Directory.CreateDirectory(cacheDir);
    }

    public string GetFaceThumbnailPath(string mediaPath, int faceIndex, int size, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".jpg";

        if (!extension.StartsWith(".", StringComparison.Ordinal))
            extension = "." + extension;

        var safe = MakeSafeFileName($"{mediaPath}|{faceIndex}|{size}|{extension}");
        return Path.Combine(cacheDir, safe + extension.ToLowerInvariant());
    }

    public async Task<string?> EnsureFaceThumbnailAsync(
        string mediaPath,
        FaceEmbedding embedding,
        int size,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return null;

        // This overload regenerates from the stored box only, so it is square but not circular.
        var path = GetFaceThumbnailPath(mediaPath, embedding.FaceIndex, size, ".jpg");

        var hasNormalizedBox = embedding.X <= 1f && embedding.Y <= 1f && embedding.W <= 1f && embedding.H <= 1f;
        var isMissingImageSize = embedding.ImageWidth <= 0 || embedding.ImageHeight <= 0;
        var shouldRegenerate = hasNormalizedBox && isMissingImageSize;

        if (File.Exists(path) && !shouldRegenerate)
            return path;

        var stream = await TryOpenImageStreamAsync(mediaPath, ct).ConfigureAwait(false);
        if (stream == null)
            return null;

        var tmpPath = GetTempPath(path);

        try
        {
            return await Task.Run(() =>
            {
                using var input = stream;
                ct.ThrowIfCancellationRequested();

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                using var image = ImageSharpImage.Load<Rgba32>(input);
                image.Mutate(ctx => ctx.AutoOrient());

                var crop = BuildCropRect(image.Width, image.Height, embedding);
                if (crop.Width <= 1 || crop.Height <= 1)
                    return null;

                using var clone = image.Clone(ctx =>
                {
                    ctx.Crop(crop);
                    ctx.Resize(new ResizeOptions
                    {
                        Mode = ImageSharpResizeMode.Crop,
                        Size = new ImageSharpSize(size, size)
                    });
                });

                ct.ThrowIfCancellationRequested();

                var encoder = new JpegEncoder { Quality = 85 };

                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    clone.Save(fs, encoder);

                if (!IsUsableThumbFile(tmpPath))
                    return null;

                File.Move(tmpPath, path, true);
                return path;

            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.EnsureFaceThumbnailAsync", $"Path={mediaPath}");
            return null;
        }
        finally
        {
            if (!string.Equals(tmpPath, path, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(tmpPath);
        }
    }

    public async Task<string?> EnsureFaceThumbnailAsync(
        Image<Rgba32> image,
        string mediaPath,
        DetectedFace face,
        int faceIndex,
        int size,
        CancellationToken ct)
    {
        if (image == null || string.IsNullOrWhiteSpace(mediaPath))
            return null;

        // Circular thumbnails require alpha, so we store them as PNG.
        var path = GetFaceThumbnailPath(mediaPath, faceIndex, size, ".png");
        if (File.Exists(path))
            return path;

        var tmpPath = GetTempPath(path);

        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                var crop = BuildCropRect(image.Width, image.Height, face);
                if (crop.Width <= 1 || crop.Height <= 1)
                    return null;

                using var clone = image.Clone(ctx =>
                {
                    ctx.Crop(crop);
                    ctx.Resize(new ResizeOptions
                    {
                        Mode = ImageSharpResizeMode.Crop,
                        Size = new ImageSharpSize(size, size)
                    });
                });

                ct.ThrowIfCancellationRequested();

                ApplyCircularAlphaMaskInPlace(clone);

                var encoder = new PngEncoder();

                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    clone.Save(fs, encoder);

                if (!IsUsableThumbFile(tmpPath))
                    return null;

                File.Move(tmpPath, path, true);
                return path;

            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.EnsureFaceThumbnailAsync(DetectedFace)", $"Path={mediaPath}");
            return null;
        }
        finally
        {
            if (!string.Equals(tmpPath, path, StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(tmpPath);
        }
    }

    /// <summary>
    /// Makes the image circular by setting alpha to 0 outside the circle.
    /// Expects a square image.
    /// </summary>
    private static void ApplyCircularAlphaMaskInPlace(Image<Rgba32> img)
    {
        var w = img.Width;
        var h = img.Height;

        var cx = (w - 1) * 0.5f;
        var cy = (h - 1) * 0.5f;
        var r = MathF.Min(w, h) * 0.5f;
        var r2 = r * r;

        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                var dy = y - cy;

                for (var x = 0; x < w; x++)
                {
                    var dx = x - cx;
                    var d2 = dx * dx + dy * dy;

                    if (d2 > r2)
                    {
                        var p = row[x];
                        p.A = 0;
                        row[x] = p;
                    }
                }
            }
        });
    }

#if WINDOWS
    private async Task<IReadOnlyList<MediaSource>> GetSourcesAsync()
    {
        if (cachedSources.Count > 0 && DateTimeOffset.UtcNow - cachedSourcesAt < TimeSpan.FromMinutes(2))
            return cachedSources;

        await sourcesGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (cachedSources.Count > 0 && DateTimeOffset.UtcNow - cachedSourcesAt < TimeSpan.FromMinutes(2))
                return cachedSources;

            cachedSources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
            cachedSourcesAt = DateTimeOffset.UtcNow;
            return cachedSources;
        }
        finally
        {
            sourcesGate.Release();
        }
    }

    private async Task<StorageFile?> GetStorageFileAsync(string mediaPath)
    {
        try
        {
            return await StorageFile.GetFileFromPathAsync(mediaPath);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.GetStorageFileAsync", $"Path={mediaPath}");
        }

        var sources = await GetSourcesAsync().ConfigureAwait(false);
        var best = sources
            .Where(src => !string.IsNullOrWhiteSpace(src.AccessToken))
            .Where(src => !string.IsNullOrWhiteSpace(src.LocalFolderPath))
            .OrderByDescending(src => src.LocalFolderPath.Length)
            .FirstOrDefault(src =>
                mediaPath.StartsWith(src.LocalFolderPath, StringComparison.OrdinalIgnoreCase));

        if (best == null || string.IsNullOrWhiteSpace(best.AccessToken))
            return null;

        try
        {
            var folder = await StorageApplicationPermissions
                .FutureAccessList
                .GetFolderAsync(best.AccessToken);

            if (string.IsNullOrWhiteSpace(folder.Path))
                return null;

            var relativePath = GetRelativePath(mediaPath, folder.Path);
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            return await GetFileFromFolderAsync(folder, relativePath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.GetStorageFileAsync(Fallback)", $"Path={mediaPath}");
            return null;
        }
    }

    private static string? GetRelativePath(string mediaPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath) || string.IsNullOrWhiteSpace(rootPath))
            return null;

        if (!mediaPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return null;

        return mediaPath[rootPath.Length..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
#endif

    private async Task<Stream?> TryOpenImageStreamAsync(string mediaPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (File.Exists(mediaPath))
            return new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

#if ANDROID && !WINDOWS
        if (mediaPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var resolver = Platform.AppContext?.ContentResolver;
                if (resolver == null)
                    return null;

                var uri = Uri.Parse(mediaPath);
                return resolver.OpenInputStream(uri);
            }
            catch
            {
                return null;
            }
        }
#endif

#if WINDOWS
        var file = await GetStorageFileAsync(mediaPath).ConfigureAwait(false);
        if (file != null)
            return await file.OpenStreamForReadAsync().ConfigureAwait(false);
#endif

        return null;
    }

    private static ImageSharpRectangle BuildCropRect(int imageWidth, int imageHeight, FaceEmbedding embedding)
    {
        var x = embedding.X;
        var y = embedding.Y;
        var w = embedding.W;
        var h = embedding.H;

        if (x <= 1 && y <= 1 && w <= 1 && h <= 1)
        {
            var scaleW = embedding.ImageWidth > 0 ? embedding.ImageWidth : imageWidth;
            var scaleH = embedding.ImageHeight > 0 ? embedding.ImageHeight : imageHeight;

            x *= scaleW;
            y *= scaleH;
            w *= scaleW;
            h *= scaleH;
        }

        return BuildCropRect(imageWidth, imageHeight, x, y, w, h);
    }

    private static ImageSharpRectangle BuildCropRect(int imageWidth, int imageHeight, DetectedFace face)
    {
        return BuildCropRect(imageWidth, imageHeight, face.X, face.Y, face.W, face.H);
    }

    private static ImageSharpRectangle BuildCropRect(int imageWidth, int imageHeight, float x, float y, float w, float h)
    {
        x = MathF.Max(0, x);
        y = MathF.Max(0, y);
        w = MathF.Max(0, w);
        h = MathF.Max(0, h);

        if (w <= 1 || h <= 1)
        {
            var size = Math.Min(imageWidth, imageHeight);
            var cx = (imageWidth - size) / 2;
            var cy = (imageHeight - size) / 2;
            return new ImageSharpRectangle(cx, cy, size, size);
        }

        var pad = 0.12f * MathF.Max(w, h);
        var px = MathF.Max(0, x - pad);
        var py = MathF.Max(0, y - pad);
        var pr = MathF.Min(imageWidth, x + w + pad);
        var pb = MathF.Min(imageHeight, y + h + pad);

        var cw = MathF.Max(1, pr - px);
        var ch = MathF.Max(1, pb - py);

        var side = MathF.Max(cw, ch);
        var centerX = px + cw / 2f;
        var centerY = py + ch / 2f - 0.1f * h;

        var left = MathF.Max(0, centerX - side / 2f);
        var top = MathF.Max(0, centerY - side / 2f);

        if (left + side > imageWidth)
            left = MathF.Max(0, imageWidth - side);

        if (top + side > imageHeight)
            top = MathF.Max(0, imageHeight - side);

        var finalW = (int)MathF.Round(MathF.Min(imageWidth - left, side));
        var finalH = (int)MathF.Round(MathF.Min(imageHeight - top, side));

        return new ImageSharpRectangle((int)MathF.Round(left), (int)MathF.Round(top), finalW, finalH);
    }

    private static string MakeSafeFileName(string input)
    {
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

    private static string GetTempPath(string finalPath)
    {
        return finalPath + ".tmp_" + Guid.NewGuid().ToString("N");
    }

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

            var ext = Path.GetExtension(path).ToLowerInvariant();

            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (ext is ".jpg" or ".jpeg")
            {
                var b1 = fs.ReadByte();
                var b2 = fs.ReadByte();
                if (b1 != 0xFF || b2 != 0xD8)
                {
                    TryDeleteFile(path);
                    return false;
                }

                fs.Seek(-2, SeekOrigin.End);
                var e1 = fs.ReadByte();
                var e2 = fs.ReadByte();
                if (e1 != 0xFF || e2 != 0xD9)
                {
                    TryDeleteFile(path);
                    return false;
                }

                return true;
            }

            if (ext == ".png")
            {
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

            TryDeleteFile(path);
            return false;
        }
        catch (IOException ex) when (IsFileInUse(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.IsUsableThumbFile", $"Path={path}");
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
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
                ErrorLog.LogException(ex, "FaceThumbnailService.TryDeleteFile", $"Path={path}");
                return;
            }
        }
    }

    private static bool IsFileInUse(IOException ex)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);
        return ex.HResult == sharingViolation || ex.HResult == lockViolation;
    }
}
