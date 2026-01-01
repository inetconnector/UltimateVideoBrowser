using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Collections;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class AlbumsViewModel : ObservableObject
{
    private readonly AlbumService albumService;
    private readonly IDialogService dialogService;

    public AlbumsViewModel(AlbumService albumService, IDialogService dialogService)
    {
        this.albumService = albumService;
        this.dialogService = dialogService;
    }

    public ObservableRangeCollection<AlbumListItem> Albums { get; } = new();

    public async Task InitializeAsync()
    {
        var summaries = await albumService.GetAlbumSummariesAsync().ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() => Albums.ReplaceRange(summaries));
    }

    [RelayCommand]
    public async Task AddAlbumAsync()
    {
        var name = await dialogService.DisplayPromptAsync(
            AppResources.NewAlbumTitle,
            AppResources.NewAlbumPrompt,
            AppResources.NewAlbumConfirm,
            AppResources.CancelButton,
            AppResources.NewAlbumPlaceholder,
            80,
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(name))
            return;

        var existing = await albumService.FindByNameAsync(name).ConfigureAwait(false);
        if (existing != null)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.AlbumExistsTitle,
                string.Format(AppResources.AlbumExistsMessage, existing.Name),
                AppResources.OkButton);
            return;
        }

        await albumService.CreateAlbumAsync(name).ConfigureAwait(false);
        await InitializeAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RenameAlbumAsync(AlbumListItem album)
    {
        if (album == null)
            return;

        var name = await dialogService.DisplayPromptAsync(
            AppResources.RenameAlbumTitle,
            AppResources.RenameAlbumPrompt,
            AppResources.RenameAlbumConfirm,
            AppResources.CancelButton,
            AppResources.NewAlbumPlaceholder,
            80,
            Keyboard.Text,
            album.Name);

        if (string.IsNullOrWhiteSpace(name))
            return;

        if (string.Equals(name.Trim(), album.Name, StringComparison.Ordinal))
            return;

        var existing = await albumService.FindByNameAsync(name).ConfigureAwait(false);
        if (existing != null)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.AlbumExistsTitle,
                string.Format(AppResources.AlbumExistsMessage, existing.Name),
                AppResources.OkButton);
            return;
        }

        var target = await albumService.GetAlbumByIdAsync(album.Id).ConfigureAwait(false);
        if (target == null)
            return;

        target.Name = name.Trim();
        await albumService.UpdateAlbumAsync(target).ConfigureAwait(false);
        await InitializeAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task DeleteAlbumAsync(AlbumListItem album)
    {
        if (album == null)
            return;

        var confirmed = await dialogService.DisplayAlertAsync(
            AppResources.DeleteAlbumTitle,
            string.Format(AppResources.DeleteAlbumMessage, album.Name),
            AppResources.DeleteAlbumConfirm,
            AppResources.CancelButton);

        if (!confirmed)
            return;

        var target = await albumService.GetAlbumByIdAsync(album.Id).ConfigureAwait(false);
        if (target == null)
            return;

        await albumService.DeleteAlbumAsync(target).ConfigureAwait(false);
        await InitializeAsync().ConfigureAwait(false);
    }
}
