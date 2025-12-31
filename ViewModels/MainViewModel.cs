using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using UltimateVideoBrowser.Collections;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IDialogService dialogService;
    private readonly IFileExportService fileExportService;
    private readonly object indexProgressLock = new();
    private readonly IndexService indexService;

    private readonly PropertyChangedEventHandler mediaMarkedHandler;
    private readonly PeopleRecognitionService peopleRecognitionService;
    private readonly PeopleTagService peopleTagService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    private readonly object thumbnailLock = new();
    private readonly ThumbnailService thumbnailService;
    [ObservableProperty] private string activeSourceId = "";
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
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    private int indexStartingCount;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    private bool isApplyingIndexProgress;
    private bool isApplyingSavedSettings;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isIndexing;
    private bool isInitialized;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    private bool isLoadingMoreMediaItems;
    [ObservableProperty] private bool isPeopleTaggingEnabled;
    [ObservableProperty] private int taggedPeopleCount;
    [ObservableProperty] private bool isPlayerFullscreen;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isSourceSwitching;
    [ObservableProperty] private int markedCount;
    [ObservableProperty] private int mediaCount;

    private readonly ObservableRangeCollection<MediaItem> mediaItems = new();
    private int mediaItemsOffset;
    private int mediaItemsVersion;
    private int mediaQueryVersion;
    private IndexProgress? pendingIndexProgress;
    [ObservableProperty] private string searchText = "";

    [ObservableProperty] private MediaType selectedMediaTypes = MediaType.All;
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private string sourcesSummary = "";
    private HashSet<MediaItem> subscribedMediaItems = new();
    private CancellationTokenSource? thumbCts;
    private bool thumbnailPipelineQueued;
    private bool thumbnailPipelineRunning;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();
    [ObservableProperty] private int totalSourceCount;

    // Visible range is reported by the view (Scrolled event) so thumbnail work can prioritize what's on screen.
    private int visibleFirstIndex;
    private int visibleLastIndex;

    // Coalesce expensive derived-data rebuilds (timeline, tag summaries, etc.).
    private CancellationTokenSource? mediaDerivedCts;

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService,
        IFileExportService fileExportService,
        IDialogService dialogService,
        PeopleTagService peopleTagService,
        PeopleRecognitionService peopleRecognitionService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;
        this.fileExportService = fileExportService;
        this.dialogService = dialogService;
        this.peopleTagService = peopleTagService;
        this.peopleRecognitionService = peopleRecognitionService;

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

        mediaItems.CollectionChanged += OnMediaItemsCollectionChanged;
    }

    public IReadOnlyList<SortOption> SortOptions { get; }
    public IReadOnlyList<MediaTypeFilterOption> MediaTypeFilters { get; }

    public ObservableRangeCollection<MediaItem> MediaItems => mediaItems;

    public bool HasMarked => MarkedCount > 0;

    public bool ShowBottomDock => IsInternalPlayerEnabled || HasMarked;

    public bool ShowVideoPlayer => IsInternalPlayerEnabled && CurrentMediaType == MediaType.Videos
                                                           && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPhotoPreview => CurrentMediaType == MediaType.Photos
                                    && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowDocumentPreview => CurrentMediaType == MediaType.Documents
                                       && !string.IsNullOrWhiteSpace(CurrentMediaSource);

    public bool ShowPreview => ShowVideoPlayer || ShowPhotoPreview || ShowDocumentPreview;

    private void OnNeedsReindexChanged(object? sender, bool needsReindex)
    {
        if (!needsReindex || IsIndexing)
            return;

        if (!isInitialized)
        {
            _ = InitializeAsync();
            return;
        }

        _ = RunIndexAsync();
    }

    public async Task InitializeAsync()
    {
        if (isInitialized)
            return;

        isInitialized = true;
        await sourceService.EnsureDefaultSourceAsync();

        ReloadSettingsFromService();
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);
        Sources = sources.Where(s => s.IsEnabled).ToList();

        HasMediaPermission = await permissionService.CheckMediaReadAsync();

        if (!HasMediaPermission)
        {
            mediaItems.Clear();
            mediaItemsOffset = 0;
            hasMoreMediaItems = false;
            var total = await indexService.CountAsync(SelectedMediaTypes);
            await MainThread.InvokeOnMainThreadAsync(() => IndexedMediaCount = total);
            return;
        }

        _ = RefreshAsync();

        _ = RefreshTaggedPeopleCountAsync();

        if (settingsService.NeedsReindex)
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

                // IMPORTANT:
                // An empty source id is treated as "all sources" (no SourceId filter).
                // Previously we returned an empty list, which made the UI look broken
                // when the active source wasn't set or when sources were temporarily unavailable.
                var querySourceId = string.IsNullOrWhiteSpace(normalizedSourceId) ? null : normalizedSourceId;
                var items = await indexService.QueryPageAsync(SearchText, querySourceId, sortKey, dateFrom, dateTo,
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
                mediaItems.ReplaceRange(result.items);
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
            var nextItems = await indexService.QueryPageAsync(SearchText, querySourceId, sortKey, dateFrom, dateTo,
                    SelectedMediaTypes, mediaItemsOffset, PageSize)
                .ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (queryVersion != mediaQueryVersion)
                    return;

                mediaItems.AddRange(nextItems);
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
            await Task.Run(
                async () => { await indexService.IndexSourcesAsync(sources, indexedTypes, progress, indexCts.Token); },
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
        var updatedMatches = await peopleRecognitionService
            .EnsurePeopleTagsForMediaAsync(item, CancellationToken.None);
        item.PeopleTagsSummary = string.Join(", ",
            updatedMatches.Select(match => match.Name).Distinct(StringComparer.OrdinalIgnoreCase));

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var sourceId = string.IsNullOrWhiteSpace(item.SourceId) ? ActiveSourceId : item.SourceId;
                if (string.IsNullOrWhiteSpace(sourceId))
                    return;

                var photos = await indexService
                    .QueryAsync(string.Empty, sourceId, "name", null, null, MediaType.Photos)
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
            mediaItems.RemoveRange(moved);
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
            mediaItems.RemoveRange(deleted);
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
            mediaItems.RemoveRange(deleted);
            await UpdateIndexedMediaCountAsync();

            if (deleted.Any(i => string.Equals(i.Path, CurrentMediaSource, StringComparison.OrdinalIgnoreCase)))
                ClearPlayerState();
        }

        RecomputeMarkedCount();
    }


    [RelayCommand]
    public async Task OpenItemMenuAsync(MediaItem item)
    {
        if (item == null)
            return;

        var shell = Shell.Current;
        if (shell == null)
            return;

        var actions = new List<string>
        {
            AppResources.ShareAction,
            AppResources.CopyMarkedAction
        };

        if (AllowFileChanges)
            actions.Add(AppResources.DeleteMarkedAction);

        var choice = await MainThread.InvokeOnMainThreadAsync(() =>
            shell.DisplayActionSheet(item.Name, AppResources.CancelButton, null, actions.ToArray()));

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

        if (string.Equals(choice, AppResources.DeleteMarkedAction, StringComparison.Ordinal))
            await DeleteItemAsync(item);
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

                // Snapshot must be taken on the UI thread (ObservableCollection is not thread-safe).
                var snapshot = await MainThread.InvokeOnMainThreadAsync(() => mediaItems.ToList());
                if (snapshot.Count == 0)
                    return;

                // Prioritize what is currently visible (+ buffer) and keep the initial part warm.
                var first = Math.Max(0, visibleFirstIndex - 24);
                var lastVisible = visibleLastIndex > 0 ? visibleLastIndex : visibleFirstIndex;
                var last = Math.Min(snapshot.Count - 1, lastVisible + 96);

                var work = new List<MediaItem>(capacity: Math.Min(900, snapshot.Count));
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
                    {
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
                    }

                    var p = await thumbnailService.EnsureThumbnailAsync(item, token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(p))
                        return;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        // Force refresh even if the same file path gets assigned again.
                        if (string.Equals(item.ThumbnailPath, p, StringComparison.OrdinalIgnoreCase))
                            item.ThumbnailPath = string.Empty;

                        item.ThumbnailPath = p;
                    });
                });
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

    public void UpdateVisibleRange(int firstVisibleIndex, int lastVisibleIndex)
    {
        if (firstVisibleIndex < 0 || lastVisibleIndex < 0)
            return;

        if (firstVisibleIndex == visibleFirstIndex && lastVisibleIndex == visibleLastIndex)
            return;

        // Throttle restarts for tiny scroll movements.
        var significant = Math.Abs(firstVisibleIndex - visibleFirstIndex) >= 6 || Math.Abs(lastVisibleIndex - visibleLastIndex) >= 6;
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

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (mediaItemsVersion != currentVersion)
                    return;

                foreach (var item in items)
                    if (tagMap.TryGetValue(item.Path, out var tags))
                        item.PeopleTagsSummary = string.Join(", ", tags);
                    else
                        item.PeopleTagsSummary = string.Empty;
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

    public void ReloadSettingsFromService()
    {
        // Suppress automatic refresh triggers while we apply multiple properties.
        isApplyingSavedSettings = true;
        try
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
            ApplyPeopleTaggingSettings();
        }
        finally
        {
            isApplyingSavedSettings = false;
        }
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

    public void ApplyPeopleTaggingSettings()
    {
        IsPeopleTaggingEnabled = settingsService.PeopleTaggingEnabled;
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

    private void OnMediaItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        MediaCount = mediaItems.Count;
        mediaItemsVersion++;

        // Keep MarkedCount accurate without re-counting the whole list on every incremental change.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            var snapshot = mediaItems.ToList();
            SubscribeToMarkedChanges(snapshot);
            MarkedCount = snapshot.Count(v => v.IsMarked);
            ScheduleDerivedMediaRebuild(snapshot, mediaItemsVersion, fullRefresh: true);
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

            ScheduleDerivedMediaRebuild(e.NewItems.OfType<MediaItem>().ToList(), mediaItemsVersion, fullRefresh: false);
            return;
        }

        if ((e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Replace) && e.OldItems != null)
        {
            foreach (var obj in e.OldItems)
            {
                if (obj is not MediaItem item)
                    continue;

                UnhookMediaItem(item);
                if (item.IsMarked)
                    MarkedCount = Math.Max(0, MarkedCount - 1);
            }

            ScheduleDerivedMediaRebuild(null, mediaItemsVersion, fullRefresh: false);
            return;
        }

        // Fallback for other actions.
        ScheduleDerivedMediaRebuild(null, mediaItemsVersion, fullRefresh: false);
    }

    private void ScheduleDerivedMediaRebuild(IReadOnlyList<MediaItem>? recentItems, int requestedVersion, bool fullRefresh)
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
                var snapshot = await MainThread.InvokeOnMainThreadAsync(() => mediaItems.ToList());
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
                {
                    await RefreshPeopleTagsAsync(snapshot, requestedVersion).ConfigureAwait(false);
                }
                else if (recentItems != null && recentItems.Count > 0)
                {
                    await RefreshPeopleTagsAsync(recentItems, requestedVersion).ConfigureAwait(false);
                }
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
            return;
        }

        _ = RefreshPeopleTagsAsync(mediaItems.ToList(), mediaItemsVersion);
        _ = RefreshTaggedPeopleCountAsync();
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
        if (isApplyingSavedSettings)
            return;

        _ = RefreshAsync();

    }

    partial void OnDateFilterFromChanged(DateTime value)
    {
        if (value > DateFilterTo)
            DateFilterTo = value;
        settingsService.DateFilterFrom = value;
        if (isApplyingSavedSettings)
            return;

        if (IsDateFilterEnabled)
            _ = RefreshAsync();

    }

    partial void OnDateFilterToChanged(DateTime value)
    {
        if (value < DateFilterFrom)
            DateFilterFrom = value;
        settingsService.DateFilterTo = value;
        if (isApplyingSavedSettings)
            return;

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
        if (isApplyingSavedSettings)
            return;

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

    private void SubscribeToMarkedChanges(IReadOnlyCollection<MediaItem>? items)
    {
        if (subscribedMediaItems.Count > 0)
        {
            foreach (var mediaItem in subscribedMediaItems)
                mediaItem.PropertyChanged -= mediaMarkedHandler;
        }

        subscribedMediaItems.Clear();
        if (items == null)
            return;

        foreach (var mediaItem in items)
        {
            if (subscribedMediaItems.Add(mediaItem))
                mediaItem.PropertyChanged += mediaMarkedHandler;
        }
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
        MarkedCount = mediaItems.Count(v => v.IsMarked);
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