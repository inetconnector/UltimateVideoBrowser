using System.Globalization;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using UltimateVideoBrowser.Models;
using ImageSharpImage = SixLabors.ImageSharp.Image;
#if ANDROID && !WINDOWS
using Android.Media;
using Uri = Android.Net.Uri;
#endif

#if WINDOWS
using Windows.Storage;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class LocationMetadataService
{
    private readonly AppSettingsService settingsService;

    public LocationMetadataService(AppSettingsService settingsService)
    {
        this.settingsService = settingsService;
    }

    public bool IsEnabled => settingsService.LocationsEnabled;

    public async Task<bool> TryPopulateLocationAsync(MediaItem item, CancellationToken ct)
    {
        if (!settingsService.LocationsEnabled)
            return false;

        if (item.MediaType is not (MediaType.Photos or MediaType.Graphics or MediaType.Videos))
            return false;

        if (item.Latitude.HasValue && item.Longitude.HasValue)
            return true;

        var location = await TryGetLocationAsync(item, ct).ConfigureAwait(false);
        if (location == null)
            return false;

        item.Latitude = location.Value.Latitude;
        item.Longitude = location.Value.Longitude;
        return true;
    }

    private async Task<GeoLocation?> TryGetLocationAsync(MediaItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = item.Path;
        if (string.IsNullOrWhiteSpace(path))
            return null;

#if ANDROID && !WINDOWS
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return await TryGetLocationFromAndroidContentAsync(path, ct).ConfigureAwait(false);
#endif

        if (item.MediaType is MediaType.Photos or MediaType.Graphics)
        {
            var location = await TryGetLocationFromImageAsync(path, ct).ConfigureAwait(false);
#if WINDOWS
            location ??= await TryGetLocationFromWindowsAsync(path).ConfigureAwait(false);
#endif
            return location;
        }

#if ANDROID && !WINDOWS
        if (item.MediaType == MediaType.Videos)
            return await TryGetLocationFromAndroidFileAsync(path, ct).ConfigureAwait(false);
#elif WINDOWS
        if (item.MediaType == MediaType.Videos)
            return await TryGetLocationFromWindowsAsync(path).ConfigureAwait(false);
#endif

        return null;
    }

    private static async Task<GeoLocation?> TryGetLocationFromImageAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            var info = await ImageSharpImage.IdentifyAsync(stream, ct).ConfigureAwait(false);
            return TryGetLocationFromExifProfile(info?.Metadata.ExifProfile);
        }
        catch
        {
            return null;
        }
    }

#if ANDROID && !WINDOWS
    private static async Task<GeoLocation?> TryGetLocationFromAndroidContentAsync(string contentUri,
        CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var resolver = Platform.AppContext?.ContentResolver;
            if (resolver == null)
                return null;

            var uri = Uri.Parse(contentUri);
            await using var stream = resolver.OpenInputStream(uri);
            if (stream == null)
                return null;

            var exif = new ExifInterface(stream);
            return TryGetLocationFromExifInterface(exif);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GeoLocation?> TryGetLocationFromAndroidFileAsync(string path, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(path);
            var exif = new ExifInterface(stream);
            return TryGetLocationFromExifInterface(exif);
        }
        catch
        {
            return null;
        }
    }

    private static GeoLocation? TryGetLocationFromExifInterface(ExifInterface exif)
    {
        try
        {
            var latLong = new float[2];
            if (!exif.GetLatLong(latLong))
                return null;

            return new GeoLocation(latLong[0], latLong[1]);
        }
        catch
        {
            return null;
        }
    }
#endif

#if WINDOWS
    private static async Task<GeoLocation?> TryGetLocationFromWindowsAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var props = await file.Properties.RetrievePropertiesAsync(new[]
            {
                "System.GPS.Latitude",
                "System.GPS.Longitude"
            });

            if (!TryGetGpsValue(props, "System.GPS.Latitude", out var lat))
                return null;
            if (!TryGetGpsValue(props, "System.GPS.Longitude", out var lon))
                return null;

            return new GeoLocation(lat, lon);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetGpsValue(IDictionary<string, object> props, string key, out double value)
    {
        value = 0;
        if (!props.TryGetValue(key, out var raw) || raw == null)
            return false;

        var parsed = raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            uint ui => ui,
            double[] arr when arr.Length > 0 => arr[0],
            float[] arr when arr.Length > 0 => arr[0],
            int[] arr when arr.Length > 0 => arr[0],
            long[] arr when arr.Length > 0 => arr[0],
            string s when double.TryParse(s, out var parsedValue) => parsedValue,
            _ => (double?)null
        };

        if (parsed == null)
            return false;

        value = parsed.Value;
        return true;
    }
