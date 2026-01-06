using System.Globalization;
using Uri = System.Uri;
#if ANDROID && !WINDOWS
using Android.Content;
using Android.Net;
using Microsoft.Maui.ApplicationModel;
#endif

namespace UltimateVideoBrowser.Converters;

public sealed class StringNullOrEmptyToFallbackConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var raw = value as string;
        var fallback = (parameter as string) ?? "video_placeholder.svg";

        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var text = raw.Trim();

        // 1) Android content:// URIs (scoped storage). Use a stream so MAUI can render it.
#if ANDROID && !WINDOWS
        if (text.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ctx = Platform.AppContext;
                if (ctx != null)
                {
                    var uri = Android.Net.Uri.Parse(text);
                    return ImageSource.FromStream(() => ctx.ContentResolver?.OpenInputStream(uri) ?? Stream.Null);
                }
            }
            catch
            {
                // Best-effort.
            }

            return fallback;
        }
#endif

        // 2) file:// URIs -> local file path (works across platforms).
        if (Uri.TryCreate(text, UriKind.Absolute, out var u) && u.IsFile)
        {
            var local = u.LocalPath;
            if (!string.IsNullOrWhiteSpace(local))
                text = local;
        }

        // 3) Regular file path.
        if (File.Exists(text))
            return text;

        return fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value ?? string.Empty;
    }

    // NOTE: We intentionally do not validate image bytes here.
    // Thumbnails are written to a temp file and moved into place atomically.
    // Any stricter validation can cause false negatives and empty tiles.
}