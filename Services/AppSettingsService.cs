using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppSettingsService
{
    private const string ActiveSourceKey = "active_source_id";
    private const string SelectedSortKey = "selected_sort_key";
    private const string SearchTextKey = "search_text";
    private const string DateFilterEnabledKey = "date_filter_enabled";
    private const string DateFilterFromKey = "date_filter_from";
    private const string DateFilterToKey = "date_filter_to";
    private const string NeedsReindexKey = "needs_reindex";
    private const string ThemePreferenceKey = "theme_preference";
    private const string InternalPlayerEnabledKey = "internal_player_enabled";
    private const string IndexedMediaTypesKey = "indexed_media_types";
    private const string VisibleMediaTypesKey = "visible_media_types";

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

    public bool DateFilterEnabled
    {
        get => Preferences.Default.Get(DateFilterEnabledKey, false);
        set => Preferences.Default.Set(DateFilterEnabledKey, value);
    }

    public DateTime DateFilterFrom
    {
        get
        {
            var fallback = DateTime.Today.AddMonths(-1).ToBinary();
            return DateTime.FromBinary(Preferences.Default.Get(DateFilterFromKey, fallback));
        }
        set => Preferences.Default.Set(DateFilterFromKey, value.Date.ToBinary());
    }

    public DateTime DateFilterTo
    {
        get
        {
            var fallback = DateTime.Today.ToBinary();
            return DateTime.FromBinary(Preferences.Default.Get(DateFilterToKey, fallback));
        }
        set => Preferences.Default.Set(DateFilterToKey, value.Date.ToBinary());
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

    public bool InternalPlayerEnabled
    {
        get => Preferences.Default.Get(InternalPlayerEnabledKey, false);
        set => Preferences.Default.Set(InternalPlayerEnabledKey, value);
    }

    public MediaType IndexedMediaTypes
    {
        get => (MediaType)Preferences.Default.Get(IndexedMediaTypesKey, (int)MediaType.All);
        set => Preferences.Default.Set(IndexedMediaTypesKey, (int)value);
    }

    public MediaType VisibleMediaTypes
    {
        get => (MediaType)Preferences.Default.Get(VisibleMediaTypesKey, (int)MediaType.All);
        set => Preferences.Default.Set(VisibleMediaTypesKey, (int)value);
    }
}
