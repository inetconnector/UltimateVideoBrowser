using System.Globalization;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Converters;

public sealed class MediaTypeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not MediaType mediaType)
            return "";

        return mediaType switch
        {
            MediaType.Photos => AppResources.MediaTypePhotos,
            MediaType.Documents => AppResources.MediaTypeDocuments,
            MediaType.Graphics => AppResources.MediaTypeGraphics,
            _ => AppResources.MediaTypeVideos
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}