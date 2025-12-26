using UltimateVideoBrowser.Views;

namespace UltimateVideoBrowser;

public partial class App : Application
{
    private const string LightDictionary = "Resources/Styles/Colors.Light.xaml";
    private const string DarkDictionary = "Resources/Styles/Colors.Dark.xaml";

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var settingsService = serviceProvider.GetRequiredService<Services.AppSettingsService>();
        ApplyTheme(settingsService.ThemePreference);

        var mainPage = serviceProvider.GetRequiredService<MainPage>();
        MainPage = new NavigationPage(mainPage);

        RequestedThemeChanged += (_, args) => ApplyThemeDictionary(args.RequestedTheme);
    }

    private static void ApplyTheme(string themePreference)
    {
        var theme = themePreference == "light" ? AppTheme.Light : AppTheme.Dark;
        if (Current != null)
        {
            Current.UserAppTheme = theme;
            ApplyThemeDictionary(theme);
        }
    }

    private static void ApplyThemeDictionary(AppTheme theme)
    {
        if (Current?.Resources?.MergedDictionaries == null)
            return;

        var dictionaries = Current.Resources.MergedDictionaries;
        var toRemove = new List<ResourceDictionary>();
        foreach (var dictionary in dictionaries)
        {
            if (dictionary is Resources.Styles.ColorsLight || dictionary is Resources.Styles.ColorsDark)
            {
                toRemove.Add(dictionary);
            }
        }

        foreach (var dictionary in toRemove)
            dictionaries.Remove(dictionary);

        var themeDictionary = theme == AppTheme.Dark
            ? new Resources.Styles.ColorsDark()
            : new Resources.Styles.ColorsLight();
        dictionaries.Add(themeDictionary);
    }
}
