using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class PhotoPeopleEditorViewModel : ObservableObject
{
    private readonly FaceThumbnailService faceThumbnails;
    private readonly PeopleDataService peopleData;
    private readonly PeopleTagService peopleTagService;
    private readonly PeopleRecognitionService recognitionService;
    [ObservableProperty] private ObservableCollection<FaceTagItemViewModel> faces = new();

    private bool hasLoaded;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string mediaPath = string.Empty;
    [ObservableProperty] private string peopleTagsText = string.Empty;

    public PhotoPeopleEditorViewModel(
        PeopleDataService peopleData,
        PeopleRecognitionService recognitionService,
        FaceThumbnailService faceThumbnails,
        PeopleTagService peopleTagService)
    {
        this.peopleData = peopleData;
        this.recognitionService = recognitionService;
        this.faceThumbnails = faceThumbnails;
        this.peopleTagService = peopleTagService;
    }

    public void Initialize(string path)
    {
        MediaPath = path ?? string.Empty;
        hasLoaded = false;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(MediaPath))
            return;

        try
        {
            IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var ct = cts.Token;

            // Ensure faces exist and are assigned to PersonIds.
            await recognitionService
                .EnsurePeopleTagsForMediaAsync(new MediaItem { Path = MediaPath, MediaType = MediaType.Photos }, ct)
                .ConfigureAwait(false);

            var faceInfos = await peopleData.GetFacesForMediaAsync(MediaPath, ct).ConfigureAwait(false);
            var items = new List<FaceTagItemViewModel>(faceInfos.Count);
            foreach (var face in faceInfos.OrderBy(f => f.FaceIndex))
            {
                ct.ThrowIfCancellationRequested();
                var thumb = await faceThumbnails.EnsureFaceThumbnailAsync(MediaPath, face.Embedding, 96, ct)
                    .ConfigureAwait(false);
                items.Add(new FaceTagItemViewModel(face.PersonId, face.PersonName, face.FaceIndex, thumb));
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Faces = new ObservableCollection<FaceTagItemViewModel>(items);
            });

            // Keep the free-form tag editor in sync with the DB.
            var tags = await peopleTagService.GetTagsForMediaAsync(MediaPath).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                PeopleTagsText = string.Join(", ", tags.Where(t => !string.IsNullOrWhiteSpace(t)));
            });

            hasLoaded = true;
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

    /// <summary>
    ///     Saves the current editor state and returns a normalized summary string
    ///     that can be displayed immediately on the originating media tile.
    /// </summary>
    public async Task<string?> SaveAndGetSummaryAsync()
    {
        if (string.IsNullOrWhiteSpace(MediaPath))
            return null;

        // Ensure the view model is fully loaded before applying a save to avoid
        // accidentally overwriting auto-derived tags with an empty Faces collection.
        if (!hasLoaded)
            await LoadAsync().ConfigureAwait(false);

        try
        {
            IsBusy = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var ct = cts.Token;

            // Apply user-provided names, merging people intelligently when the name already exists.
            var updates = Faces
                .Select(f => new { f.PersonId, Name = (f.Name ?? string.Empty).Trim() })
                .Where(x => !string.IsNullOrWhiteSpace(x.PersonId) && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.PersonId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { PersonId = g.Key, g.Last().Name })
                .ToList();

            foreach (var u in updates)
            {
                ct.ThrowIfCancellationRequested();
                await recognitionService.RenamePersonAsync(u.PersonId, u.Name, ct).ConfigureAwait(false);
            }

            // Manual people tags: editable list (comma/semicolon separated).
            var manualTags = (PeopleTagsText ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Preserve the auto-derived names from the face list, but allow the user
            // to fully edit the manual tag list.
            var autoNames = Faces
                .Select(f => (f.Name ?? string.Empty).Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var combined = autoNames
                .Concat(manualTags)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Persist: we store a single unified tag list for the media.
            // This keeps the tile summary consistent and makes "edit" deterministic.
            await peopleTagService.SetTagsForMediaAsync(MediaPath, combined).ConfigureAwait(false);

            // Keep UI text normalized so reopening shows the saved value.
            await MainThread.InvokeOnMainThreadAsync(() => { PeopleTagsText = string.Join(", ", combined); });

            return string.Join(", ", combined);
        }
        catch
        {
            return null;
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        _ = await SaveAndGetSummaryAsync().ConfigureAwait(false);
    }
}

public sealed partial class FaceTagItemViewModel : ObservableObject
{
    [ObservableProperty] private string name;

    public FaceTagItemViewModel(string personId, string name, int faceIndex, string? thumbnailPath)
    {
        PersonId = personId;
        Name = name ?? string.Empty;
        FaceIndex = faceIndex;
        ThumbnailPath = thumbnailPath;
    }

    public string PersonId { get; }
    public int FaceIndex { get; }
    public string? ThumbnailPath { get; }
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);
}