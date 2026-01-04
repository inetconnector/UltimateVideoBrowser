namespace UltimateVideoBrowser.Views.Components;

public partial class MainHeroView : ContentView
{
    public MainHeroView()
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
