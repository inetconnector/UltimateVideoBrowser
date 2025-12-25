using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
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
    [ObservableProperty] string sortKey = "name";
    [ObservableProperty] string activeSourceId = "device_all";

    public IReadOnlyList<string> SortOptions { get; } = new[] { "name", "date", "duration" };

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
    }

    public async Task InitializeAsync()
    {
        await sourceService.EnsureDefaultSourceAsync();

        var ok = await permissionService.EnsureMediaReadAsync();
        if (!ok)
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
        Videos = await indexService.QueryAsync(SearchText, ActiveSourceId, SortKey);
        StartThumbnailPipeline();
    }

    [RelayCommand]
    public async Task RunIndexAsync()
    {
        if (IsIndexing) return;

        IsIndexing = true;
        IndexedCount = 0;

        try
        {
            var progress = new Progress<int>(i => IndexedCount = i);
            using var cts = new CancellationTokenSource();

            await indexService.IndexDeviceMediaStoreAsync(ActiveSourceId, progress, cts.Token);
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand]
    public void Play(VideoItem item) => playbackService.Play(item);

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
