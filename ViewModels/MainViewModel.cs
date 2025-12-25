using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    readonly SourceService sourceService;
    readonly IndexService indexService;
    readonly ThumbnailService thumbnailService;
    readonly PlaybackService playbackService;
    readonly PermissionService permissionService;

    CancellationTokenSource? thumbCts;

    [ObservableProperty] List<VideoItem> videos = new();
    [ObservableProperty] string searchText = "";
    [ObservableProperty] bool isIndexing;
    [ObservableProperty] int indexedCount;
    [ObservableProperty] int indexProcessed;
    [ObservableProperty] int indexTotal;
    [ObservableProperty] double indexRatio;
    [ObservableProperty] string indexStatus = "";
    [ObservableProperty] bool hasMediaPermission = true;
    [ObservableProperty] string activeSourceId = "";
    [ObservableProperty] SortOption? selectedSortOption;

    public IReadOnlyList<SortOption> SortOptions { get; }

    public MainViewModel(
        SourceService sourceService,
        IndexService indexService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;

        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration),
        };
        SelectedSortOption = SortOptions.FirstOrDefault();
    }

    public async Task InitializeAsync()
    {
        await sourceService.EnsureDefaultSourceAsync();

        HasMediaPermission = await permissionService.CheckMediaReadAsync();

        if (!HasMediaPermission)
        {
            Videos = new();
            return;
        }

        await RunIndexAsync();
        await RefreshAsync();
        StartThumbnailPipeline();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var sortKey = SelectedSortOption?.Key ?? "name";
        Videos = await indexService.QueryAsync(SearchText, ActiveSourceId, sortKey);
        StartThumbnailPipeline();
    }

    [RelayCommand]
    public async Task RunIndexAsync()
    {
        if (IsIndexing) return;

        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (!HasMediaPermission)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.DisplayAlert(AppResources.PermissionTitle, AppResources.PermissionMessage, AppResources.OkButton));
            return;
        }

        IsIndexing = true;
        IndexedCount = 0;
        IndexProcessed = 0;
        IndexTotal = 0;
        IndexRatio = 0;
        IndexStatus = AppResources.Indexing;

        try
        {
            var sources = (await sourceService.GetSourcesAsync()).Where(s => s.IsEnabled).ToList();
            var progress = new Progress<IndexProgress>(p =>
            {
                IndexProcessed = p.Processed;
                IndexTotal = p.Total;
                IndexRatio = p.Ratio;
                IndexStatus = string.Format(AppResources.IndexingStatusFormat, p.SourceName, p.Processed, p.Total);
                IndexedCount = p.Inserted;
            });
            using var cts = new CancellationTokenSource();

            await indexService.IndexSourcesAsync(sources, progress, cts.Token);
        }
        finally
        {
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    public void Play(VideoItem item) => playbackService.Play(item);

    [RelayCommand]
    public async Task RequestPermissionAsync()
    {
        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (HasMediaPermission)
            await RunIndexAsync();
    }

    void StartThumbnailPipeline()
    {
        thumbCts?.Cancel();
        thumbCts?.Dispose();
        thumbCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                var ct = thumbCts.Token;
                // Generate thumbnails for the first N visible items quickly, then continue.
                foreach (var item in Videos.Take(80))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                        continue;

                    var p = await thumbnailService.EnsureThumbnailAsync(item, ct);
                    if (!string.IsNullOrWhiteSpace(p))
                        item.ThumbnailPath = p;

                    MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(Videos)));
                }
            }
            catch
            {
                // Ignore cancellation or retrieval failures.
            }
        });
    }
}
