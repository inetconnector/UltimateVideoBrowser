using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class TaggedPhotosPage : ContentPage
{
    private readonly PhotoPeopleEditorPage editorPage;
    private readonly TaggedPhotosViewModel vm;

    public TaggedPhotosPage(TaggedPhotosViewModel vm, PhotoPeopleEditorPage editorPage)
    {
        InitializeComponent();
        this.vm = vm;
        this.editorPage = editorPage;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.RefreshAsync();
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

    private async void OnPhotoSelected(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (e.CurrentSelection?.FirstOrDefault() is not MediaItem item)
                return;

            ((CollectionView)sender).SelectedItem = null;

            editorPage.Initialize(item);
            await Navigation.PushAsync(editorPage);
        }
        catch
        {
            // Ignore
        }
    }
}