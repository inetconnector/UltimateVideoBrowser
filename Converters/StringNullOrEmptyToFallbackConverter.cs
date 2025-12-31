using System.Globalization;

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
            {
                try
                {
                    // Treat 0-byte or obviously broken files as invalid to avoid "blank" thumbnails.
                    var fi = new FileInfo(text);
                    if (fi.Length > 0)
                        return ImageSource.FromFile(text);
                }
                catch
                {
                    // Ignore and fall back.
                }
            }
        }

        var fallback = parameter as string ?? string.Empty;
        return string.IsNullOrWhiteSpace(fallback)
            ? ImageSource.FromFile(string.Empty)
            : ImageSource.FromFile(fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}