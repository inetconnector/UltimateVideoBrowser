using System.Globalization;

namespace UltimateVideoBrowser.Converters;

public sealed class StringNullOrEmptyToFallbackConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (!string.IsNullOrWhiteSpace(text))
            if (text.StartsWith("content://", StringComparison.OrdinalIgnoreCase) || File.Exists(text))
                return text;

        return parameter as string ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}