using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class ProUpgradePage : ContentPage
{
    public ProUpgradePage(ProUpgradeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        BackButton.IsEnabled = false;
        await Task.Yield();

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync(false);
    }
}