using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public sealed class PersonViewModel : ObservableObject
{
    private readonly PeopleDataService peopleData;
    private readonly ThumbnailService thumbnails;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string personId = string.Empty;
    [ObservableProperty] private ObservableCollection<MediaItem> photos = new();
    [ObservableProperty] private float qualityScore;

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

            // Load person profile so we can show quality in the UI.
            var profile = await peopleData.GetPersonProfileAsync(PersonId, cts.Token).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => { QualityScore = profile?.QualityScore ?? 0f; });

            var result = await peopleData.GetMediaForPersonAsync(PersonId, cts.Token).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => { Photos = new ObservableCollection<MediaItem>(result); });

            // Generate thumbnails lazily so the UI stays responsive.
            foreach (var item in result)
                _ = EnsureAndApplyThumbnailAsync(item);
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

    private async Task EnsureAndApplyThumbnailAsync(MediaItem item)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var p = await thumbnails.EnsureThumbnailAsync(item, cts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(p))
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Force UI refresh when the path stays identical.
                if (string.Equals(item.ThumbnailPath, p, StringComparison.OrdinalIgnoreCase))
                    item.ThumbnailPath = string.Empty;

                item.ThumbnailPath = p;
            });
        }
        catch
        {
            // Ignore
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