using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IndexService indexService;
    private readonly AppSettingsService settingsService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly ISourceService sourceService;
    private readonly ThumbnailService thumbnailService;
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private string sourcesSummary = "";
    [ObservableProperty] private int totalSourceCount;
    [ObservableProperty] private int videoCount;

    private CancellationTokenSource? thumbCts;

    [ObservableProperty] private List<VideoItem> videos = new();

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;

        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };
        SelectedSortOption = SortOptions.FirstOrDefault();
    }

    public IReadOnlyList<SortOption> SortOptions { get; }

    public async Task InitializeAsync()
    {
        await sourceService.EnsureDefaultSourceAsync();

        ApplySavedSettings();
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);

        HasMediaPermission = await permissionService.CheckMediaReadAsync();

        if (!HasMediaPermission)
        {
            Videos = new List<VideoItem>();
            return;
        }

        await RefreshAsync();
        StartThumbnailPipeline();

        if (settingsService.NeedsReindex)
            _ = RunIndexAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);

        var sortKey = SelectedSortOption?.Key ?? "name";
        var videos = await indexService.QueryAsync(SearchText, ActiveSourceId, sortKey);

        if (string.IsNullOrWhiteSpace(ActiveSourceId))
        {
            var enabledIds = sources.Where(s => s.IsEnabled).Select(s => s.Id).ToHashSet();
            videos = videos.Where(v => v.SourceId != null && enabledIds.Contains(v.SourceId)).ToList();
        }

        Videos = videos;
        StartThumbnailPipeline();
    }

    [RelayCommand]
    public async Task RunIndexAsync()
    {
        if (IsIndexing) return;

        IsIndexing = true;
        IndexedCount = 0;
        IndexProcessed = 0;
        IndexTotal = 0;
        IndexRatio = 0;
        IndexStatus = AppResources.Indexing;

        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (!HasMediaPermission)
        {
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
            await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.DisplayAlert(AppResources.PermissionTitle, AppResources.PermissionMessage,
                    AppResources.OkButton));
            return;
        }

        IsIndexing = true;
        IndexedCount = 0;
        IndexProcessed = 0;
        IndexTotal = 0;
        IndexRatio = 0;
        IndexStatus = AppResources.Indexing;

        var completed = false;
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
            completed = true;
        }
        finally
        {
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
        }

        if (completed)
            settingsService.NeedsReindex = false;

        await RefreshAsync();
    }

    [RelayCommand]
    public void Play(VideoItem item)
    {
        playbackService.Play(item);
    }

    [RelayCommand]
    public async Task RequestPermissionAsync()
    {
        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (HasMediaPermission)
            await RunIndexAsync();
    }

    private void StartThumbnailPipeline()
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
                    {
                        MainThread.BeginInvokeOnMainThread(() => item.ThumbnailPath = p);
                    }
                }
            }
            catch
            {
                // Ignore cancellation or retrieval failures.
            }
        });
    }

    private async Task UpdateSourceStatsAsync()
    {
        var sources = await sourceService.GetSourcesAsync();
        await UpdateSourceStatsAsync(sources);
    }

    private Task UpdateSourceStatsAsync(List<MediaSource> sources)
    {
        TotalSourceCount = sources.Count;
        EnabledSourceCount = sources.Count(s => s.IsEnabled);
        SourcesSummary = string.Format(AppResources.SourcesSummaryFormat, EnabledSourceCount, TotalSourceCount);
        return Task.CompletedTask;
    }

    private void ApplySavedSettings()
    {
        SearchText = settingsService.SearchText;
        var sortKey = settingsService.SelectedSortOptionKey;
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.Key == sortKey) ?? SortOptions.FirstOrDefault();
        ActiveSourceId = settingsService.ActiveSourceId;
    }

    private static string NormalizeActiveSourceId(List<MediaSource> sources, string activeSourceId)
    {
        if (string.IsNullOrWhiteSpace(activeSourceId))
            return "";

        var exists = sources.Any(s => s.Id == activeSourceId && s.IsEnabled);
        return exists ? activeSourceId : "";
    }

    partial void OnVideosChanged(List<VideoItem> value)
    {
        VideoCount = value?.Count ?? 0;
    }

    partial void OnActiveSourceIdChanged(string value)
    {
        settingsService.ActiveSourceId = value;
    }

    partial void OnSearchTextChanged(string value)
    {
        settingsService.SearchText = value;
    }

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value != null)
            settingsService.SelectedSortOptionKey = value.Key;
    }
}
