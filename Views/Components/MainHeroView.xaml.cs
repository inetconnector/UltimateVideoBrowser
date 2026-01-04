using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views.Components;

public partial class MainHeroView : ContentView
{
    public MainHeroView()
    {
        InitializeComponent();
        ConfigureActionsFlyout();
    }

    private void OnActionsClicked(object sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }

    private void ConfigureActionsFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Add(CreateFlyoutItem(AppResources.SourcesButton, "OpenSourcesCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.AlbumsButton, "OpenAlbumsCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.SettingsButton, "OpenSettingsCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.ReindexButton, "RunIndexCommand"));
        FlyoutBase.SetAttachedFlyout(ActionsButton, flyout);
    }

    private static MenuFlyoutItem CreateFlyoutItem(string text, string commandPath)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.SetBinding(MenuFlyoutItem.CommandProperty, commandPath);
        return item;
    }
}
