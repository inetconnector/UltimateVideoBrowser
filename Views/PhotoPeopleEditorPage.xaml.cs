using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class PhotoPeopleEditorPage : ContentPage
{
    private readonly PlaybackService playback;
    private readonly PhotoPeopleEditorViewModel vm;
    private MediaItem? current;

    public PhotoPeopleEditorPage(PlaybackService playback, PhotoPeopleEditorViewModel vm)
    {
        InitializeComponent();
        this.playback = playback;
        this.vm = vm;
        BindingContext = vm;
    }

    public void Initialize(MediaItem item)
    {
        current = item;
        vm.Initialize(item.Path);
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

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        try
        {
            var summary = await vm.SaveAndGetSummaryAsync().ConfigureAwait(false);

            // If saving failed, stay on the page (best-effort, errors are handled inside the VM).
            if (summary == null)
                return;

            // Update the originating MediaItem immediately so the grid reflects the change without a refresh.
            if (current != null && summary != null)
                current.PeopleTagsSummary = summary;

            await MainThread.InvokeOnMainThreadAsync(async () => await Navigation.PopAsync());
        }
        catch
        {
            // Ignore
        }
    }

    private void OnOpenClicked(object sender, EventArgs e)
    {
        if (current == null)
            return;

        try
        {
            playback.Open(current);
        }
        catch
        {
            // Ignore
        }
    }
}