using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDialogService dialogService;
    private readonly IFileExportService fileExportService;
    private readonly object indexProgressLock = new();
    private readonly IndexService indexService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    private readonly object thumbnailLock = new();
    private readonly ThumbnailService thumbnailService;

    private readonly PropertyChangedEventHandler videoMarkedHandler;
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private DateTime dateFilterFrom = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime dateFilterTo = DateTime.Today;
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;

    private CancellationTokenSource? indexCts;
    [ObservableProperty] private string indexCurrentFile = "";
    [ObservableProperty] private string indexCurrentFolder = "";
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexedVideoCount;
    private int indexLastInserted;
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    private int indexStartingCount;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    private bool isApplyingIndexProgress;
    private bool isInitialized;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private int markedCount;
    private IndexProgress? pendingIndexProgress;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private string sourcesSummary = "";
    private List<VideoItem> subscribedVideos = new();
    private CancellationTokenSource? thumbCts;
    private bool thumbnailPipelineQueued;
    private bool thumbnailPipelineRunning;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();
    [ObservableProperty] private int totalSourceCount;
    [ObservableProperty] private int videoCount;
    [ObservableProperty] private bool isSourceSwitching;

    [ObservableProperty] private List<VideoItem> videos = new();
    private int videosVersion;

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService,
        IFileExportService fileExportService,
        IDialogService dialogService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;
        this.fileExportService = fileExportService;
        this.dialogService = dialogService;

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

    public bool HasMarked => MarkedCount > 0;

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;

        isInitialized = true;
        await sourceService.EnsureDefaultSourceAsync();

        ApplySavedSettings();
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);
        Sources = sources.Where(s => s.IsEnabled).ToList();

        HasMediaPermission = await permissionService.CheckMediaReadAsync();

        if (!HasMediaPermission)
        {
            Videos = new List<VideoItem>();
            var total = await indexService.CountAsync();
            await MainThread.InvokeOnMainThreadAsync(() => IndexedVideoCount = total);
            return;
        }

        _ = RefreshAsync();

        if (settingsService.NeedsReindex)
            _ = RunIndexAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await refreshLock.WaitAsync();
        try
        {
            var result = await Task.Run(async () =>
            {
                var sources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
                var normalizedSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);

                var sortKey = SelectedSortOption?.Key ?? "name";
                var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
                var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
                var videos = string.IsNullOrWhiteSpace(normalizedSourceId)
                    ? new List<VideoItem>()
                    : await indexService.QueryAsync(SearchText, normalizedSourceId, sortKey, dateFrom, dateTo)
                        .ConfigureAwait(false);
                var totalCount = await indexService.CountAsync().ConfigureAwait(false);
                var enabledSources = sources.Where(s => s.IsEnabled).ToList();

                return (sources, enabledSources, videos, totalCount, normalizedSourceId);
            });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ActiveSourceId = result.normalizedSourceId;
                Sources = result.enabledSources;
                _ = UpdateSourceStatsAsync(result.sources);
                Videos = result.videos;
                IndexedVideoCount = result.totalCount;
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

        bool hasPermission;

        try
        {
#if WINDOWS
            hasPermission = true; // Windows: no runtime media permission flow like Android/iOS
#else
            hasPermission = await permissionService.EnsureMediaReadAsync();
#endif
        }
        catch (NotImplementedException)
        {
            hasPermission = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Permission check failed: {ex}");
            hasPermission = false;
        }

        HasMediaPermission = hasPermission;

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
            indexStartingCount = await indexService.CountAsync();
            await MainThread.InvokeOnMainThreadAsync(() => IndexedVideoCount = indexStartingCount);
            var sources = (await sourceService.GetSourcesAsync()).Where(s => s.IsEnabled).ToList();
            indexLastInserted = 0;
            var progress = new Progress<IndexProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() => QueueIndexProgress(p));
            });
            indexCts?.Cancel();
            indexCts?.Dispose();
            indexCts = new CancellationTokenSource();

            await Task.Run(async () => { await indexService.IndexSourcesAsync(sources, progress, indexCts.Token); },
                indexCts.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            completed = false;
        }
        catch (Exception ex)
        {
            completed = false;
            Debug.WriteLine($"Indexing failed: {ex}");
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
    public async Task RenameAsync(VideoItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

#if WINDOWS
        var existingName = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : Path.GetFileNameWithoutExtension(item.Name);
        var suggested = BuildSuggestedName(item, existingName);
        var prompt = await dialogService.DisplayPromptAsync(
            AppResources.RenameTitle,
            AppResources.RenameMessage,
            AppResources.RenameAction,
            AppResources.CancelButton,
            AppResources.RenamePlaceholder,
            128,
            Keyboard.Text,
            suggested);

        if (string.IsNullOrWhiteSpace(prompt))
            return;

        var newBaseName = prompt.Trim();
        var extension = Path.GetExtension(item.Path);
        var finalName = Path.HasExtension(newBaseName)
            ? newBaseName
            : string.Concat(newBaseName, extension);
        var folder = Path.GetDirectoryName(item.Path) ?? string.Empty;
        var newPath = Path.Combine(folder, finalName);

        if (string.Equals(newPath, item.Path, StringComparison.OrdinalIgnoreCase))
        {
            item.Name = finalName;
            await indexService.RenameAsync(item, newPath, finalName);
            return;
        }

        if (File.Exists(newPath))
        {
            await dialogService.DisplayAlertAsync(
                AppResources.RenameFailedTitle,
                AppResources.RenameExistsMessage,
                AppResources.OkButton);
            return;
        }

        try
        {
            File.Move(item.Path, newPath);
        }
        catch
        {
            await dialogService.DisplayAlertAsync(
                AppResources.RenameFailedTitle,
                AppResources.RenameFailedMessage,
                AppResources.OkButton);
            return;
        }

        var updated = await indexService.RenameAsync(item, newPath, finalName);
        if (!updated)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.RenameFailedTitle,
                AppResources.RenameFailedMessage,
                AppResources.OkButton);
        }
