using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        // Make the UI react instantly: disable the button immediately, then yield one frame.
        // Any longer work (refreshing queries, indexing, etc.) is handled asynchronously when the MainPage appears.
        BackButton.IsEnabled = false;
        await Task.Yield();

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync(false);
    }
}