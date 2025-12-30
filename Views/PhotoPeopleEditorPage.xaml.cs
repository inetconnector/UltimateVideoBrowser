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
