using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class PersonPage : ContentPage
{
    private const double SpanWidthTolerance = 1;
    private readonly IServiceProvider serviceProvider;
    private readonly DeviceModeService deviceMode;
    private readonly PersonViewModel vm;
    private double lastSpanWidth = -1;

    public PersonPage(IServiceProvider serviceProvider, DeviceModeService deviceMode, PersonViewModel vm)
    {
        InitializeComponent();
        this.serviceProvider = serviceProvider;
        this.deviceMode = deviceMode;
        this.vm = vm;
        BindingContext = vm;
        SizeChanged += OnSizeChanged;
    }

    public void Initialize(string personId, string initialName)
    {
        vm.Initialize(personId, initialName);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.LoadAsync();
        UpdateGridSpan();
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

        await OpenTagEditorAsync(item);
    }

    private async void OnMergeClicked(object sender, EventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(vm.PersonId))
                return;

            var peopleData = serviceProvider.GetService<PeopleDataService>();
            if (peopleData == null)
                return;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var candidates = await peopleData.ListMergeCandidatesAsync(vm.PersonId, cts.Token).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                await DisplayAlert("Merge", "No merge targets available.", "OK");
                return;
            }

            // Build a stable mapping from display text to person id.
            var options = candidates
                .Select(p => string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name)
                .Distinct()
                .Take(30)
                .ToArray();

            var choice = await MainThread.InvokeOnMainThreadAsync(() =>
                DisplayActionSheet(
                    AppResources.MergeIntoTitle,
                    AppResources.CancelButton,
                    null,
                    options));

            if (string.IsNullOrWhiteSpace(choice) ||
                choice == AppResources.CancelButton)
                return;

            var target =
                candidates.FirstOrDefault(p => string.Equals(p.Name, choice, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(p => string.Equals(p.Id, choice, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return;

            var confirm = await MainThread.InvokeOnMainThreadAsync(() =>
                DisplayAlert(
                    AppResources.MergeButton,
                    $"{vm.Name} → {target.Name}",
                    AppResources.OkButton,
                    AppResources.CancelButton));

            if (!confirm)
                return;

            await peopleData.MergePersonsAsync(vm.PersonId, target.Id, cts.Token).ConfigureAwait(false);

            // Navigate back to People page (merged source becomes a redirect).
            await MainThread.InvokeOnMainThreadAsync(async () => { await Navigation.PopAsync(); });
        }
        catch
        {
            // Ignore
        }
    }

    private async void OnTagPeopleClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.CommandParameter is not MediaItem item)
            return;

        await OpenTagEditorAsync(item);
    }

    private async void OnOpenFolderClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.CommandParameter is not MediaItem item)
            return;

        var mainViewModel = serviceProvider.GetService<MainViewModel>();
        if (mainViewModel == null)
            return;

        await mainViewModel.OpenFolderAsync(item);
    }

    private async Task OpenTagEditorAsync(MediaItem item)
    {
        if (item == null)
            return;

        if (string.IsNullOrWhiteSpace(item.Path))
            return;

        var page = ActivatorUtilities.CreateInstance<PhotoPeopleEditorPage>(serviceProvider);
        page.Initialize(item);
        await Navigation.PushAsync(page);
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        UpdateGridSpan();
    }

    private void UpdateGridSpan()
    {
        if (PersonGridLayout == null)
            return;

        var mode = deviceMode.GetUiMode();
        if (mode == UiMode.Phone)
        {
            PersonGridLayout.Span = 1;
            return;
        }

        var width = PersonMediaItemsView?.Width ?? Width;
        if (width <= 0)
            width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

        if (Math.Abs(width - lastSpanWidth) < SpanWidthTolerance)
            return;

        lastSpanWidth = width;

        var minTileWidth = 240;
        var tilePadding = 20;
        var targetTile = minTileWidth + tilePadding;
        if (mode == UiMode.Tv)
            targetTile = 340;
        else if (mode == UiMode.Tablet)
            targetTile = 300;

        var span = Math.Max(2, (int)(width / targetTile));
        PersonGridLayout.Span = Math.Min(8, span);
    }
}
