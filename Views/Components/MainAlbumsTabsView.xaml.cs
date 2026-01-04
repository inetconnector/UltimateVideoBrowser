using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views.Components;

public partial class MainAlbumsTabsView : ContentView
{
    public MainAlbumsTabsView()
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
        flyout.Add(CreateFlyoutItem(AppResources.ManageAlbumsButton, "OpenAlbumsCommand"));

        var mapItem = CreateFlyoutItem(AppResources.MapButton, "OpenMapCommand");
        mapItem.SetBinding(MenuFlyoutItem.IsVisibleProperty, "IsLocationEnabled");
        flyout.Add(mapItem);

        FlyoutBase.SetAttachedFlyout(ActionsButton, flyout);
    }

    private static MenuFlyoutItem CreateFlyoutItem(string text, string commandPath)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.SetBinding(MenuFlyoutItem.CommandProperty, commandPath);
        return item;
    }
}
