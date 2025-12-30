using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class TaggedPhotosPage : ContentPage
{
    private readonly TaggedPhotosViewModel vm;
    private readonly PhotoPeopleEditorPage editorPage;

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
        await Navigation.PopAsync();
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
