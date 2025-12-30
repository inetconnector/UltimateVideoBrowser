using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class PeopleViewModel : ObservableObject
{
    private readonly PeopleDataService peopleData;
    private readonly FaceThumbnailService faceThumbnails;

    private CancellationTokenSource? searchCts;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private ObservableCollection<PersonListItemViewModel> people = new();

    public PeopleViewModel(PeopleDataService peopleData, FaceThumbnailService faceThumbnails)
    {
        this.peopleData = peopleData;
        this.faceThumbnails = faceThumbnails;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = searchCts.Token;

        try
        {
            IsBusy = true;
            var overview = await peopleData.GetPeopleOverviewAsync(SearchText, ct).ConfigureAwait(false);

            var items = new List<PersonListItemViewModel>(overview.Count);
            foreach (var p in overview)
            {
                ct.ThrowIfCancellationRequested();
                var coverPath = p.CoverFace != null
                    ? await faceThumbnails.EnsureFaceThumbnailAsync(p.CoverFace.MediaPath, p.CoverFace, 96, ct)
                        .ConfigureAwait(false)
                    : null;
                items.Add(new PersonListItemViewModel(p.Id, p.Name, p.PhotoCount, coverPath));
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                People = new ObservableCollection<PersonListItemViewModel>(items);
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Debounce search a bit so typing stays responsive.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250).ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        });
    }
}

public sealed partial class PersonListItemViewModel : ObservableObject
{
    public PersonListItemViewModel(string id, string name, int photoCount, string? coverThumbnailPath)
    {
        Id = id;
        Name = name;
        PhotoCount = photoCount;
        CoverThumbnailPath = coverThumbnailPath;
    }

    public string Id { get; }

    [ObservableProperty] private string name;
    public int PhotoCount { get; }

    public string PhotoCountText => PhotoCount == 1 ? "1 photo" : $"{PhotoCount} photos";

    public string? CoverThumbnailPath { get; }

    public bool HasCover => !string.IsNullOrWhiteSpace(CoverThumbnailPath);
}
