using UltimateVideoBrowser.Views;

namespace UltimateVideoBrowser;

public partial class App : Application
{
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var settingsService = serviceProvider.GetRequiredService<Services.AppSettingsService>();
        ApplyTheme(settingsService.ThemePreference);

        var mainPage = serviceProvider.GetRequiredService<MainPage>();
        MainPage = new NavigationPage(mainPage);
    }

    private static void ApplyTheme(string themePreference)
    {
        var theme = themePreference == "light" ? AppTheme.Light : AppTheme.Dark;
        if (Current != null)
            Current.UserAppTheme = theme;
    }
}
