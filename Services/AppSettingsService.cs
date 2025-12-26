using Microsoft.Maui.Storage;

namespace UltimateVideoBrowser.Services;

public sealed class AppSettingsService
{
    private const string ActiveSourceKey = "active_source_id";
    private const string SelectedSortKey = "selected_sort_key";
    private const string SearchTextKey = "search_text";
    private const string NeedsReindexKey = "needs_reindex";
    private const string ThemePreferenceKey = "theme_preference";

    public string ActiveSourceId
    {
        get => Preferences.Default.Get(ActiveSourceKey, "");
        set => Preferences.Default.Set(ActiveSourceKey, value ?? "");
    }

    public string SelectedSortOptionKey
    {
        get => Preferences.Default.Get(SelectedSortKey, "name");
        set => Preferences.Default.Set(SelectedSortKey, value ?? "name");
    }

    public string SearchText
    {
        get => Preferences.Default.Get(SearchTextKey, "");
        set => Preferences.Default.Set(SearchTextKey, value ?? "");
    }

    public bool NeedsReindex
    {
        get => Preferences.Default.Get(NeedsReindexKey, true);
        set => Preferences.Default.Set(NeedsReindexKey, value);
    }

    public string ThemePreference
    {
        get => Preferences.Default.Get(ThemePreferenceKey, "dark");
        set => Preferences.Default.Set(ThemePreferenceKey, value ?? "dark");
    }
}
