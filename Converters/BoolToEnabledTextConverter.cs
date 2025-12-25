using System.Globalization;
using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Converters;

public sealed class BoolToEnabledTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? AppResources.Enabled : AppResources.Disabled;

        return AppResources.Disabled;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Button text isn't used for back conversion.
        return false;
    }
}