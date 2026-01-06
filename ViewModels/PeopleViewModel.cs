using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class PeopleViewModel : ObservableObject
{
    private readonly FaceThumbnailService faceThumbnails;
    private readonly PeopleDataService peopleData;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ObservableCollection<PersonListItemViewModel> people = new();

    private CancellationTokenSource? searchCts;
    [ObservableProperty] private string searchText = string.Empty;

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
                string? coverPath = null;
                if (p.CoverFace != null)
                    coverPath = await faceThumbnails
                        .EnsureFaceThumbnailAsync(p.CoverFace.MediaPath, p.CoverFace, 96, ct)
                        .ConfigureAwait(false);

                items.Add(new PersonListItemViewModel(p.Id, p.Name, p.PhotoCount, p.QualityScore, coverPath,
                    p.IsIgnored));
            }

            var sorted = items
                .OrderByDescending(item => item.PhotoCount)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                People = new ObservableCollection<PersonListItemViewModel>(sorted);
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
    [ObservableProperty] private bool isIgnored;
    [ObservableProperty] private string name;

    public PersonListItemViewModel(string id, string name, int photoCount, float qualityScore,
        string? coverThumbnailPath, bool isIgnored)
    {
        Id = id;
        Name = name;
        PhotoCount = photoCount;
        QualityScore = qualityScore;
        CoverThumbnailPath = coverThumbnailPath;
        IsIgnored = isIgnored;
    }

    public string Id { get; }
    public int PhotoCount { get; }

    public float QualityScore { get; }

    public string PhotoCountText => PhotoCount == 1 ? "1 photo" : $"{PhotoCount} photos";

    public string QualityScoreText => $"{QualityScore:0.00}";

    public string? CoverThumbnailPath { get; }

    public bool HasCover => !string.IsNullOrWhiteSpace(CoverThumbnailPath);
}