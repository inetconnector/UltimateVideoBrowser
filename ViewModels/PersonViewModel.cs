using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class PersonViewModel : ObservableObject
{
    private readonly PeopleDataService peopleData;
    private readonly ThumbnailService thumbnails;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string personId = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private ObservableCollection<MediaItem> photos = new();

    public PersonViewModel(PeopleDataService peopleData, ThumbnailService thumbnails)
    {
        this.peopleData = peopleData;
        this.thumbnails = thumbnails;
    }

    public void Initialize(string id, string initialName)
    {
        PersonId = id;
        Name = initialName ?? string.Empty;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(PersonId))
            return;

        try
        {
            IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await peopleData.GetMediaForPersonAsync(PersonId, cts.Token).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Photos = new ObservableCollection<MediaItem>(result);
            });

            // Generate thumbnails lazily in the background so the UI stays responsive.
            foreach (var item in result)
                _ = thumbnails.EnsureThumbnailAsync(item, CancellationToken.None);
        }
        catch
        {
            // Ignore
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }

    [RelayCommand]
    public async Task SaveNameAsync()
    {
        if (string.IsNullOrWhiteSpace(PersonId))
            return;

        var trimmed = (Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        try
        {
            IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await peopleData.RenamePersonAsync(PersonId, trimmed, cts.Token).ConfigureAwait(false);
            Name = trimmed;
        }
        catch
        {
            // Ignore
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }
}