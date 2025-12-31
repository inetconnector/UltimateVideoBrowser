using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class PeoplePage : ContentPage
{
    private readonly IServiceProvider serviceProvider;
    private readonly PeopleViewModel vm;

    public PeoplePage(IServiceProvider serviceProvider, PeopleViewModel vm)
    {
        InitializeComponent();
        this.serviceProvider = serviceProvider;
        this.vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.RefreshAsync();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    private async void OnTaggedPhotosClicked(object sender, EventArgs e)
    {
        try
        {
            var page = ActivatorUtilities.CreateInstance<TaggedPhotosPage>(serviceProvider);
            await Navigation.PushAsync(page);
        }
        catch
        {
            // Ignore
        }
    }

    private async void OnPersonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection == null || e.CurrentSelection.Count == 0)
            return;

        var item = e.CurrentSelection[0] as PersonListItemViewModel;
        if (item == null)
            return;

        if (sender is CollectionView cv)
            cv.SelectedItem = null;

        // Manual tag "people" are represented as synthetic ids ("tag:<name>").
        // They should open the Tagged Photos view filtered by that name.
        if (item.Id.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var page = ActivatorUtilities.CreateInstance<TaggedPhotosPage>(serviceProvider);
                page.Initialize(item.Name);
                await Navigation.PushAsync(page);
            }
            catch
            {
                // Ignore
            }

            return;
        }

        var personPage = ActivatorUtilities.CreateInstance<PersonPage>(serviceProvider);
        personPage.Initialize(item.Id, item.Name);
        await Navigation.PushAsync(personPage);
    }
}