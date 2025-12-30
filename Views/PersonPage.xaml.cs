using Microsoft.Extensions.DependencyInjection;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class PersonPage : ContentPage
{
    private readonly IServiceProvider serviceProvider;
    private readonly PersonViewModel vm;

    public PersonPage(IServiceProvider serviceProvider, PersonViewModel vm)
    {
        InitializeComponent();
        this.serviceProvider = serviceProvider;
        this.vm = vm;
        BindingContext = vm;
    }

    public void Initialize(string personId, string initialName)
    {
        vm.Initialize(personId, initialName);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.LoadAsync();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnPhotoSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection == null || e.CurrentSelection.Count == 0)
            return;

        var item = e.CurrentSelection[0] as MediaItem;
        if (item == null)
            return;

        if (sender is CollectionView cv)
            cv.SelectedItem = null;

        if (string.IsNullOrWhiteSpace(item.Path))
            return;

        var page = ActivatorUtilities.CreateInstance<PhotoPeopleEditorPage>(serviceProvider);
        page.Initialize(item);
        await Navigation.PushAsync(page);
    }
}
