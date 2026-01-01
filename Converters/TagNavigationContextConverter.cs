using System.Globalization;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Converters;

public sealed class TagNavigationContextConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return null;

        var tagName = values[0]?.ToString() ?? string.Empty;
        var mediaItem = values[1] as MediaItem;

        return new TagNavigationContext(tagName, mediaItem);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
