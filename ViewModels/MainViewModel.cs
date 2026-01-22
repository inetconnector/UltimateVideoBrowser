using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Collections;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;
#if WINDOWS
using Windows.Storage;
using WinClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinDataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
#endif

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int PageSize = 60;
    private const int ThumbUiFlushBatchSize = 160;
    private readonly AlbumService albumService;
    private readonly IDialogService dialogService;
    private readonly FaceScanQueueService faceScanQueueService;
    private readonly IFileExportService fileExportService;
    private readonly ImageEditService imageEditService;
    private readonly object indexProgressLock = new();
    private readonly IndexService indexService;

    private readonly PropertyChangedEventHandler mediaMarkedHandler;
    private readonly ModelFileService modelFileService;
    private readonly ConcurrentQueue<(MediaItem Item, string Path)> pendingThumbUiUpdates = new();
    private readonly PeopleRecognitionService peopleRecognitionService;
    private readonly PeopleTagService peopleTagService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly ISourceService sourceService;
    private readonly Dictionary<string, bool> showHiddenBySource = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<MediaItem> subscribedMediaItems = new();
    private readonly object thumbnailLock = new();
    private readonly ThumbnailService thumbnailService;
    [ObservableProperty] private string activeAlbumId = "";
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private List<AlbumListItem> albumTabs = new();
    [ObservableProperty] private bool allowFileChanges;
    [ObservableProperty] private string currentMediaName = "";
    [ObservableProperty] private string? currentMediaSource;
    [ObservableProperty] private MediaType currentMediaType;
    [ObservableProperty] private DateTime dateFilterFrom = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime dateFilterTo = DateTime.Today;
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;
    private bool hasMoreMediaItems;

    private CancellationTokenSource? indexCts;
    [ObservableProperty] private string indexCurrentFile = "";
    [ObservableProperty] private string indexCurrentFolder = "";
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexedMediaCount;
    private int indexLastInserted;
    private int indexLastLiveRefreshInserted;
    private long indexLastLiveRefreshMs;
    private int indexLastLocationsRefreshDone;
    private long indexLastLocationsRefreshMs;
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    private int indexStartingCount;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    [ObservableProperty] private string indexWorkStatus = "";
    private bool isApplyingIndexProgress;
    private bool isApplyingSavedSettings;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isIndexing;
    private bool isInitialized;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    private bool isLoadingMoreMediaItems;
    [ObservableProperty] private bool isLocationEnabled;
    private int isLocationsCountRefreshRunning;
    [ObservableProperty] private bool isPeopleTaggingEnabled;
    [ObservableProperty] private bool isPlayerFullscreen;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isSourceSwitching;
    private bool isSyncingFilterOptions;
    [ObservableProperty] private int locationsCount;
    [ObservableProperty] private int markedCount;
    [ObservableProperty] private int mediaCount;

    // Coalesce expensive derived-data rebuilds (timeline, tag summaries, etc.).
    private CancellationTokenSource? mediaDerivedCts;
    private int mediaItemsFilteredCount;
    private int mediaItemsOffset;
    private int mediaItemsVersion;
    private int mediaQueryVersion;
    private bool isApplyingShowHiddenState;
    [ObservableProperty] private bool needsReindex;
    private IndexProgress? pendingIndexProgress;
    private int pendingThumbUiFlushScheduled;

    // Best-effort background people scan after indexing so the People browser is populated automatically.
    private CancellationTokenSource? peopleAutoScanCts;
    private bool peopleAutoScanRefreshAfterRun;
    private bool peopleAutoScanRerunRequested;
    private Task? peopleAutoScanTask;
    private long peopleCountRefreshMs;
    private int peopleCountRefreshProcessed;
    private IReadOnlyList<string> excludedSourceIds = Array.Empty<string>();
    [ObservableProperty] private string peopleModelsStatusText = string.Empty;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private AlbumListItem? selectedAlbum;
    [ObservableProperty] private bool showHiddenInFolder;
    [ObservableProperty] private bool hasHiddenItemsInFolder;

    [ObservableProperty] private MediaType selectedMediaTypes = MediaType.All;
    [ObservableProperty] private SearchScope selectedSearchScope = SearchScope.All;
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private MediaSource? selectedSource;
    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private string sourcesSummary = "";
    [ObservableProperty] private int taggedPeopleCount;
    private CancellationTokenSource? thumbCts;
    private bool thumbnailPipelineQueued;
    private bool thumbnailPipelineRunning;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();
    [ObservableProperty] private int totalSourceCount;

    // Visible range is reported by the view (Scrolled event) so thumbnail work can prioritize what's on screen.
    private int visibleFirstIndex;
    private int visibleLastIndex;

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        ImageEditService imageEditService,
        PlaybackService playbackService,
        PermissionService permissionService,
        IFileExportService fileExportService,
        IDialogService dialogService,
        AlbumService albumService,
        PeopleTagService peopleTagService,
        ModelFileService modelFileService,
        PeopleRecognitionService peopleRecognitionService,
        FaceScanQueueService faceScanQueueService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        SettingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.imageEditService = imageEditService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;
        this.fileExportService = fileExportService;
        this.dialogService = dialogService;
        this.albumService = albumService;
        this.peopleTagService = peopleTagService;
        this.modelFileService = modelFileService;
        this.peopleRecognitionService = peopleRecognitionService;
        this.faceScanQueueService = faceScanQueueService;

        mediaMarkedHandler = OnMediaPropertyChanged;
        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };
        // Default to date sorting so the timeline and date-based navigation are consistent.
        SelectedSortOption = SortOptions.FirstOrDefault(o => o.Key == "date") ?? SortOptions.FirstOrDefault();

        MediaTypeFilters = new[]
        {
            new MediaTypeFilterOption(MediaType.Videos, AppResources.MediaTypeVideos),
            new MediaTypeFilterOption(MediaType.Photos, AppResources.MediaTypePhotos),
            new MediaTypeFilterOption(MediaType.Graphics, AppResources.MediaTypeGraphics),
            new MediaTypeFilterOption(MediaType.Documents, AppResources.MediaTypeDocuments)
        };

        SearchScopeFilters = new[]
        {
            new SearchScopeFilterOption(SearchScope.Name, AppResources.SearchScopeName),
            new SearchScopeFilterOption(SearchScope.People, AppResources.SearchScopePeople),
            new SearchScopeFilterOption(SearchScope.Albums, AppResources.SearchScopeAlbums)
        };

        foreach (var option in MediaTypeFilters)
            option.PropertyChanged += HandleMediaTypeFilterOptionChanged;
        foreach (var option in SearchScopeFilters)
            option.PropertyChanged += HandleSearchScopeFilterOptionChanged;

        SyncMediaTypeFilterSelections();
        SyncSearchScopeFilterSelections();

        MediaItems.CollectionChanged += OnMediaItemsCollectionChanged;

        RefreshPeopleModelsStatus();
        settingsService.NeedsReindexChanged += HandleNeedsReindexChanged;
    }

    public bool HasIndexWorkStatus => !string.IsNullOrWhiteSpace(IndexWorkStatus);
    public bool HasLocations => LocationsCount > 0;

    internal AppSettingsService SettingsService { get; }

    public IReadOnlyList<SortOption> SortOptions { get; }
    public IReadOnlyList<MediaTypeFilterOption> MediaTypeFilters { get; }
    public IReadOnlyList<SearchScopeFilterOption> SearchScopeFilters { get; }
    public bool HasAlbums => AlbumTabs.Any(tab => !tab.IsAll);
    public bool HasMultipleSources => EnabledSourceCount > 1;

    public ObservableRangeCollection<MediaItem> MediaItems { get; } = new();

    public bool HasMarked => MarkedCount > 0;

    public bool ShowHiddenToggleVisible => HasHiddenItemsInFolder || ShowHiddenInFolder;

    public bool ShowBottomDock => IsInternalPlayerEnabled || HasMarked;

    public bool ShowVideoPlayer => IsInternalPlayerEnabled && CurrentMediaType == MediaType.Videos
                                                           && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPhotoPreview => CurrentMediaType is MediaType.Photos or MediaType.Graphics
                                    && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowDocumentPreview => CurrentMediaType == MediaType.Documents
                                       && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPreview => ShowVideoPlayer || ShowPhotoPreview || ShowDocumentPreview;

    public IndexingState IndexState =>
        IsIndexing ? IndexingState.Running :
        NeedsReindex ? IndexingState.NeedsReindex :
        IndexingState.Ready;

    partial void OnIndexWorkStatusChanged(string value)
    {
        OnPropertyChanged(nameof(HasIndexWorkStatus));
    }

    partial void OnLocationsCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasLocations));
    }

    public event EventHandler? ProUpgradeRequested;

    /// <summary>
    ///     Raised (throttled) during indexing when new items were inserted so the UI can refresh in the background.
    ///     The view is responsible for preserving the current scroll position and selection.
    /// </summary>
    public event EventHandler? IndexLiveRefreshSuggested;

    private void HandleNeedsReindexChanged(object? sender, bool needsReindex)
    {
        if (!needsReindex || IsIndexing)
        {
            NeedsReindex = needsReindex;
            return;
        }

        if (!isInitialized)
        {
            _ = InitializeAsync();
            return;
        }

        NeedsReindex = needsReindex;
        _ = RunIndexAsync();
    }

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;

        isInitialized = true;
        ReloadSettingsFromService();

        var activeSourceId = ActiveSourceId;
        var selectedMediaTypes = SelectedMediaTypes;

        var initResult = await Task.Run(async () =>
        {
            await sourceService.EnsureDefaultSourceAsync().ConfigureAwait(false);

            var sources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
            var filteredSources = FilterAndroidChildSourcesIfNeeded(sources, out var sourceExclusions);
            var normalizedSourceId = NormalizeActiveSourceId(filteredSources, activeSourceId);
            var enabledSources = filteredSources.Where(s => s.IsEnabled).ToList();
            var hasPermission = await permissionService.CheckMediaReadAsync().ConfigureAwait(false);

            var total = await indexService.CountAsync(selectedMediaTypes, sourceExclusions).ConfigureAwait(false);

            return (filteredSources, enabledSources, normalizedSourceId, hasPermission, total, sourceExclusions);
        }).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ActiveSourceId = initResult.normalizedSourceId;
            Sources = BuildSourceTabs(initResult.enabledSources);
            _ = UpdateSourceStatsAsync(initResult.sources);
            HasMediaPermission = initResult.hasPermission;
            excludedSourceIds = initResult.sourceExclusions;
        });

        if (!initResult.hasPermission)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                MediaItems.Clear();
                mediaItemsOffset = 0;
                hasMoreMediaItems = false;
                IndexedMediaCount = initResult.total;
            });
            return;
        }

        _ = RefreshAsync();
        _ = RefreshTaggedPeopleCountAsync();
        _ = RefreshLocationsCountAsync();

        if (SettingsService.NeedsReindex)
            _ = RunIndexAsync();
    }

    /// <summary>
    ///     Call this from the MainPage when the page becomes visible again (e.g. after closing Settings/Sources).
    ///     It re-applies persisted settings and refreshes the visible data so the UI does not show stale sources/items.
    /// </summary>
    public async Task OnMainPageAppearingAsync()
    {
        if (!isInitialized)
        {
            await InitializeAsync().ConfigureAwait(false);
            return;
        }

        ReloadSettingsFromService();
        await RefreshAsync().ConfigureAwait(false);
        _ = RefreshTaggedPeopleCountAsync();
        _ = RefreshLocationsCountAsync();
    }

    public async Task<bool> TryShowPeopleTaggingTrialHintAsync()
    {
        if (SettingsService.IsProUnlocked)
            return false;
        if (!SettingsService.PeopleTaggingEnabled)
            return false;
        if (SettingsService.PeopleTaggingTrialHintShown)
            return false;

        SettingsService.PeopleTaggingTrialHintShown = true;

        var msg = AppResources.PeopleTagsTrialHint;
        var ok = AppResources.OkButton;
        var upgrade = AppResources.UpgradeNowButton;

        var goUpgrade = await dialogService.DisplayAlertAsync(
            AppResources.PeopleTagsTrialTitle,
            msg,
            upgrade,
            ok).ConfigureAwait(false);

        await TryPromptLocationOptInAsync().ConfigureAwait(false);

        if (goUpgrade)
            ProUpgradeRequested?.Invoke(this, EventArgs.Empty);

        return true;
    }

    internal async Task TryPromptLocationOptInAsync()
    {
        if (SettingsService.LocationsEnabled)
            return;

        var accepted = await dialogService.DisplayAlertAsync(
            AppResources.LocationOptInTitle,
            AppResources.LocationOptInMessage,
            AppResources.LocationOptInAccept,
            AppResources.LocationOptInDecline).ConfigureAwait(false);

        if (!accepted)
            return;

        SettingsService.LocationsEnabled = true;
        IsLocationEnabled = true;
        if (!SettingsService.NeedsReindex)
            SettingsService.NeedsReindex = true;
    }

    public async Task<MediaItem?> EnsureMediaItemLoadedAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var target = FindMediaItem(path);
        var lastCount = -1;

        while (target == null && hasMoreMediaItems && !cancellationToken.IsCancellationRequested)
        {
            var count = MediaItems.Count;
            if (count == lastCount)
                break;

            lastCount = count;
            await LoadMoreAsync().ConfigureAwait(false);
            target = FindMediaItem(path);
        }

        return target;
    }

    private MediaItem? FindMediaItem(string path)
    {
        return MediaItems.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await refreshLock.WaitAsync();
        try
        {
            IsRefreshing = true;
            var refreshVersion = Interlocked.Increment(ref mediaQueryVersion);
            try
            {
                const int initialLoadTarget = PageSize * 3;
                var result = await Task.Run(async () =>
                {
                    var sources = await sourceService.GetSourcesAsync().ConfigureAwait(false);
                    var filteredSources = FilterAndroidChildSourcesIfNeeded(sources, out var sourceExclusions);
                    var normalizedSourceId = NormalizeActiveSourceId(filteredSources, ActiveSourceId);
                    var albumSummaries = await albumService.GetAlbumSummariesAsync().ConfigureAwait(false);
                    var normalizedAlbumId = NormalizeActiveAlbumId(albumSummaries, ActiveAlbumId);
                    var albumTabs = BuildAlbumTabs(albumSummaries);

                    var sortKey = SelectedSortOption?.Key ?? "name";
                    var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
                    var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
                    var hideDuplicates = SettingsService.HideDuplicateFilesEnabled &&
                                         string.IsNullOrWhiteSpace(normalizedAlbumId);
                    var includeHidden = ShowHiddenInFolder && !string.IsNullOrWhiteSpace(normalizedSourceId);

                    // IMPORTANT:
                    // An empty source id is treated as "all sources" (no SourceId filter).
                    // Previously we returned an empty list, which made the UI look broken
                    // when the active source wasn't set or when sources were temporarily unavailable.
                    var querySourceId = string.IsNullOrWhiteSpace(normalizedSourceId) ? null : normalizedSourceId;
                    var items = string.IsNullOrWhiteSpace(normalizedAlbumId)
                        ? hideDuplicates
                            ? await indexService.QueryPageUniqueOldestAsync(SearchText, SelectedSearchScope,
                                    querySourceId, sortKey, dateFrom, dateTo, SelectedMediaTypes, 0, PageSize,
                                    includeHidden, sourceExclusions)
                                .ConfigureAwait(false)
                            : await indexService.QueryPageAsync(SearchText, SelectedSearchScope, querySourceId, sortKey,
                                    dateFrom, dateTo, SelectedMediaTypes, 0, PageSize, includeHidden, sourceExclusions)
                                .ConfigureAwait(false)
                        : await albumService.QueryAlbumPageAsync(normalizedAlbumId, SearchText, SelectedSearchScope,
                                querySourceId, sortKey, dateFrom, dateTo, SelectedMediaTypes, 0, PageSize,
                                includeHidden, sourceExclusions)
                            .ConfigureAwait(false);
                    var filteredCount = string.IsNullOrWhiteSpace(normalizedAlbumId)
                        ? hideDuplicates
                            ? await indexService
                                .CountQueryUniqueAsync(SearchText, SelectedSearchScope, querySourceId, dateFrom, dateTo,
                                    SelectedMediaTypes, includeHidden, sourceExclusions)
                                .ConfigureAwait(false)
                            : await indexService
                                .CountQueryAsync(SearchText, SelectedSearchScope, querySourceId, dateFrom, dateTo,
                                    SelectedMediaTypes, includeHidden, sourceExclusions)
                                .ConfigureAwait(false)
                        : await albumService
                            .CountAlbumItemsAsync(normalizedAlbumId, SearchText, SelectedSearchScope, querySourceId,
                                dateFrom, dateTo, SelectedMediaTypes, includeHidden, sourceExclusions)
                            .ConfigureAwait(false);

                    if (items.Count < filteredCount && items.Count < initialLoadTarget)
                    {
                        var expanded = new List<MediaItem>(items);
                        var offset = expanded.Count;
                        while (offset < filteredCount && expanded.Count < initialLoadTarget)
                        {
                            var nextItems = string.IsNullOrWhiteSpace(normalizedAlbumId)
                                ? hideDuplicates
                                    ? await indexService.QueryPageUniqueOldestAsync(SearchText, SelectedSearchScope,
                                            querySourceId, sortKey, dateFrom, dateTo, SelectedMediaTypes, offset,
                                            PageSize, includeHidden, sourceExclusions)
                                        .ConfigureAwait(false)
                                    : await indexService.QueryPageAsync(SearchText, SelectedSearchScope, querySourceId,
                                            sortKey, dateFrom, dateTo, SelectedMediaTypes, offset, PageSize,
                                            includeHidden, sourceExclusions)
                                        .ConfigureAwait(false)
                                : await albumService.QueryAlbumPageAsync(normalizedAlbumId, SearchText,
                                        SelectedSearchScope, querySourceId, sortKey, dateFrom, dateTo,
                                        SelectedMediaTypes, offset, PageSize, includeHidden, sourceExclusions)
                                    .ConfigureAwait(false);

                            if (nextItems.Count == 0)
                                break;

                            expanded.AddRange(nextItems);
                            offset += nextItems.Count;
                            if (nextItems.Count < PageSize)
                                break;
                        }

                        items = expanded;
                    }

                    var totalCount =
                        await indexService.CountAsync(SelectedMediaTypes, sourceExclusions).ConfigureAwait(false);
                    var enabledSources = filteredSources.Where(s => s.IsEnabled).ToList();
                    var hiddenCount = string.IsNullOrWhiteSpace(querySourceId)
                        ? 0
                        : string.IsNullOrWhiteSpace(normalizedAlbumId)
                            ? hideDuplicates
                                ? await indexService.CountHiddenUniqueAsync(SearchText, SelectedSearchScope,
                                        querySourceId, dateFrom, dateTo, SelectedMediaTypes, sourceExclusions)
                                    .ConfigureAwait(false)
                                : await indexService.CountHiddenAsync(SearchText, SelectedSearchScope, querySourceId,
                                        dateFrom, dateTo, SelectedMediaTypes, sourceExclusions)
                                    .ConfigureAwait(false)
                            : await albumService.CountHiddenAlbumItemsAsync(normalizedAlbumId, SearchText,
                                    SelectedSearchScope, querySourceId, dateFrom, dateTo, SelectedMediaTypes,
                                    sourceExclusions)
                                .ConfigureAwait(false);

                    return (filteredSources, enabledSources, items, totalCount, filteredCount, normalizedSourceId,
                        normalizedAlbumId, albumTabs, refreshVersion, hiddenCount, sourceExclusions);
                });

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (result.refreshVersion != mediaQueryVersion)
                        return;

                    ActiveSourceId = result.normalizedSourceId;
                    ActiveAlbumId = result.normalizedAlbumId;
                    Sources = BuildSourceTabs(result.enabledSources);
                    AlbumTabs = result.albumTabs;
                    _ = UpdateSourceStatsAsync(result.sources);
                    MediaItems.ReplaceRange(result.items);
                    IndexedMediaCount = result.totalCount;
                    mediaItemsOffset = result.items.Count;
                    mediaItemsFilteredCount = result.filteredCount;
                    hasMoreMediaItems = result.items.Count < result.filteredCount;
                    isLoadingMoreMediaItems = false;
                    HasHiddenItemsInFolder = result.hiddenCount > 0;
                    excludedSourceIds = result.sourceExclusions;
                });

                _ = RefreshTaggedPeopleCountAsync();
                _ = RefreshLocationsCountAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "MainViewModel.RefreshAsync");
            }
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
        if (isLoadingMoreMediaItems || !hasMoreMediaItems)
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
            var querySourceId = string.IsNullOrWhiteSpace(ActiveSourceId) ? null : ActiveSourceId;
            var hideDuplicates = SettingsService.HideDuplicateFilesEnabled && string.IsNullOrWhiteSpace(ActiveAlbumId);
            var includeHidden = ShowHiddenInFolder && !string.IsNullOrWhiteSpace(querySourceId);
            var nextItems = string.IsNullOrWhiteSpace(ActiveAlbumId)
                ? hideDuplicates
                    ? await indexService.QueryPageUniqueOldestAsync(SearchText, SelectedSearchScope, querySourceId,
                            sortKey, dateFrom, dateTo, SelectedMediaTypes, mediaItemsOffset, PageSize, includeHidden,
                            excludedSourceIds)
                        .ConfigureAwait(false)
                    : await indexService.QueryPageAsync(SearchText, SelectedSearchScope, querySourceId, sortKey,
                            dateFrom, dateTo, SelectedMediaTypes, mediaItemsOffset, PageSize, includeHidden,
                            excludedSourceIds)
                        .ConfigureAwait(false)
                : await albumService.QueryAlbumPageAsync(ActiveAlbumId, SearchText, SelectedSearchScope, querySourceId,
                        sortKey, dateFrom, dateTo, SelectedMediaTypes, mediaItemsOffset, PageSize, includeHidden,
                        excludedSourceIds)
                    .ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (queryVersion != mediaQueryVersion)
                    return;

                MediaItems.AddRange(nextItems);
                mediaItemsOffset += nextItems.Count;
                hasMoreMediaItems = mediaItemsOffset < mediaItemsFilteredCount;
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
        IndexWorkStatus = "";
        IndexCurrentFolder = "";
        IndexCurrentFile = "";
        indexLastLocationsRefreshDone = 0;
        indexLastLocationsRefreshMs = 0;

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
            var openSettings = await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current?.DisplayAlertAsync(
                    AppResources.PermissionTitle,
                    AppResources.PermissionMessage,
                    AppResources.PermissionOk,
                    AppResources.OkButton) ?? Task.FromResult(false));
            if (openSettings)
                permissionService.OpenAppSettings();
            return;
        }

        if (IsPeopleTaggingEnabled)
            StartPeopleAutoScanInBackground(false, false);

        IsIndexing = true;
        IndexedCount = 0;
        IndexProcessed = 0;
        IndexTotal = 0;
        IndexRatio = 0;
        IndexStatus = AppResources.Indexing;
        IndexWorkStatus = "";
        IndexCurrentFolder = "";
        IndexCurrentFile = "";

        var completed = false;
        try
        {
            var sources = await sourceService.GetSourcesAsync();
            var filteredSources = FilterAndroidChildSourcesIfNeeded(sources, out var sourceExclusions);
            indexStartingCount = await indexService.CountAsync(SelectedMediaTypes, sourceExclusions);
            await MainThread.InvokeOnMainThreadAsync(() => IndexedMediaCount = indexStartingCount);
            var enabledSources = filteredSources.Where(s => s.IsEnabled).ToList();
            indexLastInserted = 0;
            var progress = new Progress<IndexProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() => QueueIndexProgress(p));
            });
            indexCts?.Cancel();
            indexCts?.Dispose();
            indexCts = new CancellationTokenSource();

            var indexedTypes = SettingsService.IndexedMediaTypes;
            await Task.Run(
                async () =>
                {
                    await indexService.IndexSourcesAsync(enabledSources, indexedTypes, progress, indexCts.Token);
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
            SettingsService.NeedsReindex = false;
        else
            SettingsService.NeedsReindex = true;

        await RefreshAsync();

        // Kick off automatic people recognition after indexing so the People browser is populated.
        // This is best-effort and runs in the background to keep the UI responsive.
        if (completed && IsPeopleTaggingEnabled)
            StartPeopleAutoScanInBackground(true, true);
    }

    private void StartPeopleAutoScanInBackground(bool refreshMediaAfterScan = true, bool rerunIfBusy = false)
    {
        // Avoid starting multiple concurrent scans.
        if (peopleAutoScanTask != null && !peopleAutoScanTask.IsCompleted)
        {
            if (rerunIfBusy)
            {
                peopleAutoScanRerunRequested = true;
                peopleAutoScanRefreshAfterRun |= refreshMediaAfterScan;
            }

            return;
        }

        peopleAutoScanCts?.Cancel();
        peopleAutoScanCts?.Dispose();
        peopleAutoScanCts = new CancellationTokenSource();
        peopleAutoScanRerunRequested = false;
        peopleAutoScanRefreshAfterRun = refreshMediaAfterScan;

        var ct = peopleAutoScanCts.Token;
        var progress = new Progress<int>(processed =>
        {
            var now = Environment.TickCount64;
            if (processed - peopleCountRefreshProcessed < 20 && now - peopleCountRefreshMs < 2000)
                return;

            peopleCountRefreshProcessed = processed;
            peopleCountRefreshMs = now;
            _ = RefreshTaggedPeopleCountAsync();
        });

        peopleAutoScanTask = Task.Run(async () =>
        {
            try
            {
                // Ensure models are present and loaded; otherwise auto-scan would silently do nothing.
                if (!modelFileService.AreAllModelsReady())
                {
                    await modelFileService.EnsureAllModelsAsync(ct).ConfigureAwait(false);
                    await MainThread.InvokeOnMainThreadAsync(RefreshPeopleModelsStatus);
                }

                if (!modelFileService.AreAllModelsReady())
                    return;

                await peopleRecognitionService.WarmupModelsAsync(ct).ConfigureAwait(false);

                var sourceId = string.IsNullOrWhiteSpace(ActiveSourceId) ? null : ActiveSourceId;
                var sortKey = SelectedSortOption?.Key ?? "date";
                DateTime? from = IsDateFilterEnabled ? DateFilterFrom : null;
                DateTime? to = IsDateFilterEnabled ? DateFilterTo : null;

                await faceScanQueueService
                    .EnqueuePhotosForScanAsync(sourceId, sortKey, from, to, ct)
                    .ConfigureAwait(false);

                await faceScanQueueService.ProcessQueueAsync(ct, progress).ConfigureAwait(false);

                _ = RefreshTaggedPeopleCountAsync();

                if (refreshMediaAfterScan && !IsIndexing)
                    // Refresh UI once after the scan to populate People/Tag counts.
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            await RefreshAsync();
                        }
                        catch
                        {
                            /* keep UI resilient */
                        }
                    });
            }
            catch
            {
                // Best-effort: never crash the app because of background scanning.
            }
            finally
            {
                if (peopleAutoScanRerunRequested && !ct.IsCancellationRequested)
                {
                    var refreshAfterRerun = peopleAutoScanRefreshAfterRun;
                    peopleAutoScanRerunRequested = false;
                    StartPeopleAutoScanInBackground(refreshAfterRerun, false);
                }
            }
        }, ct);
    }

    [RelayCommand]
    public void CancelIndex()
    {
        indexCts?.Cancel();
    }

    [RelayCommand]
    public void Select(MediaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        CurrentMediaSource = item.Path;
        CurrentMediaName = string.IsNullOrWhiteSpace(item.Name)
            ? Path.GetFileName(item.Path)
            : item.Name;
        CurrentMediaType = item.MediaType;
        IsPlayerFullscreen = false;
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

        if (item.MediaType == MediaType.Videos && SettingsService.InternalPlayerEnabled)
            return;

        playbackService.Open(item);
    }

    [RelayCommand]
    public void TogglePlayerFullscreen()
    {
        if (!ShowVideoPlayer && !IsPlayerFullscreen)
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
    public void ToggleSearchScope(SearchScope scope)
    {
        if (scope == SearchScope.None)
            return;

        var updated = SelectedSearchScope;
        if (updated.HasFlag(scope))
            updated &= ~scope;
        else
            updated |= scope;

        if (updated == SearchScope.None)
            return;

        SelectedSearchScope = updated;
    }

    private void HandleMediaTypeFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaTypeFilterOption.IsSelected) || isSyncingFilterOptions)
            return;

        var selected = MediaType.None;
        foreach (var option in MediaTypeFilters.Where(option => option.IsSelected))
            selected |= option.MediaType;

        if (selected == MediaType.None)
        {
            SyncMediaTypeFilterSelections();
            return;
        }

        SelectedMediaTypes = selected;
    }

    private void HandleSearchScopeFilterOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchScopeFilterOption.IsSelected) || isSyncingFilterOptions)
            return;

        var selected = SearchScope.None;
        foreach (var option in SearchScopeFilters.Where(option => option.IsSelected))
            selected |= option.Scope;

        if (selected == SearchScope.None)
        {
            SyncSearchScopeFilterSelections();
            return;
        }

        SelectedSearchScope = selected;
    }

    private void SyncMediaTypeFilterSelections()
    {
        isSyncingFilterOptions = true;
        try
        {
            foreach (var option in MediaTypeFilters)
                option.IsSelected = SelectedMediaTypes.HasFlag(option.MediaType);
        }
        finally
        {
            isSyncingFilterOptions = false;
        }
    }

    private void SyncSearchScopeFilterSelections()
    {
        isSyncingFilterOptions = true;
        try
        {
            foreach (var option in SearchScopeFilters)
                option.IsSelected = SelectedSearchScope.HasFlag(option.Scope);
        }
        finally
        {
            isSyncingFilterOptions = false;
        }
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
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return Task.CompletedTask;

        return fileExportService.SaveAsAsync(item);
    }

    [RelayCommand]
    public async Task SaveAsMarkedAsync()
    {
        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

#if WINDOWS
        if (markedItems.Count == 1)
        {
            await fileExportService.SaveAsAsync(markedItems[0]);
            return;
        }

        await fileExportService.CopyToFolderAsync(markedItems);
#else
        // Best-effort "Save as" on mobile: show a share sheet so the user can save into Files/Drive.
        if (markedItems.Count == 1)
        {
            await ShareAsync(markedItems[0]);
            return;
        }

        await dialogService.DisplayAlertAsync(
            AppResources.SaveAsFailedTitle,
            AppResources.SaveAsNotSupportedMessage,
            AppResources.OkButton);
#endif
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
            await dialogService.DisplayAlertAsync(
                AppResources.RenameFailedTitle,
                AppResources.RenameFailedMessage,
                AppResources.OkButton);
#else
        await dialogService.DisplayAlertAsync(
            AppResources.RenameFailedTitle,
            AppResources.RenameNotSupportedMessage,
            AppResources.OkButton);
#endif
    }

    [RelayCommand]
    public async Task TagPeopleAsync(MediaItem item)
    {
        if (!IsPeopleTaggingEnabled)
            return;

        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        var matches = await peopleRecognitionService
            .EnsurePeopleTagsForMediaAsync(item, CancellationToken.None);
        var initialValue = matches.Count > 0
            ? string.Join(", ", matches
                .OrderBy(match => match.FaceIndex)
                .Select(match => match.Name))
            : null;

        var prompt = await dialogService.DisplayPromptAsync(
            AppResources.TagPeopleTitle,
            AppResources.TagPeopleMessage,
            AppResources.TagPeopleAction,
            AppResources.CancelButton,
            AppResources.TagPeoplePlaceholder,
            256,
            Keyboard.Text,
            initialValue);

        if (prompt == null)
            return;

        var namesInOrder = prompt
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.Trim())
            .ToList();

        await peopleRecognitionService.RenamePeopleForMediaAsync(item, namesInOrder, CancellationToken.None);

        var tags = namesInOrder
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await peopleTagService.SetTagsForMediaAsync(item.Path, tags);
        _ = RefreshTaggedPeopleCountAsync();

        // Show the user-entered tags immediately on the tile, even when no faces were detected.
        // Face recognition can be disabled or fail on some photos (low resolution / side faces etc.),
        // but manual tags must always be visible once persisted.
        await MainThread.InvokeOnMainThreadAsync(() => { item.PeopleTagsSummary = string.Join(", ", tags); });

        // Best-effort: keep face DB in sync in case faces are detectable later.
        _ = peopleRecognitionService.EnsurePeopleTagsForMediaAsync(item, CancellationToken.None);

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var sourceId = string.IsNullOrWhiteSpace(item.SourceId) ? ActiveSourceId : item.SourceId;
                if (string.IsNullOrWhiteSpace(sourceId))
                    return;

                var photos = await indexService
                    .QueryAsync(string.Empty, SearchScope.All, sourceId, "name", null, null,
                        MediaType.Photos | MediaType.Graphics, true)
                    .ConfigureAwait(false);
                await peopleRecognitionService.ScanAndTagAsync(photos, null, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // best-effort background scan
            }
        });
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
            //if (!await Launcher.Default.IsSupportedAsync())
            //{
            //    await dialogService.DisplayAlertAsync(
            //        AppResources.OpenFolderFailedTitle,
            //        AppResources.OpenFolderNotSupportedMessage,
            //        AppResources.OkButton);
            //    return;
            //}

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
    public async Task OpenLocationAsync(MediaItem item)
    {
        if (item == null || !item.Latitude.HasValue || !item.Longitude.HasValue)
            return;

        var lat = item.Latitude.Value;
        var lon = item.Longitude.Value;
        var latText = lat.ToString("0.######", CultureInfo.InvariantCulture);
        var lonText = lon.ToString("0.######", CultureInfo.InvariantCulture);
        var uri = new Uri($"https://earth.google.com/web/@{latText},{lonText},0a,0d,0y,0h,0t,0r");

        try
        {
            await Launcher.OpenAsync(uri);
        }
        catch (NotImplementedException)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.OpenLocationFailedTitle,
                AppResources.OpenLocationNotSupportedMessage,
                AppResources.OkButton);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Open location failed: {ex}");
            await dialogService.DisplayAlertAsync(
                AppResources.OpenLocationFailedTitle,
                AppResources.OpenLocationFailedMessage,
                AppResources.OkButton);
        }
    }

    [RelayCommand]
    public async Task AddMarkedToAlbumAsync()
    {
        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return;

        var albums = await albumService.GetAlbumsAsync().ConfigureAwait(false);
        var choices = albums.Select(a => a.Name).ToList();
        choices.Add(AppResources.NewAlbumAction);

        var choice = await dialogService.DisplayActionSheetAsync(
            AppResources.AddToAlbumAction,
            AppResources.CancelButton,
            null,
            choices.ToArray());

        if (string.IsNullOrWhiteSpace(choice) ||
            string.Equals(choice, AppResources.CancelButton, StringComparison.Ordinal))
            return;

        Album? targetAlbum = null;

        if (string.Equals(choice, AppResources.NewAlbumAction, StringComparison.Ordinal))
        {
            var name = await dialogService.DisplayPromptAsync(
                AppResources.NewAlbumTitle,
                AppResources.NewAlbumPrompt,
                AppResources.NewAlbumConfirm,
                AppResources.CancelButton,
                AppResources.NewAlbumPlaceholder,
                80,
                Keyboard.Text);

            if (string.IsNullOrWhiteSpace(name))
                return;

            var existing = await albumService.FindByNameAsync(name).ConfigureAwait(false);
            targetAlbum = existing ?? await albumService.CreateAlbumAsync(name).ConfigureAwait(false);
        }
        else
        {
            targetAlbum = albums.FirstOrDefault(a => string.Equals(a.Name, choice, StringComparison.Ordinal));
        }

        if (targetAlbum == null)
            return;

        await albumService.AddItemsAsync(targetAlbum.Id, markedItems).ConfigureAwait(false);
        await RefreshAlbumTabsAsync().ConfigureAwait(false);

        if (string.Equals(ActiveAlbumId, targetAlbum.Id, StringComparison.Ordinal))
            await RefreshAsync().ConfigureAwait(false);
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
    public async Task CopyItemAsync(MediaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

#if WINDOWS
        try
        {
            // Copy the file itself to the OS clipboard (Explorer paste).
            var storageFile = await StorageFile.GetFileFromPathAsync(item.Path);

            var dataPackage = new WinDataPackage();
            dataPackage.RequestedOperation = WinDataPackageOperation.Copy;
            dataPackage.SetStorageItems(new[] { storageFile });

            WinClipboard.SetContent(dataPackage);
            WinClipboard.Flush();
            return;
        }
        catch
        {
            // Fall back to copying the path as text.
        }
#endif

        try
        {
            await Clipboard.Default.SetTextAsync(item.Path);
        }
        catch
        {
            // Ignore clipboard failures (e.g., clipboard not available).
        }
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
            MediaItems.RemoveRange(moved);
            await UpdateIndexedMediaCountAsync();
        }
        else
        {
            RecomputeMarkedCount();
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
            MediaItems.RemoveRange(deleted);
            await UpdateIndexedMediaCountAsync();

            if (deleted.Any(item => string.Equals(item.Path, CurrentMediaSource, StringComparison.OrdinalIgnoreCase)))
                ClearPlayerState();
        }

        RecomputeMarkedCount();
    }

    [RelayCommand]
    public async Task DeleteItemAsync(MediaItem item)
    {
        if (!AllowFileChanges)
            return;

        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        var confirm = await dialogService.DisplayAlertAsync(
            AppResources.DeleteConfirmTitle,
            string.Format(AppResources.DeleteConfirmMessageFormat, 1),
            AppResources.DeleteMarkedAction,
            AppResources.CancelButton);
        if (!confirm)
            return;

        var deleted = await fileExportService.DeletePermanentlyAsync(new[] { item });
        if (deleted.Count > 0)
        {
            await indexService.RemoveAsync(deleted);
            MediaItems.RemoveRange(deleted);
            await UpdateIndexedMediaCountAsync();

            if (deleted.Any(i => string.Equals(i.Path, CurrentMediaSource, StringComparison.OrdinalIgnoreCase)))
                ClearPlayerState();
        }

        RecomputeMarkedCount();
    }

    [RelayCommand]
    public Task HideItemAsync(MediaItem item)
    {
        if (item == null)
            return Task.CompletedTask;

        return UpdateHiddenAsync(new List<MediaItem> { item }, true);
    }

    [RelayCommand]
    public Task UnhideItemAsync(MediaItem item)
    {
        if (item == null)
            return Task.CompletedTask;

        return UpdateHiddenAsync(new List<MediaItem> { item }, false);
    }

    [RelayCommand]
    public Task HideMarkedAsync()
    {
        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return Task.CompletedTask;

        return UpdateHiddenAsync(markedItems, true);
    }

    [RelayCommand]
    public Task UnhideMarkedAsync()
    {
        var markedItems = MediaItems.Where(v => v.IsMarked).ToList();
        if (markedItems.Count == 0)
            return Task.CompletedTask;

        return UpdateHiddenAsync(markedItems, false);
    }

    [RelayCommand]
    public void ToggleShowHiddenInFolder()
    {
        ShowHiddenInFolder = !ShowHiddenInFolder;
    }


    [RelayCommand]
    public async Task OpenItemMenuAsync(MediaItem item)
    {
        if (item == null)
            return;

        var actions = new List<string>
        {
            AppResources.ShareAction,
            AppResources.CopyMarkedAction,
            item.IsHidden ? AppResources.UnhideAction : AppResources.HideAction
        };

        if (ShowHiddenToggleVisible)
            actions.Add(ShowHiddenInFolder ? AppResources.HideHiddenAction : AppResources.ShowHiddenAction);

        if (AllowFileChanges)
            actions.Add(AppResources.DeleteMarkedAction);

        var choice = await dialogService.DisplayActionSheetAsync(
            item.Name,
            AppResources.CancelButton,
            null,
            actions.ToArray());

        if (string.Equals(choice, AppResources.ShareAction, StringComparison.Ordinal))
        {
            await ShareAsync(item);
            return;
        }

        if (string.Equals(choice, AppResources.CopyMarkedAction, StringComparison.Ordinal))
        {
            await CopyItemAsync(item);
            return;
        }

        if (string.Equals(choice, AppResources.HideAction, StringComparison.Ordinal))
        {
            await HideItemAsync(item);
            return;
        }

        if (string.Equals(choice, AppResources.UnhideAction, StringComparison.Ordinal))
        {
            await UnhideItemAsync(item);
            return;
        }

        if (string.Equals(choice, AppResources.ShowHiddenAction, StringComparison.Ordinal)
            || string.Equals(choice, AppResources.HideHiddenAction, StringComparison.Ordinal))
        {
            ToggleShowHiddenInFolder();
            return;
        }

        if (string.Equals(choice, AppResources.DeleteMarkedAction, StringComparison.Ordinal))
            await DeleteItemAsync(item);
    }

    private async Task UpdateHiddenAsync(IReadOnlyList<MediaItem> items, bool isHidden)
    {
        if (items.Count == 0)
            return;

        await indexService.SetHiddenAsync(items, isHidden);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            foreach (var item in items)
                item.IsHidden = isHidden;

            if (!ShowHiddenInFolder && isHidden)
            {
                MediaItems.RemoveRange(items);
                var removedCount = items.Count;
                mediaItemsOffset = Math.Max(0, mediaItemsOffset - removedCount);
                mediaItemsFilteredCount = Math.Max(0, mediaItemsFilteredCount - removedCount);
                hasMoreMediaItems = mediaItemsOffset < mediaItemsFilteredCount;
            }
        });

        await RefreshHiddenCountAsync();
    }

    private async Task RefreshHiddenCountAsync()
    {
        try
        {
            var normalizedSourceId = ActiveSourceId;
            var normalizedAlbumId = ActiveAlbumId;
            var querySourceId = string.IsNullOrWhiteSpace(normalizedSourceId) ? null : normalizedSourceId;

            if (string.IsNullOrWhiteSpace(querySourceId))
            {
                await MainThread.InvokeOnMainThreadAsync(() => HasHiddenItemsInFolder = false);
                return;
            }

            var dateFrom = IsDateFilterEnabled ? DateFilterFrom : (DateTime?)null;
            var dateTo = IsDateFilterEnabled ? DateFilterTo : (DateTime?)null;
            var hideDuplicates = SettingsService.HideDuplicateFilesEnabled &&
                                 string.IsNullOrWhiteSpace(normalizedAlbumId);

            var hiddenCount = string.IsNullOrWhiteSpace(normalizedAlbumId)
                ? hideDuplicates
                    ? await indexService.CountHiddenUniqueAsync(SearchText, SelectedSearchScope, querySourceId,
                            dateFrom, dateTo, SelectedMediaTypes)
                        .ConfigureAwait(false)
                    : await indexService.CountHiddenAsync(SearchText, SelectedSearchScope, querySourceId, dateFrom,
                            dateTo, SelectedMediaTypes)
                        .ConfigureAwait(false)
                : await albumService.CountHiddenAlbumItemsAsync(normalizedAlbumId, SearchText, SelectedSearchScope,
                        querySourceId, dateFrom, dateTo, SelectedMediaTypes)
                    .ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => HasHiddenItemsInFolder = hiddenCount > 0);
        }
        catch
        {
            // Best-effort only.
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
    public async Task SelectAlbumAsync(AlbumListItem? album)
    {
        if (album == null || string.Equals(album.Id, ActiveAlbumId, StringComparison.Ordinal))
            return;

        ActiveAlbumId = album.Id;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RequestPermissionAsync()
    {
        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (!HasMediaPermission)
        {
            var openSettings = await dialogService.DisplayAlertAsync(
                AppResources.PermissionTitle,
                AppResources.PermissionMessage,
                AppResources.PermissionOk,
                AppResources.OkButton);
            if (openSettings)
                permissionService.OpenAppSettings();
        }
        if (HasMediaPermission)
            await RunIndexAsync();
    }


    private void EnqueueThumbnailUiUpdate(MediaItem item, string path)
    {
        if (item == null || string.IsNullOrWhiteSpace(path))
            return;

        pendingThumbUiUpdates.Enqueue((item, path));
        ScheduleThumbnailUiFlush();
    }

    private void ScheduleThumbnailUiFlush()
    {
        // Ensure at most one flush is scheduled at a time. Batching avoids UI-thread churn
        // when many thumbnails complete quickly (large libraries, fast SSDs).
        if (Interlocked.CompareExchange(ref pendingThumbUiFlushScheduled, 1, 0) != 0)
            return;

        MainThread.BeginInvokeOnMainThread(FlushThumbnailUiUpdates);
    }

    private void FlushThumbnailUiUpdates()
    {
        try
        {
            Interlocked.Exchange(ref pendingThumbUiFlushScheduled, 0);

            var processed = 0;
            while (processed < ThumbUiFlushBatchSize && pendingThumbUiUpdates.TryDequeue(out var update))
            {
                var item = update.Item;
                var p = update.Path;

                // Force refresh even if the same file path gets assigned again.
                if (string.Equals(item.ThumbnailPath, p, StringComparison.OrdinalIgnoreCase))
                    item.ThumbnailPath = string.Empty;

                item.ThumbnailPath = p;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await indexService.UpdateThumbnailPathAsync(item.Path, p, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort only.
                    }
                });
                processed++;
            }

            if (!pendingThumbUiUpdates.IsEmpty)
                // Defer the next batch slightly so scrolling and input remain responsive.
                _ = Task.Run(async () =>
                {
                    await Task.Delay(33).ConfigureAwait(false);
                    ScheduleThumbnailUiFlush();
                });
        }
        catch
        {
            // Best-effort: never crash UI because of thumbnail updates.
        }
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

                // Snapshot must be taken on the UI thread (ObservableCollection is not thread-safe).
                var snapshot = await MainThread.InvokeOnMainThreadAsync(() => MediaItems.ToList());
                if (snapshot.Count == 0)
                    return;

                // Prioritize what is currently visible (+ buffer) and keep the initial part warm.
                var first = Math.Max(0, visibleFirstIndex - 24);
                var lastVisible = visibleLastIndex > 0 ? visibleLastIndex : visibleFirstIndex;
                var last = Math.Min(snapshot.Count - 1, lastVisible + 96);

                var work = new List<MediaItem>(Math.Min(900, snapshot.Count));
                work.AddRange(snapshot.Take(64));
                if (first <= last)
                    work.AddRange(snapshot.Skip(first).Take(last - first + 1));

                // De-duplicate by path and keep the per-run work bounded.
                var workList = work
                    .Where(i => i != null && !string.IsNullOrWhiteSpace(i.Path))
                    .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Take(800)
                    .ToList();

                var dop = Math.Min(4, Math.Max(2, Environment.ProcessorCount / 2));
                await Parallel.ForEachAsync(workList, new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = ct
                }, async (item, token) =>
                {
                    token.ThrowIfCancellationRequested();

                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                        try
                        {
                            var fi = new FileInfo(item.ThumbnailPath);
                            if (fi.Length > 0)
                                return;

                            // A 0-byte file can happen if thumbnail generation was cancelled while writing.
                            File.Delete(item.ThumbnailPath);
                        }
                        catch
                        {
                            // Ignore and regenerate.
                        }

                    var p = await thumbnailService.EnsureThumbnailAsync(item, token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(p))
                        return;

                    EnqueueThumbnailUiUpdate(item, p);
                });
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation.
            }
            catch (Exception ex)
            {
                ErrorLog.LogException(ex, "MainViewModel.StartThumbnailPipeline");
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

    public void UpdateVisibleRange(int firstVisibleIndex, int lastVisibleIndex)
    {
        if (firstVisibleIndex < 0 || lastVisibleIndex < 0)
            return;

        if (firstVisibleIndex == visibleFirstIndex && lastVisibleIndex == visibleLastIndex)
            return;

        // Throttle restarts for tiny scroll movements.
        var significant = Math.Abs(firstVisibleIndex - visibleFirstIndex) >= 6 ||
                          Math.Abs(lastVisibleIndex - visibleLastIndex) >= 6;
        visibleFirstIndex = firstVisibleIndex;
        visibleLastIndex = lastVisibleIndex;

        if (!significant)
            return;

        lock (thumbnailLock)
        {
            if (thumbnailPipelineRunning)
            {
                thumbnailPipelineQueued = true;
                thumbCts?.Cancel();
                return;
            }
        }

        StartThumbnailPipeline();
    }

    private async Task RefreshPeopleTagsAsync(IReadOnlyList<MediaItem> items, int currentVersion)
    {
        if (!IsPeopleTaggingEnabled || items.Count == 0)
            return;

        try
        {
            var tagMap = await peopleTagService
                .GetTagsForMediaAsync(items.Select(item => item.Path))
                .ConfigureAwait(false);
            var faceCounts = await peopleTagService
                .GetFaceCountsForMediaAsync(items.Select(item => item.Path))
                .ConfigureAwait(false);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mediaItemsVersion != currentVersion)
                    return;

                foreach (var item in items)
                {
                    if (tagMap.TryGetValue(item.Path, out var tags))
                        item.PeopleTagsSummary = string.Join(", ", tags);
                    else
                        item.PeopleTagsSummary = string.Empty;
                    var faceCount = faceCounts.TryGetValue(item.Path, out var count) ? count : 0;
                    var hasFacesDetected = faceCount > 0;
                    item.PeopleTagActionLabel = hasFacesDetected
                        ? AppResources.TagPeopleAction
                        : AppResources.TagPeopleNoFacesAction;
                }
            });
        }
        catch
        {
            // ignore tag refresh failures to avoid disrupting browsing
        }
    }


    private async Task RefreshTaggedPeopleCountAsync()
    {
        try
        {
            if (!IsPeopleTaggingEnabled)
            {
                await MainThread.InvokeOnMainThreadAsync(() => TaggedPeopleCount = 0);
                return;
            }

            var count = await peopleTagService.CountDistinctPeopleAsync().ConfigureAwait(false);
            MainThread.BeginInvokeOnMainThread(() => TaggedPeopleCount = count);
        }
        catch
        {
            // Keep browsing resilient if the DB is unavailable or not initialized yet.
        }
    }

    private async Task RefreshLocationsCountAsync()
    {
        if (Interlocked.Exchange(ref isLocationsCountRefreshRunning, 1) == 1)
            return;

        try
        {
            if (!IsLocationEnabled)
            {
                await MainThread.InvokeOnMainThreadAsync(() => LocationsCount = 0);
                return;
            }

            var count = await indexService.CountLocationsAsync(MediaType.All, excludedSourceIds).ConfigureAwait(false);
            MainThread.BeginInvokeOnMainThread(() => LocationsCount = count);
        }
        catch
        {
            // Keep browsing resilient if the DB is unavailable or not initialized yet.
        }
        finally
        {
            Interlocked.Exchange(ref isLocationsCountRefreshRunning, 0);
        }
    }

    private async Task UpdateSourceStatsAsync()
    {
        var sources = await sourceService.GetSourcesAsync();
        var filteredSources = FilterAndroidChildSourcesIfNeeded(sources, out var sourceExclusions);
        await UpdateSourceStatsAsync(filteredSources);
        excludedSourceIds = sourceExclusions;
    }

    private Task UpdateSourceStatsAsync(List<MediaSource> sources)
    {
        TotalSourceCount = sources.Count;
        EnabledSourceCount = sources.Count(s => s.IsEnabled);
        SourcesSummary = string.Format(AppResources.SourcesSummaryFormat, EnabledSourceCount, TotalSourceCount);
        return Task.CompletedTask;
    }

    public void ReloadSettingsFromService()
    {
        // Suppress automatic refresh triggers while we apply multiple properties.
        isApplyingSavedSettings = true;
        try
        {
            SearchText = SettingsService.SearchText;
            SelectedSearchScope = SettingsService.SearchScope == SearchScope.None
                ? SearchScope.All
                : SettingsService.SearchScope;
            var sortKey = SettingsService.SelectedSortOptionKey;
            SelectedSortOption = SortOptions.FirstOrDefault(o => o.Key == sortKey) ?? SortOptions.FirstOrDefault();
            ActiveSourceId = SettingsService.ActiveSourceId;
            ActiveAlbumId = SettingsService.ActiveAlbumId;
            IsDateFilterEnabled = SettingsService.DateFilterEnabled;
            DateFilterFrom = SettingsService.DateFilterFrom;
            DateFilterTo = SettingsService.DateFilterTo;
            var visibleTypes = SettingsService.VisibleMediaTypes;
            SelectedMediaTypes = visibleTypes == MediaType.None ? MediaType.All : visibleTypes;
            IsLocationEnabled = SettingsService.LocationsEnabled;
            NeedsReindex = SettingsService.NeedsReindex;
            ApplyPlaybackSettings();
            ApplyFileChangeSettings();
            ApplyPeopleTaggingSettings();
        }
        finally
        {
            isApplyingSavedSettings = false;
        }
    }

    public void ApplyPlaybackSettings()
    {
        IsInternalPlayerEnabled = SettingsService.InternalPlayerEnabled;
        if (!IsInternalPlayerEnabled)
            ClearPlayerState();
    }

    public void ApplyFileChangeSettings()
    {
        AllowFileChanges = SettingsService.AllowFileChanges;
    }

    public void ApplyPeopleTaggingSettings()
    {
        IsPeopleTaggingEnabled = SettingsService.PeopleTaggingEnabled;
    }

    private void ClearPlayerState()
    {
        CurrentMediaSource = null;
        CurrentMediaName = "";
        CurrentMediaType = MediaType.None;
        IsPlayerFullscreen = false;
    }

    private static List<MediaSource> FilterAndroidChildSourcesIfNeeded(List<MediaSource> sources,
        out List<string> excludedSourceIds)
    {
        excludedSourceIds = new List<string>();
#if ANDROID && !WINDOWS
        var allDeviceSource = sources.FirstOrDefault(source =>
            string.Equals(source.Id, "device_all", StringComparison.OrdinalIgnoreCase));
        if (allDeviceSource is { IsEnabled: false })
        {
            var childSources = sources.Where(IsAndroidChildSource).ToList();
            excludedSourceIds = childSources
                .Select(source => source.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            return sources.Where(source => !IsAndroidChildSource(source)).ToList();
        }
#endif
        return sources;
    }

    private static bool IsAndroidChildSource(MediaSource source)
    {
        return !string.Equals(source.Id, "device_all", StringComparison.OrdinalIgnoreCase)
               && source.Id.StartsWith("android_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeActiveSourceId(List<MediaSource> sources, string activeSourceId)
    {
        var enabledSources = sources.Where(s => s.IsEnabled).ToList();
        if (enabledSources.Count == 0)
            return string.Empty;

        // Empty source id means "all sources" (no filtering).
        if (string.IsNullOrWhiteSpace(activeSourceId))
            return string.Empty;

        var exists = enabledSources.Any(s => s.Id == activeSourceId);
        return exists ? activeSourceId : string.Empty;
    }

    private static List<MediaSource> BuildSourceTabs(List<MediaSource> enabledSources)
    {
        // Provide a stable "All media" choice (Id = "") at the top so users can view items
        // that were indexed before SourceId existed or when multiple sources are enabled.
        var tabs = new List<MediaSource>
        {
            new()
            {
                Id = string.Empty,
                DisplayName = AppResources.AllAlbumsTab,
                LocalFolderPath = string.Empty,
                IsEnabled = true
            }
        };

        if (enabledSources != null && enabledSources.Count > 0)
            tabs.AddRange(enabledSources);

        return tabs;
    }


    private static string NormalizeActiveAlbumId(List<AlbumListItem> albums, string activeAlbumId)
    {
        if (string.IsNullOrWhiteSpace(activeAlbumId))
            return string.Empty;

        return albums.Any(a => string.Equals(a.Id, activeAlbumId, StringComparison.Ordinal))
            ? activeAlbumId
            : string.Empty;
    }

    private static List<AlbumListItem> BuildAlbumTabs(List<AlbumListItem> albums)
    {
        var tabs = new List<AlbumListItem>
        {
            new()
            {
                Id = string.Empty,
                Name = AppResources.AllAlbumsTab,
                IsAll = true
            }
        };

        tabs.AddRange(albums);
        return tabs;
    }

    private async Task RefreshAlbumTabsAsync()
    {
        var albumSummaries = await albumService.GetAlbumSummariesAsync().ConfigureAwait(false);
        var normalizedAlbumId = NormalizeActiveAlbumId(albumSummaries, ActiveAlbumId);
        var tabs = BuildAlbumTabs(albumSummaries);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ActiveAlbumId = normalizedAlbumId;
            AlbumTabs = tabs;
        });
    }

    private void OnMediaItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MediaCount = MediaItems.Count;
        mediaItemsVersion++;

        // Keep MarkedCount accurate without re-counting the whole list on every incremental change.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            var snapshot = MediaItems.ToList();
            SubscribeToMarkedChanges(snapshot);
            MarkedCount = snapshot.Count(v => v.IsMarked);
            ScheduleDerivedMediaRebuild(snapshot, mediaItemsVersion, true);
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (var obj in e.NewItems)
            {
                if (obj is not MediaItem item)
                    continue;

                HookMediaItem(item);
                if (item.IsMarked)
                    MarkedCount++;
            }

            ScheduleDerivedMediaRebuild(e.NewItems.OfType<MediaItem>().ToList(), mediaItemsVersion, false);
            return;
        }

        if ((e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace) &&
            e.OldItems != null)
        {
            foreach (var obj in e.OldItems)
            {
                if (obj is not MediaItem item)
                    continue;

                UnhookMediaItem(item);
                if (item.IsMarked)
                    MarkedCount = Math.Max(0, MarkedCount - 1);
            }

            ScheduleDerivedMediaRebuild(null, mediaItemsVersion, false);
            return;
        }

        // Fallback for other actions.
        ScheduleDerivedMediaRebuild(null, mediaItemsVersion, false);
    }

    private void ScheduleDerivedMediaRebuild(IReadOnlyList<MediaItem>? recentItems, int requestedVersion,
        bool fullRefresh)
    {
        mediaDerivedCts?.Cancel();
        mediaDerivedCts?.Dispose();
        mediaDerivedCts = new CancellationTokenSource();
        var ct = mediaDerivedCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce bursts (Refresh + LoadMore etc.)
                await Task.Delay(120, ct).ConfigureAwait(false);

                // Snapshot must be taken on the UI thread (ObservableCollection is not thread-safe).
                var snapshot = await MainThread.InvokeOnMainThreadAsync(() => MediaItems.ToList());
                ct.ThrowIfCancellationRequested();

                var timeline = BuildTimelineEntries(snapshot);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (requestedVersion != mediaItemsVersion)
                        return;

                    TimelineEntries = timeline;
                    StartThumbnailPipeline();
                });

                if (!IsPeopleTaggingEnabled)
                    return;

                if (fullRefresh)
                    await RefreshPeopleTagsAsync(snapshot, requestedVersion).ConfigureAwait(false);
                else if (recentItems != null && recentItems.Count > 0)
                    await RefreshPeopleTagsAsync(recentItems, requestedVersion).ConfigureAwait(false);
            }
            catch
            {
                // Ignore cancellation or background failures.
            }
        }, ct);
    }

    partial void OnIsPeopleTaggingEnabledChanged(bool value)
    {
        if (!value)
        {
            foreach (var item in MediaItems)
                item.PeopleTagsSummary = string.Empty;
            _ = RefreshTaggedPeopleCountAsync();
            PeopleModelsStatusText = string.Empty;
            return;
        }

        _ = RefreshPeopleTagsAsync(MediaItems.ToList(), mediaItemsVersion);
        _ = RefreshTaggedPeopleCountAsync();
        RefreshPeopleModelsStatus();
    }

    partial void OnIsLocationEnabledChanged(bool value)
    {
        _ = RefreshLocationsCountAsync();
    }

    private void RefreshPeopleModelsStatus()
    {
        if (!IsPeopleTaggingEnabled)
        {
            PeopleModelsStatusText = string.Empty;
            return;
        }

        var s = modelFileService.GetStatusSnapshot();
        var ready = s.YuNet == ModelFileService.ModelStatus.Ready
                    && s.SFace == ModelFileService.ModelStatus.Ready;

        var stateText = ready
            ? AppResources.SettingsPeopleModelsStatusReady
            : AppResources.SettingsPeopleModelsStatusMissing;
        PeopleModelsStatusText = $"{AppResources.SettingsPeopleModelsStatusLabel}: {stateText}";
    }

    partial void OnIsInternalPlayerEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBottomDock));
        OnPropertyChanged(nameof(ShowVideoPlayer));
        OnPropertyChanged(nameof(ShowPreview));
    }

    partial void OnMarkedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMarked));
        OnPropertyChanged(nameof(ShowBottomDock));
    }

    partial void OnShowHiddenInFolderChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHiddenToggleVisible));

        if (isApplyingShowHiddenState)
            return;

        SetShowHiddenForSource(ActiveSourceId, value);
        _ = RefreshAsync();
    }

    partial void OnHasHiddenItemsInFolderChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowHiddenToggleVisible));
    }

    partial void OnActiveSourceIdChanged(string value)
    {
        SettingsService.ActiveSourceId = value;
        SelectedSource = Sources.FirstOrDefault(source => source.Id == value)
                         ?? Sources.FirstOrDefault();

        isApplyingShowHiddenState = true;
        try
        {
            ShowHiddenInFolder = GetShowHiddenForSource(value);
        }
        finally
        {
            isApplyingShowHiddenState = false;
        }
    }

    partial void OnActiveAlbumIdChanged(string value)
    {
        SettingsService.ActiveAlbumId = value;
        SelectedAlbum = AlbumTabs.FirstOrDefault(album => album.Id == value)
                        ?? AlbumTabs.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value)
    {
        SettingsService.SearchText = value;
    }

    partial void OnSelectedSearchScopeChanged(SearchScope value)
    {
        SettingsService.SearchScope = value == SearchScope.None ? SearchScope.All : value;
        SyncSearchScopeFilterSelections();
        if (!isApplyingSavedSettings)
            _ = RefreshAsync();
    }

    partial void OnNeedsReindexChanged(bool value)
    {
        OnPropertyChanged(nameof(IndexState));
    }

    partial void OnIsIndexingChanged(bool value)
    {
        SettingsService.IsIndexing = value;
        OnPropertyChanged(nameof(IndexState));
    }

    partial void OnIsDateFilterEnabledChanged(bool value)
    {
        SettingsService.DateFilterEnabled = value;
        if (isApplyingSavedSettings)
            return;

        _ = RefreshAsync();
    }

    partial void OnDateFilterFromChanged(DateTime value)
    {
        if (value > DateFilterTo)
            DateFilterTo = value;
        SettingsService.DateFilterFrom = value;
        if (isApplyingSavedSettings)
            return;

        if (IsDateFilterEnabled)
            _ = RefreshAsync();
    }

    partial void OnDateFilterToChanged(DateTime value)
    {
        if (value < DateFilterFrom)
            DateFilterFrom = value;
        SettingsService.DateFilterTo = value;
        if (isApplyingSavedSettings)
            return;

        if (IsDateFilterEnabled)
            _ = RefreshAsync();
    }

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value != null)
            SettingsService.SelectedSortOptionKey = value.Key;
    }

    partial void OnSelectedMediaTypesChanged(MediaType value)
    {
        if (value == MediaType.None)
            return;

        SettingsService.VisibleMediaTypes = value;
        SyncMediaTypeFilterSelections();
        if (isApplyingSavedSettings)
            return;

        _ = RefreshAsync();
    }

    partial void OnSelectedSourceChanged(MediaSource? value)
    {
        if (value == null || value.Id == ActiveSourceId)
            return;

        _ = SelectSourceAsync(value);
    }

    partial void OnSelectedAlbumChanged(AlbumListItem? value)
    {
        if (value == null || string.Equals(value.Id, ActiveAlbumId, StringComparison.Ordinal))
            return;

        _ = SelectAlbumAsync(value);
    }

    partial void OnSourcesChanged(List<MediaSource> value)
    {
        SelectedSource = value.FirstOrDefault(source => source.Id == ActiveSourceId)
                         ?? value.FirstOrDefault();
        OnPropertyChanged(nameof(HasMultipleSources));
    }

    partial void OnAlbumTabsChanged(List<AlbumListItem> value)
    {
        SelectedAlbum = value.FirstOrDefault(album => album.Id == ActiveAlbumId)
                        ?? value.FirstOrDefault();
        OnPropertyChanged(nameof(HasAlbums));
    }

    private bool GetShowHiddenForSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;

        return showHiddenBySource.TryGetValue(sourceId, out var value) && value;
    }

    private void SetShowHiddenForSource(string sourceId, bool value)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return;

        if (value)
            showHiddenBySource[sourceId] = true;
        else
            showHiddenBySource.Remove(sourceId);
    }

    private static List<TimelineEntry> BuildTimelineEntries(List<MediaItem>? items)
    {
        if (items == null || items.Count == 0)
            return new List<TimelineEntry>();

        var entries = new List<TimelineEntry>();
        var lastKey = "";
        var lastYear = -1;

        // Build the timeline in a deterministic chronological order even when the media grid
        // is sorted by a different key (name/duration). This keeps the date sidebar stable
        // and makes it clear which month/year the user is navigating.
        var ordered = items
            .OrderByDescending(i => i.DateAddedSeconds)
            .ToList();

        foreach (var item in ordered)
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

    private void SubscribeToMarkedChanges(IReadOnlyCollection<MediaItem>? items)
    {
        if (subscribedMediaItems.Count > 0)
            foreach (var mediaItem in subscribedMediaItems)
                mediaItem.PropertyChanged -= mediaMarkedHandler;

        subscribedMediaItems.Clear();
        if (items == null)
            return;

        foreach (var mediaItem in items)
            if (subscribedMediaItems.Add(mediaItem))
                mediaItem.PropertyChanged += mediaMarkedHandler;
    }

    private void HookMediaItem(MediaItem item)
    {
        if (subscribedMediaItems.Add(item))
            item.PropertyChanged += mediaMarkedHandler;
    }

    private void UnhookMediaItem(MediaItem item)
    {
        if (subscribedMediaItems.Remove(item))
            item.PropertyChanged -= mediaMarkedHandler;
    }

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MediaItem.IsMarked))
            return;

        if (sender is not MediaItem item)
            return;

        MarkedCount = Math.Max(0, MarkedCount + (item.IsMarked ? 1 : -1));
    }

    private void RecomputeMarkedCount()
    {
        MarkedCount = MediaItems.Count(v => v.IsMarked);
    }

    private void UpdateIndexLocation(string sourceName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            IndexCurrentFolder = sourceName;
            IndexCurrentFile = "";
            return;
        }

        string? fileName;
        string? folderName;

        try
        {
            fileName = Path.GetFileName(path);
            folderName = Path.GetDirectoryName(path);
        }
        catch
        {
            fileName = null;
            folderName = null;
        }

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

        var parts = new List<string>(3);
        if (progress.ThumbsQueued > 0)
            parts.Add($"🖼 {progress.ThumbsDone:N0}/{progress.ThumbsQueued:N0}");
        if (progress.LocationsQueued > 0)
            parts.Add($"📍 {progress.LocationsDone:N0}/{progress.LocationsQueued:N0}");
        if (progress.DurationsQueued > 0)
            parts.Add($"⏱ {progress.DurationsDone:N0}/{progress.DurationsQueued:N0}");
        IndexWorkStatus = parts.Count == 0 ? "" : string.Join("  •  ", parts);

        UpdateIndexLocation(progress.SourceName, progress.CurrentPath);
        IndexedCount = progress.Inserted;
        IndexedMediaCount = indexStartingCount + progress.Inserted;

        if (progress.LocationsDone > indexLastLocationsRefreshDone)
        {
            var now = Environment.TickCount64;
            if (progress.LocationsDone - indexLastLocationsRefreshDone >= 20 ||
                now - indexLastLocationsRefreshMs >= 2000)
            {
                indexLastLocationsRefreshDone = progress.LocationsDone;
                indexLastLocationsRefreshMs = now;
                _ = RefreshLocationsCountAsync();
            }
        }

        // IMPORTANT: Do not refresh the visible media list while indexing.
        // Refreshing without preserving scroll state resets the CollectionView and causes jumpy UI.
        // We still need a background refresh so newly indexed items become visible.
        // Therefore we only suggest refreshes (throttled) and the view preserves scroll position.
        if (progress.Inserted > indexLastInserted)
        {
            indexLastInserted = progress.Inserted;

            var now = Environment.TickCount64;
            var insertedDelta = indexLastInserted - indexLastLiveRefreshInserted;

            // Throttle UI refresh suggestions to avoid excessive DB queries.
            if (insertedDelta >= 6 && now - indexLastLiveRefreshMs >= 1200)
            {
                indexLastLiveRefreshMs = now;
                indexLastLiveRefreshInserted = indexLastInserted;
                IndexLiveRefreshSuggested?.Invoke(this, EventArgs.Empty);
            }
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

    [RelayCommand]
    public async Task RotateLeftAsync(MediaItem item)
    {
        await ApplyImageEditAsync(item, ImageEditOperation.RotateLeft).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RotateRightAsync(MediaItem item)
    {
        await ApplyImageEditAsync(item, ImageEditOperation.RotateRight).ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task MirrorAsync(MediaItem item)
    {
        await ApplyImageEditAsync(item, ImageEditOperation.MirrorHorizontal).ConfigureAwait(false);
    }

    private async Task ApplyImageEditAsync(MediaItem item, ImageEditOperation operation)
    {
        if (!AllowFileChanges)
            return;

        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        if (item.MediaType is not (MediaType.Photos or MediaType.Graphics))
            return;

        try
        {
            IsRefreshing = true;

            var ok = await imageEditService.TryApplyAsync(item.Path, operation, CancellationToken.None)
                .ConfigureAwait(false);
            if (!ok)
                return;

            // Force thumbnail refresh and update the UI binding immediately.
            thumbnailService.DeleteThumbnailForPath(item.Path);

            var thumb = await thumbnailService.EnsureThumbnailAsync(item, CancellationToken.None).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                item.ThumbnailPath = string.Empty;
                item.ThumbnailPath = thumb;

                if (string.Equals(CurrentMediaSource, item.Path, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentMediaSource = null;
                    CurrentMediaSource = item.Path;
                }
            });

            if (!string.IsNullOrWhiteSpace(thumb))
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await indexService.UpdateThumbnailPathAsync(item.Path, thumb, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort only.
                    }
                });
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public sealed partial class MediaTypeFilterOption : ObservableObject
    {
        [ObservableProperty] private bool isSelected;

        public MediaTypeFilterOption(MediaType mediaType, string label)
        {
            MediaType = mediaType;
            Label = label;
        }

        public MediaType MediaType { get; }
        public string Label { get; }
    }

    public sealed partial class SearchScopeFilterOption : ObservableObject
    {
        [ObservableProperty] private bool isSelected;

        public SearchScopeFilterOption(SearchScope scope, string label)
        {
            Scope = scope;
            Label = label;
        }

        public SearchScope Scope { get; }
        public string Label { get; }
    }
}
