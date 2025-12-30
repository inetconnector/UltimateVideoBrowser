using System.Globalization;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Converters;

public sealed class MediaTypeSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        if (values[0] is not MediaType selected || values[1] is not MediaType option)
            return false;

        return selected.HasFlag(option);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}