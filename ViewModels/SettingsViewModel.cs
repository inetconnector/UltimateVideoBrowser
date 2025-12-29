using CommunityToolkit.Mvvm.ComponentModel;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService settingsService;
    [ObservableProperty] private DateTime dateFilterFrom;
    [ObservableProperty] private DateTime dateFilterTo;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool needsReindex;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private bool isVideosIndexed;
    [ObservableProperty] private bool isPhotosIndexed;
    [ObservableProperty] private bool isDocumentsIndexed;
    [ObservableProperty] private string videoExtensionsText = string.Empty;
    [ObservableProperty] private string photoExtensionsText = string.Empty;
    [ObservableProperty] private string documentExtensionsText = string.Empty;

    [ObservableProperty] private ThemeOption? selectedTheme;

    public SettingsViewModel(AppSettingsService settingsService)
    {
        this.settingsService = settingsService;
        ThemeOptions = new[]
        {
            new ThemeOption("light", AppResources.ThemeLight),
            new ThemeOption("dark", AppResources.ThemeDark)
        };

        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };

        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Key == settingsService.ThemePreference)
                        ?? ThemeOptions.First();
        ApplyTheme(SelectedTheme.Key);

        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Key == settingsService.SelectedSortOptionKey)
                             ?? SortOptions.First();
        IsDateFilterEnabled = settingsService.DateFilterEnabled;
        DateFilterFrom = settingsService.DateFilterFrom;
        DateFilterTo = settingsService.DateFilterTo;
        NeedsReindex = settingsService.NeedsReindex;
        IsInternalPlayerEnabled = settingsService.InternalPlayerEnabled;

        var indexed = settingsService.IndexedMediaTypes;
        IsVideosIndexed = indexed.HasFlag(MediaType.Videos);
        IsPhotosIndexed = indexed.HasFlag(MediaType.Photos);
        IsDocumentsIndexed = indexed.HasFlag(MediaType.Documents);

        VideoExtensionsText = settingsService.VideoExtensions;
        PhotoExtensionsText = settingsService.PhotoExtensions;
        DocumentExtensionsText = settingsService.DocumentExtensions;
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public IReadOnlyList<SortOption> SortOptions { get; }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value == null)
            return;

        settingsService.ThemePreference = value.Key;
        ApplyTheme(value.Key);
    }

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value == null)
            return;

        settingsService.SelectedSortOptionKey = value.Key;
    }

    partial void OnIsDateFilterEnabledChanged(bool value)
    {
        settingsService.DateFilterEnabled = value;
    }

    partial void OnDateFilterFromChanged(DateTime value)
    {
        if (value > DateFilterTo)
            DateFilterTo = value;

        settingsService.DateFilterFrom = value;
    }

    partial void OnDateFilterToChanged(DateTime value)
    {
        if (value < DateFilterFrom)
            DateFilterFrom = value;

        settingsService.DateFilterTo = value;
    }

    partial void OnNeedsReindexChanged(bool value)
    {
        settingsService.NeedsReindex = value;
    }

    partial void OnIsInternalPlayerEnabledChanged(bool value)
    {
        settingsService.InternalPlayerEnabled = value;
    }

    partial void OnIsVideosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsPhotosIndexed, IsDocumentsIndexed, () => IsVideosIndexed = true))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsPhotosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsVideosIndexed, IsDocumentsIndexed, () => IsPhotosIndexed = true))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsDocumentsIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsVideosIndexed, IsPhotosIndexed, () => IsDocumentsIndexed = true))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnVideoExtensionsTextChanged(string value)
    {
        settingsService.VideoExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnPhotoExtensionsTextChanged(string value)
    {
        settingsService.PhotoExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnDocumentExtensionsTextChanged(string value)
    {
        settingsService.DocumentExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    private static void ApplyTheme(string themeKey)
    {
        var theme = themeKey == "light" ? AppTheme.Light : AppTheme.Dark;
        App.ApplyTheme(theme);
    }

    private void ApplyIndexedMediaTypes()
    {
        settingsService.IndexedMediaTypes = BuildIndexedMediaTypes();
        settingsService.NeedsReindex = true;
    }

    private MediaType BuildIndexedMediaTypes()
    {
        var mediaTypes = MediaType.None;
        if (IsVideosIndexed)
            mediaTypes |= MediaType.Videos;
        if (IsPhotosIndexed)
            mediaTypes |= MediaType.Photos;
        if (IsDocumentsIndexed)
            mediaTypes |= MediaType.Documents;
        return mediaTypes;
    }

    private static bool EnsureAtLeastOneIndexed(bool currentValue, bool otherOne, bool otherTwo, Action restore)
    {
        if (!currentValue && !otherOne && !otherTwo)
        {
            restore();
            return false;
        }

        return true;
    }

    public sealed record ThemeOption(string Key, string Display);
}
