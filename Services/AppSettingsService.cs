using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class AppSettingsService
{
    private const string ActiveSourceKey = "active_source_id";
    private const string ActiveAlbumKey = "active_album_id";
    private const string SelectedSortKey = "selected_sort_key";
    private const string SearchTextKey = "search_text";
    private const string SearchScopeKey = "search_scope";
    private const string DateFilterEnabledKey = "date_filter_enabled";
    private const string DateFilterFromKey = "date_filter_from";
    private const string DateFilterToKey = "date_filter_to";
    private const string NeedsReindexKey = "needs_reindex";
    private const string ThemePreferenceKey = "theme_preference";
    private const string InternalPlayerEnabledKey = "internal_player_enabled";
    private const string IndexedMediaTypesKey = "indexed_media_types";
    private const string VisibleMediaTypesKey = "visible_media_types";
    private const string VideoExtensionsKey = "video_extensions";
    private const string PhotoExtensionsKey = "photo_extensions";
    private const string DocumentExtensionsKey = "document_extensions";
    private const string AllowFileChangesKey = "allow_file_changes";
    private const string PeopleTaggingEnabledKey = "people_tagging_enabled";
    private const string LocationsEnabledKey = "locations_enabled";
    private bool isIndexing;

    private static readonly string[] DefaultVideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

    private static readonly string[] DefaultPhotoExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic" };

    private static readonly string[] DefaultDocumentExtensions =
        { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" };

    public string ActiveSourceId
    {
        get => Preferences.Default.Get(ActiveSourceKey, "");
        set => Preferences.Default.Set(ActiveSourceKey, value ?? "");
    }

    public string ActiveAlbumId
    {
        get => Preferences.Default.Get(ActiveAlbumKey, "");
        set => Preferences.Default.Set(ActiveAlbumKey, value ?? "");
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

    public SearchScope SearchScope
    {
        get => (SearchScope)Preferences.Default.Get(SearchScopeKey, (int)SearchScope.All);
        set => Preferences.Default.Set(SearchScopeKey, (int)value);
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
        set
        {
            var current = Preferences.Default.Get(NeedsReindexKey, true);
            if (current == value)
            {
                if (value)
                    NeedsReindexChanged?.Invoke(this, value);
                return;
            }

            Preferences.Default.Set(NeedsReindexKey, value);
            NeedsReindexChanged?.Invoke(this, value);
        }
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

    public string VideoExtensions
    {
        get => Preferences.Default.Get(VideoExtensionsKey, string.Join(", ", DefaultVideoExtensions));
        set => Preferences.Default.Set(VideoExtensionsKey, value ?? string.Empty);
    }

    public string PhotoExtensions
    {
        get => Preferences.Default.Get(PhotoExtensionsKey, string.Join(", ", DefaultPhotoExtensions));
        set => Preferences.Default.Set(PhotoExtensionsKey, value ?? string.Empty);
    }

    public string DocumentExtensions
    {
        get => Preferences.Default.Get(DocumentExtensionsKey, string.Join(", ", DefaultDocumentExtensions));
        set => Preferences.Default.Set(DocumentExtensionsKey, value ?? string.Empty);
    }

    public bool AllowFileChanges
    {
        get => Preferences.Default.Get(AllowFileChangesKey, false);
        set => Preferences.Default.Set(AllowFileChangesKey, value);
    }

    public bool PeopleTaggingEnabled
    {
        get => Preferences.Default.Get(PeopleTaggingEnabledKey, false);
        set => Preferences.Default.Set(PeopleTaggingEnabledKey, value);
    }

    public bool LocationsEnabled
    {
        get => Preferences.Default.Get(LocationsEnabledKey, false);
        set => Preferences.Default.Set(LocationsEnabledKey, value);
    }

    public bool IsIndexing
    {
        get => isIndexing;
        set
        {
            if (isIndexing == value)
                return;

            isIndexing = value;
            IsIndexingChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<bool>? NeedsReindexChanged;
    public event EventHandler<bool>? IsIndexingChanged;

    public IReadOnlySet<string> GetVideoExtensions()
    {
        return ParseExtensions(VideoExtensions, DefaultVideoExtensions);
    }

    public IReadOnlySet<string> GetPhotoExtensions()
    {
        return ParseExtensions(PhotoExtensions, DefaultPhotoExtensions);
    }

    public IReadOnlySet<string> GetDocumentExtensions()
    {
        return ParseExtensions(DocumentExtensions, DefaultDocumentExtensions);
    }

    private static HashSet<string> ParseExtensions(string? raw, IEnumerable<string> fallback)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = (raw ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var normalized = token.StartsWith(".", StringComparison.Ordinal)
                ? token
                : $".{token}";
            if (string.Equals(normalized, ".", StringComparison.Ordinal))
                continue;
            extensions.Add(normalized);
        }

        if (extensions.Count == 0)
            foreach (var ext in fallback)
                extensions.Add(ext);

        return extensions;
    }
}
