using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using ImageSharpRectangle = SixLabors.ImageSharp.Rectangle;
using ImageSharpSize = SixLabors.ImageSharp.Size;
using ImageSharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;

namespace UltimateVideoBrowser.Services;

public sealed class FaceThumbnailService
{
    private readonly string cacheDir;

    public FaceThumbnailService()
    {
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

        if (!File.Exists(mediaPath))
            return null;

        var tmpPath = GetTempPath(path);
        try
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                // Ensure target folder exists
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                using var image = ImageSharpImage.Load<Rgba32>(mediaPath);
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

            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.IsUsableThumbFile", $"Path={path}");
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "FaceThumbnailService.TryDeleteFile", $"Path={path}");
        }
    }
}
