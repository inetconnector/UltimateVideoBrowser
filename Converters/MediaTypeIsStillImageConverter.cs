using System;
using System.Globalization;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Converters;

public sealed class MediaTypeIsStillImageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MediaType mediaType)
            return false;

        return mediaType is MediaType.Photos or MediaType.Graphics;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
