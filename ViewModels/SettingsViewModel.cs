using CommunityToolkit.Mvvm.ComponentModel;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService settingsService;

    [ObservableProperty] private ThemeOption? selectedTheme;
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private DateTime dateFilterFrom;
    [ObservableProperty] private DateTime dateFilterTo;
    [ObservableProperty] private bool needsReindex;

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

    private static void ApplyTheme(string themeKey)
    {
        var theme = themeKey == "light" ? AppTheme.Light : AppTheme.Dark;
        App.ApplyTheme(theme);
    }

    public sealed record ThemeOption(string Key, string Display);
}