#else
        await dialogService.DisplayAlertAsync(
            AppResources.RenameFailedTitle,
            AppResources.RenameNotSupportedMessage,
            AppResources.OkButton);
#endif
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
            await UpdateIndexedVideoCountAsync();
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
    public async Task SelectSourceAsync(MediaSource? source)
    {
        if (source == null || source.Id == ActiveSourceId)
            return;

        IsSourceSwitching = true;
        try
        {
            ActiveSourceId = source.Id;
            await RefreshAsync();
        }
        finally
        {
            IsSourceSwitching = false;
        }
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
        int currentVersion;
        lock (thumbnailLock)
        {
            if (thumbnailPipelineRunning)
            {
                thumbnailPipelineQueued = true;
                return;
            }

            thumbnailPipelineRunning = true;
            thumbnailPipelineQueued = false;
            currentVersion = videosVersion;
        }

        thumbCts?.Cancel();
        thumbCts?.Dispose();
        thumbCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                var ct = thumbCts.Token;
                // Generate thumbnails for the first N visible items quickly, then continue.
                var snapshot = Videos.ToList();
                var priority = snapshot.Take(80);
                var remainder = snapshot.Skip(80);

                foreach (var item in priority.Concat(remainder))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                        continue;

                    var p = await thumbnailService.EnsureThumbnailAsync(item, ct);
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (string.Equals(item.ThumbnailPath, p, StringComparison.OrdinalIgnoreCase))
                                item.ThumbnailPath = string.Empty;

                            item.ThumbnailPath = p;
                        });
                    }
                }
            }
            catch
            {
                // Ignore cancellation or retrieval failures.
            }
            finally
            {
                var shouldRestart = false;
                lock (thumbnailLock)
                {
                    thumbnailPipelineRunning = false;
                    shouldRestart = thumbnailPipelineQueued || videosVersion != currentVersion;
                    thumbnailPipelineQueued = false;
                }

                if (shouldRestart)
                    MainThread.BeginInvokeOnMainThread(StartThumbnailPipeline);
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
        var enabledSources = sources.Where(s => s.IsEnabled).ToList();
        if (enabledSources.Count == 0)
            return "";

        if (string.IsNullOrWhiteSpace(activeSourceId))
            return enabledSources[0].Id;

        var exists = enabledSources.Any(s => s.Id == activeSourceId);
        return exists ? activeSourceId : enabledSources[0].Id;
    }

    partial void OnVideosChanged(List<VideoItem> value)
    {
        VideoCount = value?.Count ?? 0;
        TimelineEntries = BuildTimelineEntries(value);
        SubscribeToMarkedChanges(value);
        UpdateMarkedCount();
        videosVersion++;
        StartThumbnailPipeline();
    }

    partial void OnMarkedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMarked));
    }

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
            foreach (var video in subscribedVideos)
                video.PropertyChanged -= videoMarkedHandler;

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
        IndexedVideoCount = indexStartingCount + progress.Inserted;

        if (progress.Inserted > indexLastInserted)
        {
            indexLastInserted = progress.Inserted;
            _ = RefreshAsync();
        }
    }

    private async Task UpdateIndexedVideoCountAsync()
    {
        var total = await indexService.CountAsync();
        await MainThread.InvokeOnMainThreadAsync(() => IndexedVideoCount = total);
    }

    private static string BuildSuggestedName(VideoItem item, string fallbackName)
    {
        var date = item.DateAddedSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(item.DateAddedSeconds).ToLocalTime().DateTime
            : DateTime.Now;
        var datePart = date.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = string.IsNullOrWhiteSpace(fallbackName) ? "video" : fallbackName.Trim();
        return $"{datePart}-{baseName}";
    }
}
