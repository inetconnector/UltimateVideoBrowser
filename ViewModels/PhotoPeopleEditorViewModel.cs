using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class PhotoPeopleEditorViewModel : ObservableObject
{
    private readonly PeopleDataService peopleData;
    private readonly PeopleRecognitionService recognitionService;
    private readonly FaceThumbnailService faceThumbnails;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string mediaPath = string.Empty;
    [ObservableProperty] private ObservableCollection<FaceTagItemViewModel> faces = new();

    public PhotoPeopleEditorViewModel(
        PeopleDataService peopleData,
        PeopleRecognitionService recognitionService,
        FaceThumbnailService faceThumbnails)
    {
        this.peopleData = peopleData;
        this.recognitionService = recognitionService;
        this.faceThumbnails = faceThumbnails;
    }

    public void Initialize(string path)
    {
        MediaPath = path ?? string.Empty;
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
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(MediaPath))
            return;

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
                .Select(g => new { PersonId = g.Key, Name = g.Last().Name })
                .ToList();

            foreach (var u in updates)
            {
                ct.ThrowIfCancellationRequested();
                await recognitionService.RenamePersonAsync(u.PersonId, u.Name, ct).ConfigureAwait(false);
            }

            // Refresh the editor view so the latest names show up.
            await LoadAsync().ConfigureAwait(false);
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

public sealed partial class FaceTagItemViewModel : ObservableObject
{
    public FaceTagItemViewModel(string personId, string name, int faceIndex, string? thumbnailPath)
    {
        PersonId = personId;
        Name = name ?? string.Empty;
        FaceIndex = faceIndex;
        ThumbnailPath = thumbnailPath;
    }

    public string PersonId { get; }
    public int FaceIndex { get; }

    [ObservableProperty] private string name;
    public string? ThumbnailPath { get; }
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath);
}
