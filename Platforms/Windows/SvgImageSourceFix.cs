using System.IO;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Media.Imaging;

namespace UltimateVideoBrowser.Platforms.Windows;

public static class SvgImageSourceFix
{
    public static void Configure()
    {
        ImageHandler.Mapper.AppendToMapping("SvgImageSourceFix", (handler, view) =>
        {
            if (view.Source is not FileImageSource fileImageSource)
            {
                return;
            }

            var fileName = NormalizeSvgFileName(fileImageSource.File);
            if (fileName is null)
            {
                return;
            }

            _ = ApplySvgSourceAsync(handler, fileName);
        });
    }

    private static string? NormalizeSvgFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var resolvedName = fileName;
        var hasExtension = Path.HasExtension(resolvedName);
        if (!hasExtension && !HasDirectorySeparator(resolvedName))
        {
            resolvedName += ".svg";
            hasExtension = true;
        }

        if (!hasExtension || !resolvedName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resolvedName;
    }

    private static bool HasDirectorySeparator(string path)
    {
        return path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }

    private static async Task ApplySvgSourceAsync(IImageHandler handler, string fileName)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            var svgSource = new SvgImageSource();
            await svgSource.SetSourceAsync(stream.AsRandomAccessStream());
            handler.PlatformView.Source = svgSource;
        }
        catch (Exception)
        {
            // Ignore failures and fall back to default handling.
        }
    }
}
