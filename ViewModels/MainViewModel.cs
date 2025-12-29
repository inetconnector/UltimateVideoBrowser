using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
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

    private readonly PropertyChangedEventHandler mediaMarkedHandler;
    private const int PageSize = 60;
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private DateTime dateFilterFrom = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime dateFilterTo = DateTime.Today;
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;
    [ObservableProperty] private string? currentMediaSource;
    [ObservableProperty] private string currentMediaName = "";
    [ObservableProperty] private MediaType currentMediaType;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    [ObservableProperty] private bool isPlayerFullscreen;

    private CancellationTokenSource? indexCts;
    [ObservableProperty] private string indexCurrentFile = "";
    [ObservableProperty] private string indexCurrentFolder = "";
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexedMediaCount;
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
    private List<MediaItem> subscribedMediaItems = new();
    private CancellationTokenSource? thumbCts;
    private bool thumbnailPipelineQueued;
    private bool thumbnailPipelineRunning;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();
    [ObservableProperty] private int totalSourceCount;
    [ObservableProperty] private int mediaCount;
    [ObservableProperty] private bool isSourceSwitching;
    [ObservableProperty] private bool allowFileChanges;

    [ObservableProperty] private List<MediaItem> mediaItems = new();
    private bool hasMoreMediaItems;
    private bool isLoadingMoreMediaItems;
    private int mediaItemsOffset;
    private int mediaQueryVersion;
    private int mediaItemsVersion;

    [ObservableProperty] private MediaType selectedMediaTypes = MediaType.All;
    [ObservableProperty] private bool isRefreshing;

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

        mediaMarkedHandler = OnMediaPropertyChanged;
        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };
        SelectedSortOption = SortOptions.FirstOrDefault();

        MediaTypeFilters = new[]
        {
            new MediaTypeFilterOption(MediaType.Videos, AppResources.MediaTypeVideos),
            new MediaTypeFilterOption(MediaType.Photos, AppResources.MediaTypePhotos),
            new MediaTypeFilterOption(MediaType.Documents, AppResources.MediaTypeDocuments)
        };
    }

    public IReadOnlyList<SortOption> SortOptions { get; }
    public IReadOnlyList<MediaTypeFilterOption> MediaTypeFilters { get; }

    public bool HasMarked => MarkedCount > 0;

    public bool ShowVideoPlayer => IsInternalPlayerEnabled && CurrentMediaType == MediaType.Videos
                                   && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPhotoPreview => CurrentMediaType == MediaType.Photos
                                    && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowDocumentPreview => CurrentMediaType == MediaType.Documents
                                       && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPreview => ShowVideoPlayer || ShowPhotoPreview || ShowDocumentPreview;

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
            MediaItems = new List<MediaItem>();
            mediaItemsOffset = 0;
            hasMoreMediaItems = false;
            var total = await indexService.CountAsync(SelectedMediaTypes);
            await MainThread.InvokeOnMainThreadAsync(() => IndexedMediaCount = total);
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
            IsRefreshing = true;
            var refreshVersion = Interlocked.Increment(ref mediaQueryVersion);
            var result = await Task.Run(async () =>
            {
                var sources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
                var normalizedSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);

                var sortKey = SelectedSortOption?.Key ?? "name";
                var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
                var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
                var items = string.IsNullOrWhiteSpace(normalizedSourceId)
                    ? new List<MediaItem>()
                    : await indexService.QueryPageAsync(SearchText, normalizedSourceId, sortKey, dateFrom, dateTo,
                            SelectedMediaTypes, 0, PageSize)
                        .ConfigureAwait(false);
                var totalCount = await indexService.CountAsync(SelectedMediaTypes).ConfigureAwait(false);
                var enabledSources = sources.Where(s => s.IsEnabled).ToList();

                return (sources, enabledSources, items, totalCount, normalizedSourceId, refreshVersion);
            });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (result.refreshVersion != mediaQueryVersion)
                    return;

                ActiveSourceId = result.normalizedSourceId;
                Sources = result.enabledSources;
                _ = UpdateSourceStatsAsync(result.sources);
                MediaItems = result.items;
                IndexedMediaCount = result.totalCount;
                mediaItemsOffset = result.items.Count;
                hasMoreMediaItems = result.items.Count == PageSize;
                isLoadingMoreMediaItems = false;
            });
        }
        finally
        {
            IsRefreshing = false;
            refreshLock.Release();
        }
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (isLoadingMoreMediaItems || !hasMoreMediaItems || string.IsNullOrWhiteSpace(ActiveSourceId))
            return;

        if (refreshLock.CurrentCount == 0)
            return;

        isLoadingMoreMediaItems = true;
        var queryVersion = mediaQueryVersion;

        try
        {
            var sortKey = SelectedSortOption?.Key ?? "name";
            var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
            var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
            var nextItems = await indexService.QueryPageAsync(SearchText, ActiveSourceId, sortKey, dateFrom, dateTo,
                    SelectedMediaTypes, mediaItemsOffset, PageSize)
                .ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (queryVersion != mediaQueryVersion)
                    return;

                MediaItems = MediaItems.Concat(nextItems).ToList();
                mediaItemsOffset += nextItems.Count;
                hasMoreMediaItems = nextItems.Count == PageSize;
            });
        }
        finally
        {
            isLoadingMoreMediaItems = false;
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
            indexStartingCount = await indexService.CountAsync(SelectedMediaTypes);
            await MainThread.InvokeOnMainThreadAsync(() => IndexedMediaCount = indexStartingCount);
            var sources = (await sourceService.GetSourcesAsync()).Where(s => s.IsEnabled).ToList();
            indexLastInserted = 0;
            var progress = new Progress<IndexProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() => QueueIndexProgress(p));
            });
            indexCts?.Cancel();
            indexCts?.Dispose();
            indexCts = new CancellationTokenSource();

            var indexedTypes = settingsService.IndexedMediaTypes;
            await Task.Run(async () =>
            {
                await indexService.IndexSourcesAsync(sources, indexedTypes, progress, indexCts.Token);
            },
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
    public void Play(MediaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        CurrentMediaSource = item.Path;
        CurrentMediaName = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileName(item.Path)
            : item.Name;
        CurrentMediaType = item.MediaType;
        IsPlayerFullscreen = false;

        if (item.MediaType == MediaType.Videos && settingsService.InternalPlayerEnabled)
            return;

        playbackService.Open(item);
    }

    [RelayCommand]
    public void TogglePlayerFullscreen()
    {
        if (!ShowVideoPlayer)
            return;

        IsPlayerFullscreen = !IsPlayerFullscreen;
    }

    [RelayCommand]
    public void ToggleMediaTypeFilter(MediaType mediaType)
    {
        if (mediaType == MediaType.None)
            return;

        var updated = SelectedMediaTypes;
        if (updated.HasFlag(mediaType))
            updated &= ~mediaType;
        else
            updated |= mediaType;

        if (updated == MediaType.None)
            return;

        SelectedMediaTypes = updated;
    }

    [RelayCommand]
    public async Task ShareAsync(MediaItem item)
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
    public Task SaveAsAsync(MediaItem item)
    {
        if (!AllowFileChanges)
            return Task.CompletedTask;

        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return Task.CompletedTask;

        return fileExportService.SaveAsAsync(item);
    }

    [RelayCommand]
    public async Task RenameAsync(MediaItem item)
    {
        if (!AllowFileChanges)
            return;

        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

#if WINDOWS
        var existingName = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : Path.GetFileNameWithoutExtension(item.Name);
        var currentName = Path.GetFileName(item.Path);
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

        var confirm = await dialogService.DisplayAlertAsync(
            AppResources.RenameConfirmTitle,
            string.Format(AppResources.RenameConfirmMessage, currentName, finalName),
            AppResources.RenameAction,
            AppResources.CancelButton);
        if (!confirm)
            return;

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
    public async Task OpenFolderAsync(MediaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        var folder = Path.GetDirectoryName(item.Path);
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
#if WINDOWS
            var arguments = $"/select,\"{item.Path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true
            });
#else
            if (!await Launcher.Default.IsSupportedAsync())
            {
                await dialogService.DisplayAlertAsync(
                    AppResources.OpenFolderFailedTitle,
                    AppResources.OpenFolderNotSupportedMessage,
                    AppResources.OkButton);
                return;
            }

            var uri = new Uri($"file://{folder}");
            await Launcher.OpenAsync(uri);
#endif
        }
        catch (NotImplementedException)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.OpenFolderFailedTitle,
                AppResources.OpenFolderNotSupportedMessage,
                AppResources.OkButton);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open folder failed: {ex}");
            await dialogService.DisplayAlertAsync(
                AppResources.OpenFolderFailedTitle,
                AppResources.OpenFolderFailedMessage,
                AppResources.OkButton);
        }
    }

    [RelayCommand]
    public async Task CopyMarkedAsync()
    {
        if (!AllowFileChanges)
            return;

        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        await fileExportService.CopyToFolderAsync(markedItems);
    }

    [RelayCommand]
    public async Task MoveMarkedAsync()
    {
        if (!AllowFileChanges)
            return;

        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        var moved = await fileExportService.MoveToFolderAsync(markedItems);
        if (moved.Count > 0)
        {
            await indexService.RemoveAsync(moved);
            MediaItems = MediaItems.Except(moved).ToList();
            await UpdateIndexedMediaCountAsync();
        }
        else
        {
            UpdateMarkedCount();
        }
    }

    [RelayCommand]
    public async Task DeleteMarkedAsync()
    {
        if (!AllowFileChanges)
            return;

        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        var confirm = await dialogService.DisplayAlertAsync(
            AppResources.DeleteConfirmTitle,
            string.Format(AppResources.DeleteConfirmMessageFormat, markedItems.Count),
            AppResources.DeleteMarkedAction,
            AppResources.CancelButton);
        if (!confirm)
            return;

        var deleted = await fileExportService.DeletePermanentlyAsync(markedItems);
        if (deleted.Count > 0)
        {
            await indexService.RemoveAsync(deleted);
            MediaItems = MediaItems.Except(deleted).ToList();
            await UpdateIndexedMediaCountAsync();

            if (deleted.Any(item => string.Equals(item.Path, CurrentMediaSource, StringComparison.OrdinalIgnoreCase)))
                ClearPlayerState();
        }
        else
        {
            UpdateMarkedCount();
        }
    }

    [RelayCommand]
    public void ClearMarked()
    {
        foreach (var item in MediaItems.Where(v => v.IsMarked))
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
            currentVersion = mediaItemsVersion;
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
                var snapshot = MediaItems.ToList();
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
                    shouldRestart = thumbnailPipelineQueued || mediaItemsVersion != currentVersion;
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
        var visibleTypes = settingsService.VisibleMediaTypes;
        SelectedMediaTypes = visibleTypes == MediaType.None ? MediaType.All : visibleTypes;
        ApplyPlaybackSettings();
        ApplyFileChangeSettings();
    }

    public void ApplyPlaybackSettings()
    {
        IsInternalPlayerEnabled = settingsService.InternalPlayerEnabled;
        if (!IsInternalPlayerEnabled)
            ClearPlayerState();
    }

    public void ApplyFileChangeSettings()
    {
        AllowFileChanges = settingsService.AllowFileChanges;
    }

    private void ClearPlayerState()
    {
        CurrentMediaSource = null;
        CurrentMediaName = "";
        CurrentMediaType = MediaType.None;
        IsPlayerFullscreen = false;
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

    partial void OnMediaItemsChanged(List<MediaItem> value)
    {
        MediaCount = value?.Count ?? 0;
        mediaItemsVersion++;

        var snapshot = value?.ToList() ?? new List<MediaItem>();
        var currentVersion = mediaItemsVersion;

        _ = Task.Run(() =>
        {
            var timelineEntries = BuildTimelineEntries(snapshot);
            var markedCount = snapshot.Count(v => v.IsMarked);
            return (timelineEntries, markedCount);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mediaItemsVersion != currentVersion)
                    return;

                TimelineEntries = task.Result.timelineEntries;
                MarkedCount = task.Result.markedCount;
                SubscribeToMarkedChanges(snapshot);
                StartThumbnailPipeline();
            });
        });
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

    partial void OnSelectedMediaTypesChanged(MediaType value)
    {
        if (value == MediaType.None)
            return;

        settingsService.VisibleMediaTypes = value;
        _ = RefreshAsync();
    }

    private static List<TimelineEntry> BuildTimelineEntries(List<MediaItem>? items)
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

    private void SubscribeToMarkedChanges(List<MediaItem>? items)
    {
        if (subscribedMediaItems.Count > 0)
            foreach (var mediaItem in subscribedMediaItems)
                mediaItem.PropertyChanged -= mediaMarkedHandler;

        subscribedMediaItems = items ?? new List<MediaItem>();

        foreach (var mediaItem in subscribedMediaItems)
            mediaItem.PropertyChanged += mediaMarkedHandler;
    }

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaItem.IsMarked))
            UpdateMarkedCount();
    }

    private void UpdateMarkedCount()
    {
        MarkedCount = MediaItems?.Count(v => v.IsMarked) ?? 0;
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
        IndexedMediaCount = indexStartingCount + progress.Inserted;

        if (progress.Inserted > indexLastInserted)
        {
            indexLastInserted = progress.Inserted;
            _ = RefreshAsync();
        }
    }

    private async Task UpdateIndexedMediaCountAsync()
    {
        var total = await indexService.CountAsync(SelectedMediaTypes);
        await MainThread.InvokeOnMainThreadAsync(() => IndexedMediaCount = total);
    }

    private static string BuildSuggestedName(MediaItem item, string fallbackName)
    {
        var date = item.DateAddedSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(item.DateAddedSeconds).ToLocalTime().DateTime
            : DateTime.Now;
        var datePart = date.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var baseName = string.IsNullOrWhiteSpace(fallbackName) ? "media" : fallbackName.Trim();
        if (baseName.StartsWith($"{datePart}-", StringComparison.OrdinalIgnoreCase))
            return baseName;
        return $"{datePart}-{baseName}";
    }

    public sealed record MediaTypeFilterOption(MediaType MediaType, string Label);
}
