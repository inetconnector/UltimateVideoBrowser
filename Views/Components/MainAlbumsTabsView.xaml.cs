using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Views.Components;

public partial class MainAlbumsTabsView : ContentView
{
    private readonly MenuFlyout actionsFlyout;

    public MainAlbumsTabsView()
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
        flyout.Add(CreateFlyoutItem(AppResources.ManageAlbumsButton, "OpenAlbumsCommand"));

        var mapItem = CreateFlyoutItem(AppResources.MapButton, "OpenMapCommand");
        mapItem.SetBinding(MenuFlyoutItem.IsEnabledProperty, "IsLocationEnabled");
        flyout.Add(mapItem);

        return flyout;
    }

    private static MenuFlyoutItem CreateFlyoutItem(string text, string commandPath)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.SetBinding(MenuFlyoutItem.CommandProperty, commandPath);
        return item;
    }
}
