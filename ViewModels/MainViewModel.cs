using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileExportService fileExportService;
    private readonly IndexService indexService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    private readonly ThumbnailService thumbnailService;
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private DateTime dateFilterFrom = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime dateFilterTo = DateTime.Today;
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;

    private CancellationTokenSource? indexCts;
    [ObservableProperty] private string indexCurrentFile = "";
    [ObservableProperty] private string indexCurrentFolder = "";
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private string sourcesSummary = "";
    private CancellationTokenSource? thumbCts;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();
    [ObservableProperty] private int totalSourceCount;
    [ObservableProperty] private int videoCount;

    [ObservableProperty] private List<VideoItem> videos = new();
    [ObservableProperty] private int markedCount;

    private readonly PropertyChangedEventHandler videoMarkedHandler;
    private List<VideoItem> subscribedVideos = new();
    private int indexLastInserted;
    private DateTime indexLastRefresh;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly object indexProgressLock = new();
    private bool isApplyingIndexProgress;
    private IndexProgress? pendingIndexProgress;

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService,
        IFileExportService fileExportService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;
        this.fileExportService = fileExportService;

        videoMarkedHandler = OnVideoPropertyChanged;
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
        await refreshLock.WaitAsync();
        try
        {
            var sources = await sourceService.GetSourcesAsync();
            var normalizedSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);

            var sortKey = SelectedSortOption?.Key ?? "name";
            var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
            var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
            var videos = await indexService.QueryAsync(SearchText, normalizedSourceId, sortKey, dateFrom, dateTo);

            if (string.IsNullOrWhiteSpace(normalizedSourceId))
            {
                var enabledIds = sources.Where(s => s.IsEnabled).Select(s => s.Id).ToHashSet();
                videos = videos.Where(v => v.SourceId != null && enabledIds.Contains(v.SourceId)).ToList();
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ActiveSourceId = normalizedSourceId;
                _ = UpdateSourceStatsAsync(sources);
                Videos = videos;
                StartThumbnailPipeline();
            });
        }
        finally
        {
            refreshLock.Release();
        }
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
        IndexCurrentFolder = "";
        IndexCurrentFile = "";

        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (!HasMediaPermission)
        {
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
            await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current?.DisplayAlertAsync(
                    AppResources.PermissionTitle,
                    AppResources.PermissionMessage,
                    AppResources.OkButton) ?? Task.CompletedTask);
            return;
        }

        IsIndexing = true;
        IndexedCount = 0;
        IndexProcessed = 0;
        IndexTotal = 0;
        IndexRatio = 0;
        IndexStatus = AppResources.Indexing;
        IndexCurrentFolder = "";
        IndexCurrentFile = "";

        var completed = false;
        try
        {
            var sources = (await sourceService.GetSourcesAsync()).Where(s => s.IsEnabled).ToList();
            indexLastRefresh = DateTime.UtcNow;
            indexLastInserted = 0;
            var progress = new Progress<IndexProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() => QueueIndexProgress(p));
            });
            indexCts?.Cancel();
            indexCts?.Dispose();
            indexCts = new CancellationTokenSource();

            await indexService.IndexSourcesAsync(sources, progress, indexCts.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            completed = false;
        }
        finally
        {
            indexCts?.Dispose();
            indexCts = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsIndexing = false;
                IndexStatus = "";
                IndexRatio = 0;
                IndexCurrentFolder = "";
                IndexCurrentFile = "";
            });
        }

        if (completed)
            settingsService.NeedsReindex = false;

        await RefreshAsync();
    }

    [RelayCommand]
    public void CancelIndex()
    {
        indexCts?.Cancel();
    }

    [RelayCommand]
    public void Play(VideoItem item)
    {
        playbackService.Play(item);
    }

    [RelayCommand]
    public async Task ShareAsync(VideoItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = AppResources.ShareTitle,
            File = new ShareFile(item.Path)
        });
    }

    [RelayCommand]
    public Task SaveAsAsync(VideoItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return Task.CompletedTask;

        return fileExportService.SaveAsAsync(item);
    }

    [RelayCommand]
    public async Task CopyMarkedAsync()
    {
        var markedItems = Videos.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        await fileExportService.CopyToFolderAsync(markedItems);
    }

    [RelayCommand]
    public async Task MoveMarkedAsync()
    {
        var markedItems = Videos.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        var moved = await fileExportService.MoveToFolderAsync(markedItems);
        if (moved.Count > 0)
        {
            await indexService.RemoveAsync(moved);
            Videos = Videos.Except(moved).ToList();
        }
        else
        {
            UpdateMarkedCount();
        }
    }

    [RelayCommand]
    public void ClearMarked()
    {
        foreach (var item in Videos.Where(v => v.IsMarked))
            item.IsMarked = false;
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
                    if (!string.IsNullOrWhiteSpace(p)) MainThread.BeginInvokeOnMainThread(() => item.ThumbnailPath = p);
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
        IsDateFilterEnabled = settingsService.DateFilterEnabled;
        DateFilterFrom = settingsService.DateFilterFrom;
        DateFilterTo = settingsService.DateFilterTo;
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
        TimelineEntries = BuildTimelineEntries(value);
        SubscribeToMarkedChanges(value);
        UpdateMarkedCount();
    }

    partial void OnMarkedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMarked));
    }

    public bool HasMarked => MarkedCount > 0;

    partial void OnActiveSourceIdChanged(string value)
    {
        settingsService.ActiveSourceId = value;
    }

    partial void OnSearchTextChanged(string value)
    {
        settingsService.SearchText = value;
    }

    partial void OnIsDateFilterEnabledChanged(bool value)
    {
        settingsService.DateFilterEnabled = value;
        _ = RefreshAsync();
    }

    partial void OnDateFilterFromChanged(DateTime value)
    {
        if (value > DateFilterTo)
            DateFilterTo = value;
        settingsService.DateFilterFrom = value;
        if (IsDateFilterEnabled)
            _ = RefreshAsync();
    }

    partial void OnDateFilterToChanged(DateTime value)
    {
        if (value < DateFilterFrom)
            DateFilterFrom = value;
        settingsService.DateFilterTo = value;
        if (IsDateFilterEnabled)
            _ = RefreshAsync();
    }

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value != null)
            settingsService.SelectedSortOptionKey = value.Key;
    }

    private static List<TimelineEntry> BuildTimelineEntries(List<VideoItem>? items)
    {
        if (items == null || items.Count == 0)
            return new List<TimelineEntry>();

        var entries = new List<TimelineEntry>();
        var lastKey = "";
        var lastYear = -1;

        foreach (var item in items)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, item.DateAddedSeconds))
                .ToLocalTime()
                .DateTime;
            var key = date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

            if (key == lastKey)
                continue;

            var showYear = date.Year != lastYear;
            entries.Add(new TimelineEntry(date.Year, date.Month, item, showYear));
            lastKey = key;
            lastYear = date.Year;
        }

        return entries;
    }

    private void SubscribeToMarkedChanges(List<VideoItem>? items)
    {
        if (subscribedVideos.Count > 0)
        {
            foreach (var video in subscribedVideos)
                video.PropertyChanged -= videoMarkedHandler;
        }

        subscribedVideos = items ?? new List<VideoItem>();

        foreach (var video in subscribedVideos)
            video.PropertyChanged += videoMarkedHandler;
    }

    private void OnVideoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoItem.IsMarked))
            UpdateMarkedCount();
    }

    private void UpdateMarkedCount()
    {
        MarkedCount = Videos?.Count(v => v.IsMarked) ?? 0;
    }

    private void UpdateIndexLocation(string sourceName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            IndexCurrentFolder = sourceName;
            IndexCurrentFile = "";
            return;
        }

        var fileName = Path.GetFileName(path);
        var folderName = Path.GetDirectoryName(path);
        IndexCurrentFile = string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        IndexCurrentFolder = string.IsNullOrWhiteSpace(folderName) ? sourceName : folderName;
    }

    private void QueueIndexProgress(IndexProgress progress)
    {
        var next = progress;
        while (next != null)
        {
            lock (indexProgressLock)
            {
                if (isApplyingIndexProgress)
                {
                    pendingIndexProgress = next;
                    return;
                }

                isApplyingIndexProgress = true;
            }

            ApplyIndexProgress(next);

            lock (indexProgressLock)
            {
                isApplyingIndexProgress = false;
                next = pendingIndexProgress;
                pendingIndexProgress = null;
            }
        }
    }

    private void ApplyIndexProgress(IndexProgress progress)
    {
        IndexProcessed = progress.Processed;
        IndexTotal = progress.Total;
        IndexRatio = progress.Ratio;
        IndexStatus = string.Format(AppResources.IndexingStatusFormat, progress.SourceName, progress.Processed,
            progress.Total);
        UpdateIndexLocation(progress.SourceName, progress.CurrentPath);
        IndexedCount = progress.Inserted;

        if (progress.Inserted > indexLastInserted &&
            DateTime.UtcNow - indexLastRefresh > TimeSpan.FromMilliseconds(400))
        {
            indexLastInserted = progress.Inserted;
            indexLastRefresh = DateTime.UtcNow;
            _ = RefreshAsync();
        }
    }
}
