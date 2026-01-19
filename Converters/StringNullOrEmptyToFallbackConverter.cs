using System.Globalization;

#if ANDROID
using AndroidUri = Android.Net.Uri;
#endif

namespace UltimateVideoBrowser.Converters;

public sealed class StringNullOrEmptyToFallbackConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string;
        var fallback = parameter as string ?? "video_placeholder.svg";

        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var text = raw.Trim();

#if ANDROID
        if (text.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ctx = Platform.AppContext;
                if (ctx != null)
                {
                    var uri = AndroidUri.Parse(text);
                    return ImageSource.FromStream(() => ctx.ContentResolver?.OpenInputStream(uri) ?? Stream.Null);
                }
            }
            catch
            {
            }

            return fallback;
        }
#endif

        // file:// URIs -> local file path
        if (global::System.Uri.TryCreate(text, global::System.UriKind.Absolute, out var u) && u.IsFile)
        {
            var local = u.LocalPath;
            if (!string.IsNullOrWhiteSpace(local))
                text = local;
        }

        if (File.Exists(text))
            return text;

        return fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }
}