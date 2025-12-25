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
}