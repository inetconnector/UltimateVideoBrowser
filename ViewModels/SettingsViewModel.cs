using CommunityToolkit.Mvvm.ComponentModel;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService settingsService;

    [ObservableProperty] private ThemeOption? selectedTheme;

    public SettingsViewModel(AppSettingsService settingsService)
    {
        this.settingsService = settingsService;
        ThemeOptions = new[]
        {
            new ThemeOption("light", AppResources.ThemeLight),
            new ThemeOption("dark", AppResources.ThemeDark)
        };

        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Key == settingsService.ThemePreference)
            ?? ThemeOptions.First();
        ApplyTheme(SelectedTheme.Key);
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value == null)
            return;

        settingsService.ThemePreference = value.Key;
        ApplyTheme(value.Key);
    }

    private static void ApplyTheme(string themeKey)
    {
        var theme = themeKey == "light" ? AppTheme.Light : AppTheme.Dark;
        if (Application.Current != null)
            Application.Current.UserAppTheme = theme;
    }

    public sealed record ThemeOption(string Key, string Display);
}
