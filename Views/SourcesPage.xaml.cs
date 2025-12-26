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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.InitializeAsync();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (Navigation?.NavigationStack?.Count > 1)
            await Navigation.PopAsync();
    }
}
