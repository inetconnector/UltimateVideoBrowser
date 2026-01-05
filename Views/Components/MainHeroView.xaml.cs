using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views.Components;

public partial class MainHeroView : ContentView
{
    private readonly MenuFlyout actionsFlyout;

    public MainHeroView()
    {
        InitializeComponent();
        actionsFlyout = CreateActionsFlyout();
    }

    private void OnActionsClicked(object sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            actionsFlyout.ShowAt(element);
        }
    }

    private MenuFlyout CreateActionsFlyout()
    {
        var flyout = new MenuFlyout();
        flyout.Add(CreateFlyoutItem(AppResources.SourcesButton, "OpenSourcesCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.AlbumsButton, "OpenAlbumsCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.SettingsButton, "OpenSettingsCommand"));
        flyout.Add(CreateFlyoutItem(AppResources.ReindexButton, "RunIndexCommand"));
        return flyout;
    }

    private static MenuFlyoutItem CreateFlyoutItem(string text, string commandPath)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.SetBinding(MenuFlyoutItem.CommandProperty, commandPath);
        return item;
    }
}