#endif

    private static GeoLocation? TryGetLocationFromExifProfile(ExifProfile? profile)
    {
        if (profile == null)
            return null;

        if (!profile.TryGetValue(ExifTag.GPSLatitude, out var latValue) ||
            !TryConvertGpsCoordinate(latValue.Value, out var lat))
            return null;
        if (!profile.TryGetValue(ExifTag.GPSLongitude, out var lonValue) ||
            !TryConvertGpsCoordinate(lonValue.Value, out var lon))
            return null;

        profile.TryGetValue(ExifTag.GPSLatitudeRef, out var latRefValue);
        profile.TryGetValue(ExifTag.GPSLongitudeRef, out var lonRefValue);
        var latRef = latRefValue?.Value;
        var lonRef = lonRefValue?.Value;
        if (string.Equals(latRef, "S", StringComparison.OrdinalIgnoreCase))
            lat = -lat;
        if (string.Equals(lonRef, "W", StringComparison.OrdinalIgnoreCase))
            lon = -lon;

        return new GeoLocation(lat, lon);
    }

    private static bool TryConvertGpsCoordinate(object? value, out double result)
    {
        result = 0;
        switch (value)
        {
            case Rational[] rationals:
                result = ConvertRationalsToDegrees(rationals);
                return true;
            case SignedRational[] signedRationals:
                result = ConvertSignedRationalsToDegrees(signedRationals);
                return true;
            case double[] doubles:
                result = ConvertDoubleArrayToDegrees(doubles);
                return true;
            case float[] floats:
                result = ConvertDoubleArrayToDegrees(floats.Select(v => (double)v).ToArray());
                return true;
            case int[] ints:
                result = ConvertDoubleArrayToDegrees(ints.Select(v => (double)v).ToArray());
                return true;
            case long[] longs:
                result = ConvertDoubleArrayToDegrees(longs.Select(v => (double)v).ToArray());
                return true;
            case Rational rational:
                result = ToDouble(rational);
                return true;
            case SignedRational signedRational:
                result = ToDouble(signedRational);
                return true;
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case string s:
                return TryParseGpsString(s, out result);
            default:
                return false;
        }
    }

    private static double ConvertRationalsToDegrees(IReadOnlyList<Rational> values)
    {
        if (values.Count == 0)
            return 0;

        var deg = ToDouble(values[0]);
        var min = values.Count > 1 ? ToDouble(values[1]) : 0;
        var sec = values.Count > 2 ? ToDouble(values[2]) : 0;
        return deg + min / 60.0 + sec / 3600.0;
    }

    private static double ConvertSignedRationalsToDegrees(IReadOnlyList<SignedRational> values)
    {
        if (values.Count == 0)
            return 0;

        var deg = ToDouble(values[0]);
        var min = values.Count > 1 ? ToDouble(values[1]) : 0;
        var sec = values.Count > 2 ? ToDouble(values[2]) : 0;
        return deg + min / 60.0 + sec / 3600.0;
    }

    private static double ConvertDoubleArrayToDegrees(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;

        if (values.Count == 1)
            return values[0];

        var deg = values[0];
        var min = values.Count > 1 ? values[1] : 0;
        var sec = values.Count > 2 ? values[2] : 0;
        return deg + min / 60.0 + sec / 3600.0;
    }

    private static bool TryParseGpsString(string value, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            result = parsed;
            return true;
        }

        var tokens = trimmed
            .Replace("°", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("'", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("\"", " ", StringComparison.OrdinalIgnoreCase)
            .Replace(",", " ", StringComparison.OrdinalIgnoreCase)
            .Replace(":", " ", StringComparison.OrdinalIgnoreCase)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return false;

        var parts = new List<double>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!TryParseRationalToken(token, out var part))
                return false;
            parts.Add(part);
        }

        result = ConvertDoubleArrayToDegrees(parts);
        return true;
    }

    private static bool TryParseRationalToken(string token, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var split = token.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length == 2)
        {
            if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator))
                return false;
            if (!double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
                Math.Abs(denominator) < double.Epsilon)
                return false;
            result = numerator / denominator;
            return true;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static double ToDouble(Rational rational)
    {
        return rational.Denominator == 0 ? 0 : rational.Numerator / (double)rational.Denominator;
    }

    private static double ToDouble(SignedRational rational)
    {
        return rational.Denominator == 0 ? 0 : rational.Numerator / (double)rational.Denominator;
    }

    private readonly record struct GeoLocation(double Latitude, double Longitude);
}
