using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace UltimateVideoBrowser.Services;

public enum ImageEditOperation
{
    RotateLeft,
    RotateRight,
    MirrorHorizontal
}

public sealed class ImageEditService
{
    private const int JpegQuality = 92;

    public Task<bool> TryApplyAsync(string path, ImageEditOperation operation, CancellationToken ct)
    {
        return Task.Run(() => TryApply(path, operation, ct), ct);
    }

    private static bool TryApply(string path, ImageEditOperation operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!File.Exists(path))
            return false;

        try
        {
            ct.ThrowIfCancellationRequested();

            IImageFormat format;
            using var image = ImageSharpImage.Load(path);
            format = image.Metadata.DecodedImageFormat!;

            // Normalize EXIF orientation to avoid double rotations.
            image.Mutate(ctx => ctx.AutoOrient());

            switch (operation)
            {
                case ImageEditOperation.RotateLeft:
                    image.Mutate(ctx => ctx.Rotate(-90));
                    break;
                case ImageEditOperation.RotateRight:
                    image.Mutate(ctx => ctx.Rotate(90));
                    break;
                case ImageEditOperation.MirrorHorizontal:
                    image.Mutate(ctx => ctx.Flip(FlipMode.Horizontal));
                    break;
                default:
                    return false;
            }

            // Ensure EXIF orientation is reset to normal.
            image.Metadata.ExifProfile ??= new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)1);

            var tmpPath = path + ".tmp";
            try
            {
                if (format is JpegFormat)
                {
                    var encoder = new JpegEncoder { Quality = JpegQuality };
                    image.Save(tmpPath, encoder);
                }
                else
                {
                    image.Save(tmpPath);
                }

                File.Move(tmpPath, path, true);
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                }
                catch
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}