namespace UltimateVideoBrowser.Views.Components;

public partial class MainAlbumsTabsView : ContentView
{
    public MainAlbumsTabsView()
    {
        InitializeComponent();
    }

    private void OnActionsClicked(object sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }
}
