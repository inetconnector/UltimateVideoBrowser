using UltimateVideoBrowser.Views;

namespace UltimateVideoBrowser;

public partial class App : Application
{
    public App(MainPage mainPage)
    {
        InitializeComponent();
        MainPage = new NavigationPage(mainPage);
    }
}