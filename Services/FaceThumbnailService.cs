using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRectangle = SixLabors.ImageSharp.Rectangle;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
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

    public string GetFaceThumbnailPath(string mediaPath, int faceIndex, int size)
    {
        var safe = MakeSafeFileName($"{mediaPath}|{faceIndex}|{size}");
        return Path.Combine(cacheDir, safe + ".jpg");
    }

    public async Task<string?> EnsureFaceThumbnailAsync(
        string mediaPath,
        FaceEmbedding embedding,
        int size,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            return null;

        var path = GetFaceThumbnailPath(mediaPath, embedding.FaceIndex, size);
        if (File.Exists(path))
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

                // Ensure target folder exists
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

                using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
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
            TryDeleteFile(tmpPath);
        }
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

#if WINDOWS
        var file = await GetStorageFileAsync(mediaPath).ConfigureAwait(false);
        if (file != null)
            return await file.OpenStreamForReadAsync().ConfigureAwait(false);
#endif

        return null;
    }

    private static ImageSharpRectangle BuildCropRect(int imageWidth, int imageHeight, FaceEmbedding embedding)
    {
        var x = MathF.Max(0, embedding.X);
        var y = MathF.Max(0, embedding.Y);
        var w = MathF.Max(0, embedding.W);
        var h = MathF.Max(0, embedding.H);

        if (w <= 1 || h <= 1)
        {
            // Fallback to a centered crop if we don't have a stored bounding box.
            var size = Math.Min(imageWidth, imageHeight);
            var cx = (imageWidth - size) / 2;
            var cy = (imageHeight - size) / 2;
            return new ImageSharpRectangle(cx, cy, size, size);
        }

        // Add a bit of padding around the face for a nicer Picasa-like crop.
        var pad = 0.28f * MathF.Max(w, h);
        var px = MathF.Max(0, x - pad);
        var py = MathF.Max(0, y - pad);
        var pr = MathF.Min(imageWidth, x + w + pad);
        var pb = MathF.Min(imageHeight, y + h + pad);

        var cw = MathF.Max(1, pr - px);
        var ch = MathF.Max(1, pb - py);

        // Prefer a square crop for avatar-style thumbnails.
        var side = MathF.Max(cw, ch);
        var centerX = px + cw / 2f;
        var centerY = py + ch / 2f;

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
        // Hash-like safe filename (FNV-1a 64-bit)
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

            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var b1 = fs.ReadByte();
            var b2 = fs.ReadByte();
            if (b1 != 0xFF || b2 != 0xD8)
            {
                TryDeleteFile(path);
                return false;
            }

            if (fs.Length >= 2)
            {
                fs.Seek(-2, SeekOrigin.End);
                var e1 = fs.ReadByte();
                var e2 = fs.ReadByte();
                if (e1 != 0xFF || e2 != 0xD9)
                {
                    TryDeleteFile(path);
                    return false;
                }
            }

            return true;
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

    private static bool IsFileInUse(IOException ex)
    {
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);
        return ex.HResult == sharingViolation || ex.HResult == lockViolation;
    }
}
