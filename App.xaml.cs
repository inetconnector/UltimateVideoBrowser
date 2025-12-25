using Microsoft.Extensions.DependencyInjection;
using UltimateVideoBrowser.Views;

namespace UltimateVideoBrowser;

public partial class App : Application
{
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        var mainPage = serviceProvider.GetRequiredService<MainPage>();
        MainPage = new NavigationPage(mainPage);
    }
}
