using System.Globalization;

namespace UltimateVideoBrowser.Converters;

public class StringEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        var left = values[0]?.ToString() ?? string.Empty;
        var right = values[1]?.ToString() ?? string.Empty;

        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
