namespace UltimateVideoBrowser.Services;

public enum UiMode
{
    Phone,
    Tablet,
    Tv
}

public sealed class DeviceModeService
{
    public UiMode GetUiMode()
    {
        // Conservative heuristic:
        // - TV: no touch + large screen often; we expose focus visuals regardless.
        // - Tablet: larger idiom or size.
        // MAUI doesn't directly expose Android TV detection in a portable way,
        // so we use device idiom + size as baseline and let focus visuals handle TV input.
        var idiom = DeviceInfo.Idiom;
        if (idiom == DeviceIdiom.TV)
            return UiMode.Tv;

        if (idiom == DeviceIdiom.Tablet || idiom == DeviceIdiom.Desktop)
            return UiMode.Tablet;

        // Fallback based on width at runtime can be done in UI layer.
        return UiMode.Phone;
    }
}
