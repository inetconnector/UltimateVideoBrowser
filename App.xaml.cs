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
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (string.Equals(source, LightDictionary, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, DarkDictionary, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        var themeDictionary = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? DarkDictionary : LightDictionary, UriKind.Relative)
        };
        dictionaries.Add(themeDictionary);
    }
}
