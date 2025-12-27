using UltimateVideoBrowser.Views;
using System.Linq;

namespace UltimateVideoBrowser;

public partial class App : Application
{
    private readonly Services.AppSettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _serviceProvider = serviceProvider;
        _settingsService = serviceProvider.GetRequiredService<Services.AppSettingsService>();

        ApplyThemePreference(_settingsService.ThemePreference);

        RequestedThemeChanged += (_, args) =>
        {
            if (IsFollowingSystem(_settingsService.ThemePreference))
                ApplyThemeDictionary(args.RequestedTheme);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = _serviceProvider.GetRequiredService<MainPage>();
        return new Window(new NavigationPage(mainPage));
    }

    private static bool IsFollowingSystem(string themePreference)
    {
        var pref = (themePreference ?? string.Empty).Trim().ToLowerInvariant();
        return pref is "" or "system" or "auto" or "default";
    }

    private static void ApplyThemePreference(string themePreference)
    {
        if (Current == null)
            return;

        var pref = (themePreference ?? string.Empty).Trim().ToLowerInvariant();

        var theme = pref switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        ApplyTheme(theme);
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current == null)
            return;

        Current.UserAppTheme = theme;

        var effectiveTheme = Current.UserAppTheme == AppTheme.Unspecified
            ? Current.RequestedTheme
            : Current.UserAppTheme;

        ApplyThemeDictionary(effectiveTheme);
    }

    private static void ApplyThemeDictionary(AppTheme theme)
    {
        var merged = Current?.Resources?.MergedDictionaries;
        if (merged == null)
            return;

        var toRemove = merged
            .Where(d => d is Resources.Styles.ColorsLight || d is Resources.Styles.ColorsDark)
            .ToList();

        foreach (var d in toRemove)
            merged.Remove(d);

        ResourceDictionary themeDictionary =
            theme == AppTheme.Dark
                ? (ResourceDictionary)new Resources.Styles.ColorsDark()
                : new Resources.Styles.ColorsLight();

        merged.Add(themeDictionary);
    }
}
