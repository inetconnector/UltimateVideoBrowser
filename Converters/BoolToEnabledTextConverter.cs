using System.Globalization;

namespace UltimateVideoBrowser.Converters;

public sealed class BoolToEnabledTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? "Enabled" : "Disabled";

        return "Disabled";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Button text isn't used for back conversion.
        return false;
    }
}
