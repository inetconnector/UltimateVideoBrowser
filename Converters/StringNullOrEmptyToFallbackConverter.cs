using System.Globalization;
using System.IO;

namespace UltimateVideoBrowser.Converters;

public sealed class StringNullOrEmptyToFallbackConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (text.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                return ImageSource.FromUri(new Uri(text));

            if (File.Exists(text))
                try
                {
                    // Treat 0-byte or obviously broken files as invalid to avoid "blank" thumbnails.
                    if (IsUsableImageFile(text))
                        return ImageSource.FromFile(text);
                }
                catch
                {
                    // Ignore and fall back.
                }
        }

        var fallback = parameter as string ?? string.Empty;
        return ImageSource.FromFile(string.IsNullOrWhiteSpace(fallback) ? "video_placeholder.svg" : fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }

    private static bool IsUsableImageFile(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Length < 128)
            return false;

        if (!path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return true;

        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var b1 = fs.ReadByte();
        var b2 = fs.ReadByte();
        if (b1 != 0xFF || b2 != 0xD8)
            return false;

        if (fs.Length >= 2)
        {
            fs.Seek(-2, SeekOrigin.End);
            var e1 = fs.ReadByte();
            var e2 = fs.ReadByte();
            if (e1 != 0xFF || e2 != 0xD9)
                return false;
        }

        return true;
    }
}
