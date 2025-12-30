using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class SourcesPage : ContentPage
{
    private readonly SourcesViewModel vm;

    public SourcesPage(SourcesViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Don't block the UI thread while loading sources/permissions.
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Yield();
                await vm.InitializeAsync();
            }
            catch
            {
                // Ignore
            }
        });
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (sender is Button b)
            b.IsEnabled = false;

        await Task.Yield();

        if (Navigation?.NavigationStack?.Count <= 1)
            return;

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Navigation.PopAsync(false);
            }
            catch
            {
                // Ignore
            }
        });
    }
}