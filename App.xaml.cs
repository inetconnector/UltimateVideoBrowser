namespace UltimateVideoBrowser;

public partial class App : Application
{
    public App(Views.MainPage mainPage)
    {
        InitializeComponent();
        MainPage = new NavigationPage(mainPage);
    }
}
