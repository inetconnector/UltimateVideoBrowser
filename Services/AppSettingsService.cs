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
    private const string ProUnlockedKey = "pro_unlocked";
    private const string ProActivationTokenKey = "pro_activation_token";
    private const string ProActivationValidUntilKey = "pro_activation_valid_until";
    private const string LicenseServerBaseUrlKey = "license_server_base_url";

    private static readonly string[] DefaultVideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".m4v" };

    private static readonly string[] DefaultPhotoExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic" };

    private static readonly string[] DefaultDocumentExtensions =
        { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" };

    private readonly FileSettingsStore store;
    private bool isIndexing;

    public AppSettingsService(FileSettingsStore store)
    {
        this.store = store;
    }

    public string ActiveSourceId
    {
        get => store.GetString(ActiveSourceKey, "");
        set => store.SetString(ActiveSourceKey, value ?? "");
    }

    public string ActiveAlbumId
    {
        get => store.GetString(ActiveAlbumKey, "");
        set => store.SetString(ActiveAlbumKey, value ?? "");
    }

    public string SelectedSortOptionKey
    {
        get => store.GetString(SelectedSortKey, "name");
        set => store.SetString(SelectedSortKey, value ?? "name");
    }

    public string SearchText
    {
        get => store.GetString(SearchTextKey, "");
        set => store.SetString(SearchTextKey, value ?? "");
    }

    public SearchScope SearchScope
    {
        get => (SearchScope)store.GetInt(SearchScopeKey, (int)SearchScope.All);
        set => store.SetInt(SearchScopeKey, (int)value);
    }

    public bool DateFilterEnabled
    {
        get => store.GetBool(DateFilterEnabledKey, false);
        set => store.SetBool(DateFilterEnabledKey, value);
    }

    public DateTime DateFilterFrom
    {
        get
        {
            var fallback = DateTime.Today.AddMonths(-1).ToBinary();
            return DateTime.FromBinary(store.GetLong(DateFilterFromKey, fallback));
        }
        set => store.SetLong(DateFilterFromKey, value.Date.ToBinary());
    }

    public DateTime DateFilterTo
    {
        get
        {
            var fallback = DateTime.Today.ToBinary();
            return DateTime.FromBinary(store.GetLong(DateFilterToKey, fallback));
        }
        set => store.SetLong(DateFilterToKey, value.Date.ToBinary());
    }

    public bool NeedsReindex
    {
        get => store.GetBool(NeedsReindexKey, true);
        set
        {
            var current = store.GetBool(NeedsReindexKey, true);
            if (current == value)
            {
                if (value)
                    NeedsReindexChanged?.Invoke(this, value);
                return;
            }

            store.SetBool(NeedsReindexKey, value);
            NeedsReindexChanged?.Invoke(this, value);
        }
    }

    public string ThemePreference
    {
        get => store.GetString(ThemePreferenceKey, "dark");
        set => store.SetString(ThemePreferenceKey, value ?? "dark");
    }

    public bool InternalPlayerEnabled
    {
        get => store.GetBool(InternalPlayerEnabledKey, false);
        set => store.SetBool(InternalPlayerEnabledKey, value);
    }

    public MediaType IndexedMediaTypes
    {
        get => (MediaType)store.GetInt(IndexedMediaTypesKey, (int)MediaType.All);
        set => store.SetInt(IndexedMediaTypesKey, (int)value);
    }

    public MediaType VisibleMediaTypes
    {
        get => (MediaType)store.GetInt(VisibleMediaTypesKey, (int)MediaType.All);
        set => store.SetInt(VisibleMediaTypesKey, (int)value);
    }

    public string VideoExtensions
    {
        get => store.GetString(VideoExtensionsKey, string.Join(", ", DefaultVideoExtensions));
        set => store.SetString(VideoExtensionsKey, value ?? string.Empty);
    }

    public string PhotoExtensions
    {
        get => store.GetString(PhotoExtensionsKey, string.Join(", ", DefaultPhotoExtensions));
        set => store.SetString(PhotoExtensionsKey, value ?? string.Empty);
    }

    public string DocumentExtensions
    {
        get => store.GetString(DocumentExtensionsKey, string.Join(", ", DefaultDocumentExtensions));
        set => store.SetString(DocumentExtensionsKey, value ?? string.Empty);
    }

    public bool AllowFileChanges
    {
        get => store.GetBool(AllowFileChangesKey, false);
        set => store.SetBool(AllowFileChangesKey, value);
    }

    public bool PeopleTaggingEnabled
    {
        get => store.GetBool(PeopleTaggingEnabledKey, false);
        set => store.SetBool(PeopleTaggingEnabledKey, value);
    }

    public bool LocationsEnabled
    {
        get => store.GetBool(LocationsEnabledKey, false);
        set => store.SetBool(LocationsEnabledKey, value);
    }

    public bool IsProUnlocked
    {
        get => store.GetBool(ProUnlockedKey, false);
        set => store.SetBool(ProUnlockedKey, value);
    }

    public string ProActivationToken
    {
        get => store.GetString(ProActivationTokenKey, string.Empty);
        set => store.SetString(ProActivationTokenKey, value ?? string.Empty);
    }

    public DateTimeOffset? ProActivationValidUntil
    {
        get
        {
            var seconds = store.GetLong(ProActivationValidUntilKey, 0L);
            return seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        set
        {
            store.SetLong(ProActivationValidUntilKey, value?.ToUnixTimeSeconds() ?? 0L);
        }
    }

    public string LicenseServerBaseUrl
    {
        get => store.GetString(LicenseServerBaseUrlKey, "https://license.netregservice.com");
        set => store.SetString(LicenseServerBaseUrlKey, value ?? string.Empty);
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
