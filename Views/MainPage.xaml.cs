using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Collections;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;
#if WINDOWS
using UltimateVideoBrowser.Behaviors;
#endif

namespace UltimateVideoBrowser.Views;

public partial class MainPage : ContentPage
{
    private const int TimelinePreviewMinCount = 6;
    private readonly PeopleDataService peopleData;
    private readonly IServiceProvider serviceProvider;
    private readonly MainViewModel vm;
    private CancellationTokenSource? appearingCts;
    private bool isHeaderSizeHooked;
    private bool isTimelineSelectionSyncing;
    private int lastFirstVisibleIndex = -1;
    private int lastLastVisibleIndex = -1;
    private int lastTimelineFirstVisibleIndex = -1;
    private int lastTimelineLastVisibleIndex = -1;

#if WINDOWS
    private IDisposable? marqueeSelection;
    private MarqueeOverlayDrawable? marqueeDrawable;
#endif

    // Remember the origin tile when navigating away (e.g. tagging) so we can restore
    // the scroll position when the user returns.
    private string? pendingScrollToMediaPath;

    public MainPage(MainViewModel vm, DeviceModeService deviceMode, IServiceProvider serviceProvider,
        PeopleDataService peopleData, IProUpgradeService proUpgradeService)
    {
        InitializeComponent();
        this.vm = vm;
        this.serviceProvider = serviceProvider;
        this.peopleData = peopleData;
        BindingContext = new MainPageBinding(vm, deviceMode, this, serviceProvider, peopleData, proUpgradeService);
        HeaderContainer.BindingContext = BindingContext;
        BindingContextChanged += (_, _) => HeaderContainer.BindingContext = BindingContext;
        TimelineSidebar.TimelineView.Scrolled += OnTimelineScrolled;

        // The header lives inside the MediaItemsView header. We keep a spacer above the timeline
        // so both columns align and scrolling feels natural.
        TryHookHeaderSize();

        vm.ProUpgradeRequested += (_, _) =>
        {
            if (BindingContext is MainPageBinding binding && binding.OpenProUpgradeCommand.CanExecute(null))
                binding.OpenProUpgradeCommand.Execute(null);
        };
    }

    // Implemented per-platform (Windows only) to add reliable keyboard navigation.
    partial void TryHookPlatformKeyboard();
    partial void UnhookPlatformKeyboard();

    internal void RememberScrollTarget(MediaItem item)
    {
        pendingScrollToMediaPath = item?.Path;
    }

    internal void ClearMediaSelection()
    {
        try
        {
            MediaItemsView.SelectedItem = null;
            MediaItemsView.SelectedItems?.Clear();
        }
        catch
        {
            // Best-effort: never crash due to selection edge cases.
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Apply user-visible flags synchronously so the UI updates immediately.
        vm.ApplyPlaybackSettings();
        vm.ApplyFileChangeSettings();
        vm.ApplyPeopleTaggingSettings();

        // Navigation back from Settings/Sources should feel instant.
        // Heavy work (DB queries, thumbnail pipeline, etc.) is queued so the first frame can render.
        appearingCts?.Cancel();
        appearingCts?.Dispose();
        appearingCts = new CancellationTokenSource();
        var ct = appearingCts.Token;

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Yield();
                if (ct.IsCancellationRequested)
                    return;

                await vm.OnMainPageAppearingAsync();

                // Requested: default People Tags ON but inform users about the 2-week non-Pro trial.
                await vm.TryShowPeopleTaggingTrialHintAsync().ConfigureAwait(false);

                // Restore scroll position after returning from a detail page (e.g. tagging).
                var path = pendingScrollToMediaPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pendingScrollToMediaPath = null;
                    var target = await vm.EnsureMediaItemLoadedAsync(path, ct);
                    if (target != null)
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            MediaItemsView.ScrollTo(target, position: ScrollToPosition.MakeVisible, animate: false);
                            MediaItemsView.Focus();
                        });
                }
            }
            catch
            {
                // Ignore
            }
        });

        _ = ((MainPageBinding)BindingContext).ApplyGridSpanAsync();
        SizeChanged += OnPageSizeChanged;

        TryHookHeaderSize();
        TryHookPlatformKeyboard();

#if WINDOWS
        TryHookMarqueeSelection();
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        appearingCts?.Cancel();
        appearingCts?.Dispose();
        appearingCts = null;
        SizeChanged -= OnPageSizeChanged;
        UnhookPlatformKeyboard();

#if WINDOWS
        try
        {
            marqueeSelection?.Dispose();
        }
        catch
        {
            // Ignore
        }

        marqueeSelection = null;
#endif

        if (isHeaderSizeHooked)
        {
            try
            {
                HeaderContainer.SizeChanged -= OnHeaderContainerSizeChanged;
            }
            catch
            {
                // Ignore
            }

            isHeaderSizeHooked = false;
        }
    }

#if WINDOWS
    private void TryHookMarqueeSelection()
    {
        if (marqueeSelection != null)
            return;

        if (MarqueeOverlay == null)
            return;

        marqueeDrawable ??= new MarqueeOverlayDrawable();
        MarqueeOverlay.Drawable = marqueeDrawable;

        // Attach pointer handling to the native WinUI ListView used by CollectionView.
        // The overlay remains InputTransparent and is only used for drawing.
        marqueeSelection = WindowsMarqueeSelection.Attach(MediaItemsView, MarqueeOverlay, marqueeDrawable);
    }
#endif

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        _ = ((MainPageBinding)BindingContext).ApplyGridSpanAsync();
    }

    private void OnTimelineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TimelineEntry entry)
            return;

        if (isTimelineSelectionSyncing)
            return;

        MediaItemsView.ScrollTo(entry.AnchorMedia, position: ScrollToPosition.Start, animate: true);
        if (BindingContext is MainPageBinding binding)
            binding.SetTimelinePreview(entry);
    }

    private void OnMediaItemsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // Drive the thumbnail priority queue based on what the user is currently seeing.
        vm.UpdateVisibleRange(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
        lastFirstVisibleIndex = e.FirstVisibleItemIndex;
        lastLastVisibleIndex = e.LastVisibleItemIndex;
        if (BindingContext is MainPageBinding binding)
        {
            var headerHeight = HeaderContainer?.Height ?? binding.HeaderHeight;
            var isHeaderVisible = e.VerticalOffset <= Math.Max(0, headerHeight - 1);
            binding.SetHeaderVisibility(isHeaderVisible);
        }

        if (vm.MediaItems.Count == 0 || vm.TimelineEntries.Count == 0)
            return;

        // Only sync the timeline selection from scrolling when the grid is sorted by date.
        // If the user sorts by name/duration, the first visible tile does not represent a
        // stable point on the timeline, which can cause jitter and broken scrolling.
        if (!string.Equals(vm.SelectedSortOption?.Key, "date", StringComparison.Ordinal))
            return;

        var index = Math.Clamp(e.FirstVisibleItemIndex, 0, vm.MediaItems.Count - 1);
        var item = vm.MediaItems[index];
        var date = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, item.DateAddedSeconds))
            .ToLocalTime()
            .DateTime;
        var entry = vm.TimelineEntries.FirstOrDefault(t => t.Year == date.Year && t.Month == date.Month);
        if (entry == null)
            return;

        // If timeline already shows the correct selection, we still may want to keep it in view,
        // but avoid jitter: only re-scroll when the selected entry actually changes.
        var selectionChanged = TimelineSidebar.TimelineView.SelectedItem != entry;

        isTimelineSelectionSyncing = true;
        TimelineSidebar.TimelineView.SelectedItem = entry;

        // Keep timeline in sync with the media scroll (single-scrollbar UX).
        // No animation -> no jitter; keep the entry visible.
        if (selectionChanged)
        {
            try
            {
                TimelineSidebar.TimelineView.ScrollTo(entry, position: ScrollToPosition.MakeVisible, animate: false);
            }
            catch
            {
                // Best-effort only.
            }
        }

        isTimelineSelectionSyncing = false;

        if (BindingContext is MainPageBinding binding1)
            binding1.SetTimelinePreview(entry);

    }

    private void OnTimelineScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        lastTimelineFirstVisibleIndex = e.FirstVisibleItemIndex;
        lastTimelineLastVisibleIndex = e.LastVisibleItemIndex;
    }

    private void OnMediaItemsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
#if WINDOWS
        // Keep the existing "marked" workflow fully intact. On Windows, selection behaves like
        // File Explorer (Ctrl / Shift multi-select), but all batch actions still operate on IsMarked.
        try
        {
            foreach (var removed in e.PreviousSelection.OfType<MediaItem>())
                removed.IsMarked = false;

            foreach (var added in e.CurrentSelection.OfType<MediaItem>())
                added.IsMarked = true;

            // Update preview without launching external playback. Double-click still opens.
            var last = e.CurrentSelection.OfType<MediaItem>().LastOrDefault();
            if (last != null)
                vm.Select(last);
        }
        catch
        {
            // Best-effort: never crash the UI due to selection edge cases.
        }
#endif
    }

    public void OnTimelineScrollUpClicked(object sender, EventArgs e)
    {
        ScrollTimelineByPage(false);
    }

    public void OnTimelineScrollDownClicked(object sender, EventArgs e)
    {
        ScrollTimelineByPage(true);
    }

    public void OnTimelineScrollTopClicked(object sender, EventArgs e)
    {
        ScrollTimelineToTop();
    }

    public void OnTimelineScrollBottomClicked(object sender, EventArgs e)
    {
        ScrollTimelineToBottom();
    }

    public void OnMediaScrollUpClicked(object sender, EventArgs e)
    {
        ScrollMediaByPage(false);
    }

    public void OnMediaScrollUpClicked(object sender, TappedEventArgs e)
    {
        ScrollMediaByPage(false);
    }

    public void OnMediaScrollDownClicked(object sender, EventArgs e)
    {
        ScrollMediaByPage(true);
    }

    public void OnMediaScrollDownClicked(object sender, TappedEventArgs e)
    {
        ScrollMediaByPage(true);
    }

    public void OnMediaScrollTopClicked(object sender, EventArgs e)
    {
        ScrollMediaToTop();
    }

    public void OnMediaScrollBottomClicked(object sender, EventArgs e)
    {
        ScrollMediaToBottom();
    }

    public void OnSettingsClicked(object sender, EventArgs e)
    {
        if (BindingContext is MainPageBinding binding && binding.OpenSettingsCommand.CanExecute(null))
            binding.OpenSettingsCommand.Execute(null);
    }

    private void ScrollMediaByPage(bool isDown)
    {
        if (vm.MediaItems.Count == 0)
            return;

        var pageSize = lastFirstVisibleIndex >= 0 && lastLastVisibleIndex >= lastFirstVisibleIndex
            ? Math.Max(1, lastLastVisibleIndex - lastFirstVisibleIndex + 1)
            : Math.Min(12, vm.MediaItems.Count);

        if (!isDown && lastFirstVisibleIndex <= 0)
        {
            ScrollToHeader();
            return;
        }

        var targetIndex = isDown
            ? Math.Min(vm.MediaItems.Count - 1,
                lastLastVisibleIndex >= 0 ? lastLastVisibleIndex + 1 : pageSize)
            : Math.Max(0, lastFirstVisibleIndex >= 0 ? lastFirstVisibleIndex - pageSize : 0);

        var target = vm.MediaItems[targetIndex];
        MediaItemsView.ScrollTo(target, position: ScrollToPosition.Start, animate: true);
    }

    private void ScrollMediaToTop()
    {
        if (vm.MediaItems.Count == 0)
            return;

        ScrollToHeader();
    }

    private void ScrollMediaToBottom()
    {
        if (vm.MediaItems.Count == 0)
            return;

        MediaItemsView.ScrollTo(vm.MediaItems.Count - 1, position: ScrollToPosition.End, animate: true);
    }

    private void ScrollToHeader()
    {
        MediaItemsView.ScrollTo(0, position: ScrollToPosition.MakeVisible, animate: true);
    }

    private void ScrollTimelineByPage(bool isDown)
    {
        if (vm.TimelineEntries.Count == 0)
            return;

        var pageSize = lastTimelineFirstVisibleIndex >= 0 && lastTimelineLastVisibleIndex >= lastTimelineFirstVisibleIndex
            ? Math.Max(1, lastTimelineLastVisibleIndex - lastTimelineFirstVisibleIndex + 1)
            : Math.Min(8, vm.TimelineEntries.Count);

        var targetIndex = isDown
            ? Math.Min(vm.TimelineEntries.Count - 1,
                lastTimelineLastVisibleIndex >= 0 ? lastTimelineLastVisibleIndex + 1 : pageSize)
            : Math.Max(0, lastTimelineFirstVisibleIndex >= 0 ? lastTimelineFirstVisibleIndex - pageSize : 0);

        var target = vm.TimelineEntries[targetIndex];
        TimelineSidebar.TimelineView.ScrollTo(target, position: ScrollToPosition.Start, animate: true);
    }

    private void ScrollTimelineToTop()
    {
        if (vm.TimelineEntries.Count == 0)
            return;

        TimelineSidebar.TimelineView.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
    }

    private void ScrollTimelineToBottom()
    {
        if (vm.TimelineEntries.Count == 0)
            return;

        TimelineSidebar.TimelineView.ScrollTo(vm.TimelineEntries.Count - 1, position: ScrollToPosition.End, animate: true);
    }

    private void TryHookHeaderSize()
    {
        if (isHeaderSizeHooked)
            return;

        if (HeaderContainer == null)
            return;

        HeaderContainer.SizeChanged += OnHeaderContainerSizeChanged;
        isHeaderSizeHooked = true;

        // Apply initial value if the layout is already measured.
        OnHeaderContainerSizeChanged(this, EventArgs.Empty);
    }

    private void OnHeaderContainerSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not MainPageBinding binding)
            return;

        // Height is 0 until the first layout pass.
        var newHeight = HeaderContainer.Height;
        if (newHeight <= 0)
            return;

        // Prevent list jumps while indexing: small header re-measurements (e.g. counters changing)
        // must not constantly adjust the spacer and therefore the scroll offset.
        var delta = Math.Abs(binding.HeaderHeight - newHeight);
        if (vm.IsIndexing && lastFirstVisibleIndex > 0 && delta < 60)
            return;

        if (delta < 2)
            return;

        binding.HeaderHeight = newHeight;
    }

    private void OnSortChipTapped(object sender, TappedEventArgs e)
    {
        SearchSortView?.SortPickerControl?.Focus();
    }

    private sealed class MainPageBinding : BindableObject
    {
        private readonly DeviceModeService deviceMode;
        private readonly IDialogService dialogService;
        private readonly MainPage page;
        private readonly PeopleDataService peopleData;
        private readonly IProUpgradeService proUpgradeService;
        private readonly IServiceProvider serviceProvider;
        private readonly MainViewModel vm;

        private int gridSpan = 3;
        private double headerHeight;
        private Window? indexingWindow;
        private bool isFiltersDockExpanded = true;
        private bool isHeaderVisible = true;
        private bool isIndexingOverlaySuppressed;
        private bool isIndexingOverlayVisible;
        private bool isIndexRefreshPending;
        private bool isPreviewDockExpanded = true;
        private bool isTimelinePreviewVisible;

        private int displayIndexedMediaCount;
        private int displayLocationsCount;
        private int displayTaggedPeopleCount;
        private int? pendingIndexedMediaCount;
        private int? pendingLocationsCount;
        private int? pendingTaggedPeopleCount;

        private long lastLiveRefreshMs;
        private string timelinePreviewLabel = "";

        public MainPageBinding(MainViewModel vm, DeviceModeService deviceMode, MainPage page,
            IServiceProvider serviceProvider, PeopleDataService peopleData, IProUpgradeService proUpgradeService)
        {
            this.vm = vm;
            this.deviceMode = deviceMode;
            this.page = page;
            this.serviceProvider = serviceProvider;
            this.peopleData = peopleData;
            this.proUpgradeService = proUpgradeService;
            dialogService = serviceProvider.GetService<IDialogService>() ?? new DialogService();

            OpenSourcesCommand = new AsyncRelayCommand(OpenSourcesAsync);
            RequestReindexCommand = new AsyncRelayCommand(RequestReindexAsync);
            OpenAlbumsCommand = new AsyncRelayCommand(OpenAlbumsAsync);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
            OpenProUpgradeCommand = new AsyncRelayCommand(OpenProUpgradeAsync);
            OpenPeopleCommand = new AsyncRelayCommand(OpenPeopleAsync);
            OpenMapCommand = new AsyncRelayCommand(OpenMapAsync);
            OpenLocationCommand = vm.OpenLocationCommand;
            RefreshCommand = vm.RefreshCommand;
            RunIndexCommand = vm.RunIndexCommand;
            CancelIndexCommand = vm.CancelIndexCommand;
            PlayCommand = vm.PlayCommand;
            TogglePlayerFullscreenCommand = vm.TogglePlayerFullscreenCommand;
            ToggleMediaTypeFilterCommand = vm.ToggleMediaTypeFilterCommand;
            ToggleSearchScopeCommand = vm.ToggleSearchScopeCommand;
            LoadMoreCommand = vm.LoadMoreCommand;
            RequestPermissionCommand = vm.RequestPermissionCommand;
            SaveAsMarkedCommand = vm.SaveAsMarkedCommand;
            AddMarkedToAlbumCommand = vm.AddMarkedToAlbumCommand;
            CopyMarkedCommand = vm.CopyMarkedCommand;
            MoveMarkedCommand = vm.MoveMarkedCommand;
            DeleteMarkedCommand = vm.DeleteMarkedCommand;
            ClearMarkedCommand = new RelayCommand(ClearMarked);
            RenameCommand = vm.RenameCommand;
            TagPeopleCommand = new AsyncRelayCommand<MediaItem>(OpenTagEditorAsync);
            OpenPersonFromTagCommand = new AsyncRelayCommand<TagNavigationContext>(OpenPersonFromTagAsync);
            OpenFolderCommand = vm.OpenFolderCommand;
            SelectSourceCommand = vm.SelectSourceCommand;
            SelectAlbumCommand = vm.SelectAlbumCommand;
            ShareCommand = vm.ShareCommand;
            SaveAsCommand = vm.SaveAsCommand;
            CopyItemCommand = vm.CopyItemCommand;
            DeleteItemCommand = vm.DeleteItemCommand;
            RotateLeftCommand = vm.RotateLeftCommand;
            RotateRightCommand = vm.RotateRightCommand;
            MirrorCommand = vm.MirrorCommand;
            OpenItemMenuCommand = vm.OpenItemMenuCommand;
            // Restore UI states from persisted settings.
            try
            {
                isPreviewDockExpanded = vm.SettingsService.PreviewDockExpanded;
                isFiltersDockExpanded = vm.SettingsService.FiltersDockExpanded;
            }
            catch
            {
                // Best-effort only.
            }

            displayIndexedMediaCount = vm.IndexedMediaCount;
            displayLocationsCount = vm.LocationsCount;
            displayTaggedPeopleCount = vm.TaggedPeopleCount;

            TogglePreviewDockExpandedCommand = new RelayCommand(() => IsPreviewDockExpanded = !IsPreviewDockExpanded);
            ToggleFiltersDockExpandedCommand = new RelayCommand(() => IsFiltersDockExpanded = !IsFiltersDockExpanded);
            DismissIndexOverlayCommand = new RelayCommand(() => IsIndexingOverlayVisible = false);
            ShowIndexOverlayCommand = new RelayCommand(() =>
            {
                isIndexingOverlaySuppressed = false;
                IsIndexingOverlayVisible = true;
            });
            RefreshIndexCommand = new AsyncRelayCommand(RefreshIndexAsync);


            proUpgradeService.ProStatusChanged += (_, _) =>
                MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(nameof(IsProUnlocked)));

            // While indexing we avoid automatic refreshes that can scroll the grid.
            // Instead we show a refresh button so the user can opt-in.
            vm.IndexLiveRefreshSuggested += (_, _) => MainThread.BeginInvokeOnMainThread(MarkIndexRefreshPending);

            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainViewModel.IsIndexing):
                        if (!vm.IsIndexing)
                        {
                            isIndexingOverlaySuppressed = false;
                            IsIndexingOverlayVisible = false;
                            IsIndexRefreshPending = false;
                        }
                        else
                        {
                            IsIndexingOverlayVisible = !isIndexingOverlaySuppressed;
                        }

                        OnPropertyChanged(nameof(IsIndexing));
                        OnPropertyChanged(nameof(IsTopBusy));
                        OnPropertyChanged(nameof(IsEmptyStateVisible));
                        OnPropertyChanged(nameof(ShowIndexingBanner));
                        OnPropertyChanged(nameof(IndexBannerTitle));
                        OnPropertyChanged(nameof(IndexBannerMessage));
                        OnPropertyChanged(nameof(ShowIndexBannerAction));
                        OnPropertyChanged(nameof(IndexBannerActionText));
                        OnPropertyChanged(nameof(IndexBannerActionCommand));
                        OnPropertyChanged(nameof(ShowIndexRefreshAction));
                        OnPropertyChanged(nameof(RefreshIndexCommand));
                        OnPropertyChanged(nameof(IndexState));
                        break;
                    case nameof(MainViewModel.IndexedCount):
                        OnPropertyChanged(nameof(IndexedCount));
                        OnPropertyChanged(nameof(ShowIndexingBanner));
                        break;
                    case nameof(MainViewModel.IndexProcessed):
                        OnPropertyChanged(nameof(IndexProcessed));
                        break;
                    case nameof(MainViewModel.IndexTotal):
                        OnPropertyChanged(nameof(IndexTotal));
                        break;
                    case nameof(MainViewModel.IndexRatio):
                        OnPropertyChanged(nameof(IndexRatio));
                        break;
                    case nameof(MainViewModel.IndexStatus):
                        OnPropertyChanged(nameof(IndexStatus));
                        break;
                    case nameof(MainViewModel.IndexCurrentFolder):
                        OnPropertyChanged(nameof(IndexCurrentFolder));
                        break;
                    case nameof(MainViewModel.IndexCurrentFile):
                        OnPropertyChanged(nameof(IndexCurrentFile));
                        break;
                    case nameof(MainViewModel.HasMediaPermission):
                        OnPropertyChanged(nameof(HasMediaPermission));
                        OnPropertyChanged(nameof(IsEmptyStateVisible));
                        break;
                    case nameof(MainViewModel.MediaItems):
                        OnPropertyChanged(nameof(MediaItems));
                        UpdateTimelinePreviewVisibility();
                        break;
                    case nameof(MainViewModel.MediaCount):
                        OnPropertyChanged(nameof(MediaCount));
                        break;
                    case nameof(MainViewModel.IndexedMediaCount):
                        UpdateDisplayCount(ref displayIndexedMediaCount, ref pendingIndexedMediaCount,
                            vm.IndexedMediaCount, nameof(IndexedMediaCountDisplay));
                        break;
                    case nameof(MainViewModel.LocationsCount):
                        UpdateDisplayCount(ref displayLocationsCount, ref pendingLocationsCount,
                            vm.LocationsCount, nameof(LocationsCountDisplay));
                        OnPropertyChanged(nameof(HasLocations));
                        break;
                    case nameof(MainViewModel.TimelineEntries):
                        OnPropertyChanged(nameof(TimelineEntries));
                        UpdateTimelinePreviewVisibility();
                        break;
                    case nameof(MainViewModel.EnabledSourceCount):
                        OnPropertyChanged(nameof(EnabledSourceCount));
                        break;
                    case nameof(MainViewModel.SourcesSummary):
                        OnPropertyChanged(nameof(SourcesSummary));
                        break;
                    case nameof(MainViewModel.TaggedPeopleCount):
                        UpdateDisplayCount(ref displayTaggedPeopleCount, ref pendingTaggedPeopleCount,
                            vm.TaggedPeopleCount, nameof(TaggedPeopleCountDisplay));
                        break;
                    case nameof(MainViewModel.MarkedCount):
                        OnPropertyChanged(nameof(MarkedCount));
                        OnPropertyChanged(nameof(HasMarked));
                        OnPropertyChanged(nameof(ShowBottomDock));
                        break;
                    case nameof(MainViewModel.Sources):
                        OnPropertyChanged(nameof(Sources));
                        OnPropertyChanged(nameof(HasMultipleSources));
                        break;
                    case nameof(MainViewModel.AlbumTabs):
                        OnPropertyChanged(nameof(AlbumTabs));
                        OnPropertyChanged(nameof(HasAlbums));
                        break;
                    case nameof(MainViewModel.HasMultipleSources):
                        OnPropertyChanged(nameof(HasMultipleSources));
                        break;
                    case nameof(MainViewModel.HasAlbums):
                        OnPropertyChanged(nameof(HasAlbums));
                        break;
                    case nameof(MainViewModel.ActiveSourceId):
                        OnPropertyChanged(nameof(ActiveSourceId));
                        break;
                    case nameof(MainViewModel.ActiveAlbumId):
                        OnPropertyChanged(nameof(ActiveAlbumId));
                        break;
                    case nameof(MainViewModel.SelectedSearchScope):
                        OnPropertyChanged(nameof(SelectedSearchScope));
                        break;
                    case nameof(MainViewModel.IsDateFilterEnabled):
                        OnPropertyChanged(nameof(IsDateFilterEnabled));
                        break;
                    case nameof(MainViewModel.DateFilterFrom):
                        OnPropertyChanged(nameof(DateFilterFrom));
                        break;
                    case nameof(MainViewModel.DateFilterTo):
                        OnPropertyChanged(nameof(DateFilterTo));
                        break;
                    case nameof(MainViewModel.IsSourceSwitching):
                        OnPropertyChanged(nameof(IsSourceSwitching));
                        OnPropertyChanged(nameof(IsTopBusy));
                        OnPropertyChanged(nameof(IsEmptyStateVisible));
                        break;
                    case nameof(MainViewModel.IsRefreshing):
                        OnPropertyChanged(nameof(IsRefreshing));
                        OnPropertyChanged(nameof(IsTopBusy));
                        OnPropertyChanged(nameof(IsEmptyStateVisible));
                        break;
                    case nameof(MainViewModel.IsInternalPlayerEnabled):
                        OnPropertyChanged(nameof(IsInternalPlayerEnabled));
                        OnPropertyChanged(nameof(ShowVideoPlayer));
                        OnPropertyChanged(nameof(ShowPreview));
                        OnPropertyChanged(nameof(ShowBottomDock));
                        break;
                    case nameof(MainViewModel.CurrentMediaSource):
                        OnPropertyChanged(nameof(CurrentMediaSource));
                        OnPropertyChanged(nameof(ShowVideoPlayer));
                        OnPropertyChanged(nameof(ShowPreview));
                        OnPropertyChanged(nameof(ShowPhotoPreview));
                        OnPropertyChanged(nameof(ShowDocumentPreview));
                        break;
                    case nameof(MainViewModel.CurrentMediaName):
                        OnPropertyChanged(nameof(CurrentMediaName));
                        break;
                    case nameof(MainViewModel.CurrentMediaType):
                        OnPropertyChanged(nameof(CurrentMediaType));
                        OnPropertyChanged(nameof(ShowVideoPlayer));
                        OnPropertyChanged(nameof(ShowPhotoPreview));
                        OnPropertyChanged(nameof(ShowDocumentPreview));
                        OnPropertyChanged(nameof(ShowPreview));
                        break;
                    case nameof(MainViewModel.SelectedMediaTypes):
                        OnPropertyChanged(nameof(SelectedMediaTypes));
                        break;
                    case nameof(MainViewModel.IsPlayerFullscreen):
                        OnPropertyChanged(nameof(IsPlayerFullscreen));
                        break;
                    case nameof(MainViewModel.AllowFileChanges):
                        OnPropertyChanged(nameof(AllowFileChanges));
                        break;
                    case nameof(MainViewModel.IsPeopleTaggingEnabled):
                        OnPropertyChanged(nameof(IsPeopleTaggingEnabled));
                        break;
                    case nameof(MainViewModel.IsLocationEnabled):
                        OnPropertyChanged(nameof(IsLocationEnabled));
                        OnPropertyChanged(nameof(ShowLocationsHighlight));
                        OnPropertyChanged(nameof(HasLocations));
                        break;
                    case nameof(MainViewModel.NeedsReindex):
                        OnPropertyChanged(nameof(ShowIndexingBanner));
                        OnPropertyChanged(nameof(IndexBannerTitle));
                        OnPropertyChanged(nameof(IndexBannerMessage));
                        OnPropertyChanged(nameof(ShowIndexBannerAction));
                        OnPropertyChanged(nameof(IndexBannerActionText));
                        OnPropertyChanged(nameof(IndexBannerActionCommand));
                        OnPropertyChanged(nameof(ShowIndexRefreshAction));
                        OnPropertyChanged(nameof(IndexState));
                        break;
                }
            };

            // MediaItems is an ObservableRangeCollection; most changes come via CollectionChanged,
            // not PropertyChanged. We need to refresh derived UI state (empty overlay) accordingly.
            vm.MediaItems.CollectionChanged += (_, _) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                    OnPropertyChanged(nameof(MediaItems));
                });
            };
        }

        private void ClearMarked()
        {
            if (vm.ClearMarkedCommand.CanExecute(null))
                vm.ClearMarkedCommand.Execute(null);

#if WINDOWS
            page.ClearMediaSelection();
#endif
        }

        public void SetHeaderVisibility(bool isVisible)
        {
            if (isHeaderVisible == isVisible)
                return;

            isHeaderVisible = isVisible;
            if (isHeaderVisible)
                FlushPendingCounts();
        }

        private void FlushPendingCounts()
        {
            if (pendingIndexedMediaCount.HasValue)
            {
                displayIndexedMediaCount = pendingIndexedMediaCount.Value;
                pendingIndexedMediaCount = null;
                OnPropertyChanged(nameof(IndexedMediaCountDisplay));
            }

            if (pendingLocationsCount.HasValue)
            {
                displayLocationsCount = pendingLocationsCount.Value;
                pendingLocationsCount = null;
                OnPropertyChanged(nameof(LocationsCountDisplay));
            }

            if (pendingTaggedPeopleCount.HasValue)
            {
                displayTaggedPeopleCount = pendingTaggedPeopleCount.Value;
                pendingTaggedPeopleCount = null;
                OnPropertyChanged(nameof(TaggedPeopleCountDisplay));
            }
        }

        private void UpdateDisplayCount(ref int displayValue, ref int? pendingValue, int value,
            string propertyName)
        {
            if (isHeaderVisible)
            {
                if (displayValue == value)
                    return;

                displayValue = value;
                pendingValue = null;
                OnPropertyChanged(propertyName);
            }
            else
            {
                pendingValue = value;
            }
        }


        public double HeaderHeight
        {
            get => headerHeight;
            set
            {
                if (Math.Abs(headerHeight - value) < 0.5)
                    return;

                headerHeight = value;
                OnPropertyChanged();
            }
        }

        public IAsyncRelayCommand OpenSourcesCommand { get; }
        public IAsyncRelayCommand RequestReindexCommand { get; }
        public IAsyncRelayCommand OpenAlbumsCommand { get; }
        public IAsyncRelayCommand OpenSettingsCommand { get; }
        public IAsyncRelayCommand OpenProUpgradeCommand { get; }
        public IAsyncRelayCommand OpenPeopleCommand { get; }
        public IAsyncRelayCommand OpenMapCommand { get; }
        public IAsyncRelayCommand OpenLocationCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand RunIndexCommand { get; }
        public IRelayCommand CancelIndexCommand { get; }
        public IAsyncRelayCommand RequestPermissionCommand { get; }
        public IRelayCommand DismissIndexOverlayCommand { get; }
        public IRelayCommand ShowIndexOverlayCommand { get; }
        public IAsyncRelayCommand RefreshIndexCommand { get; }
        public IRelayCommand TogglePreviewDockExpandedCommand { get; }
        public IRelayCommand ToggleFiltersDockExpandedCommand { get; }
        public IRelayCommand PlayCommand { get; }
        public IRelayCommand TogglePlayerFullscreenCommand { get; }
        public IRelayCommand ToggleMediaTypeFilterCommand { get; }
        public IRelayCommand ToggleSearchScopeCommand { get; }
        public IAsyncRelayCommand LoadMoreCommand { get; }
        public IAsyncRelayCommand CopyMarkedCommand { get; }
        public IAsyncRelayCommand SaveAsMarkedCommand { get; }
        public IAsyncRelayCommand AddMarkedToAlbumCommand { get; }
        public IAsyncRelayCommand MoveMarkedCommand { get; }
        public IAsyncRelayCommand DeleteMarkedCommand { get; }
        public IRelayCommand ClearMarkedCommand { get; }
        public IAsyncRelayCommand RenameCommand { get; }
        public IAsyncRelayCommand TagPeopleCommand { get; }
        public IAsyncRelayCommand<TagNavigationContext> OpenPersonFromTagCommand { get; }
        public IAsyncRelayCommand OpenFolderCommand { get; }
        public IAsyncRelayCommand SelectSourceCommand { get; }
        public IAsyncRelayCommand SelectAlbumCommand { get; }

        public IAsyncRelayCommand ShareCommand { get; }
        public IAsyncRelayCommand SaveAsCommand { get; }
        public IAsyncRelayCommand CopyItemCommand { get; }
        public IAsyncRelayCommand DeleteItemCommand { get; }
        public IAsyncRelayCommand RotateLeftCommand { get; }
        public IAsyncRelayCommand RotateRightCommand { get; }
        public IAsyncRelayCommand MirrorCommand { get; }
        public IAsyncRelayCommand OpenItemMenuCommand { get; }

        public int GridSpan
        {
            get => gridSpan;
            set
            {
                gridSpan = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => vm.SearchText;
            set
            {
                vm.SearchText = value;
                OnPropertyChanged();
            }
        }

        public SearchScope SelectedSearchScope
        {
            get => vm.SelectedSearchScope;
            set
            {
                if (vm.SelectedSearchScope == value)
                    return;

                vm.SelectedSearchScope = value;
                OnPropertyChanged();
            }
        }

        public bool IsDateFilterEnabled
        {
            get => vm.IsDateFilterEnabled;
            set
            {
                if (vm.IsDateFilterEnabled == value)
                    return;

                vm.IsDateFilterEnabled = value;
                OnPropertyChanged();
            }
        }

        public DateTime DateFilterFrom
        {
            get => vm.DateFilterFrom;
            set
            {
                if (vm.DateFilterFrom == value)
                    return;

                vm.DateFilterFrom = value;
                OnPropertyChanged();
            }
        }

        public DateTime DateFilterTo
        {
            get => vm.DateFilterTo;
            set
            {
                if (vm.DateFilterTo == value)
                    return;

                vm.DateFilterTo = value;
                OnPropertyChanged();
            }
        }

        public bool IsIndexing => vm.IsIndexing;
        public bool IsSourceSwitching => vm.IsSourceSwitching;
        public bool IsRefreshing => vm.IsRefreshing;

        public bool IsTopBusy => vm.IsIndexing || vm.IsRefreshing || vm.IsSourceSwitching;

        public bool IsIndexingOverlayVisible
        {
            get => isIndexingOverlayVisible;
            set
            {
                if (isIndexingOverlayVisible == value)
                    return;

                isIndexingOverlayVisible = value;
                if (value)
                {
                    MainThread.BeginInvokeOnMainThread(EnsureIndexingWindow);
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(CloseIndexingWindow);
                    if (vm.IsIndexing)
                        isIndexingOverlaySuppressed = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowIndexingBanner));
            }
        }

        public bool ShowIndexingBanner =>
            vm.IndexState == IndexingState.NeedsReindex ||
            (vm.IndexState == IndexingState.Running && !IsIndexingOverlayVisible);

        public IndexingState IndexState => vm.IndexState;

        public string IndexBannerTitle => vm.IndexState switch
        {
            IndexingState.Running => AppResources.IndexStatusRunningTitle,
            IndexingState.NeedsReindex => AppResources.IndexStatusNeededTitle,
            _ => AppResources.IndexStatusReadyTitle
        };

        public string IndexBannerMessage => vm.IndexState switch
        {
            IndexingState.Running => AppResources.IndexStatusRunningMessage,
            IndexingState.NeedsReindex => AppResources.IndexStatusNeededMessage,
            _ => AppResources.IndexStatusReadyMessage
        };

        public bool ShowIndexBannerAction => vm.IndexState != IndexingState.Ready;

        public string IndexBannerActionText => vm.IndexState == IndexingState.Running
            ? AppResources.IndexingShowDetailsButton
            : AppResources.IndexStatusActionStart;

        public ICommand IndexBannerActionCommand =>
            vm.IndexState == IndexingState.Running ? ShowIndexOverlayCommand : RunIndexCommand;

        public bool ShowIndexRefreshAction => vm.IndexState == IndexingState.Running;

        public bool IsIndexRefreshPending
        {
            get => isIndexRefreshPending;
            private set
            {
                if (isIndexRefreshPending == value)
                    return;

                isIndexRefreshPending = value;
                OnPropertyChanged();
            }
        }

        public int IndexedCount => vm.IndexedCount;
        public int IndexProcessed => vm.IndexProcessed;
        public int IndexTotal => vm.IndexTotal;
        public double IndexRatio => vm.IndexRatio;
        public string IndexStatus => vm.IndexStatus;
        public string IndexCurrentFolder => vm.IndexCurrentFolder;
        public string IndexCurrentFile => vm.IndexCurrentFile;
        public bool HasMediaPermission => vm.HasMediaPermission;
        public int MediaCount => vm.MediaCount;
        public int IndexedMediaCountDisplay => displayIndexedMediaCount;
        public int LocationsCountDisplay => displayLocationsCount;
        public int EnabledSourceCount => vm.EnabledSourceCount;
        public int TaggedPeopleCountDisplay => displayTaggedPeopleCount;
        public string SourcesSummary => vm.SourcesSummary;
        public int MarkedCount => vm.MarkedCount;
        public bool HasMarked => vm.HasMarked;
        public bool ShowBottomDock => vm.ShowBottomDock;

        public bool IsPreviewDockExpanded
        {
            get => isPreviewDockExpanded;
            set
            {
                if (isPreviewDockExpanded == value)
                    return;

                isPreviewDockExpanded = value;
                try
                {
                    vm.SettingsService.PreviewDockExpanded = value;
                }
                catch
                {
                    // Best-effort only.
                }

                OnPropertyChanged();
            }
        }

        public bool IsFiltersDockExpanded
        {
            get => isFiltersDockExpanded;
            set
            {
                if (isFiltersDockExpanded == value)
                    return;

                isFiltersDockExpanded = value;
                try
                {
                    vm.SettingsService.FiltersDockExpanded = value;
                }
                catch
                {
                    // Best-effort only.
                }

                OnPropertyChanged();
            }
        }

        public bool IsEmptyStateVisible =>
            vm.HasMediaPermission
            && !vm.IsIndexing
            && !vm.IsSourceSwitching
            && !vm.IsRefreshing
            // Only show the "no sources" helper when the app truly has nothing configured.
            // If media exists but is currently filtered out, showing "no sources" is misleading.
            && vm.EnabledSourceCount == 0
            && vm.IndexedMediaCount == 0
            && vm.MediaItems.Count == 0;

        public IReadOnlyList<SortOption> SortOptions => vm.SortOptions;
        public IReadOnlyList<MainViewModel.MediaTypeFilterOption> MediaTypeFilters => vm.MediaTypeFilters;
        public IReadOnlyList<MainViewModel.SearchScopeFilterOption> SearchScopeFilters => vm.SearchScopeFilters;
        public List<AlbumListItem> AlbumTabs => vm.AlbumTabs;
        public List<MediaSource> Sources => vm.Sources;
        public bool HasMultipleSources => vm.HasMultipleSources;
        public bool HasAlbums => vm.HasAlbums;
        public bool IsProUnlocked => proUpgradeService.IsProUnlocked;
        public string ActiveSourceId => vm.ActiveSourceId;
        public string ActiveAlbumId => vm.ActiveAlbumId;

        public SortOption? SelectedSortOption
        {
            get => vm.SelectedSortOption;
            set
            {
                if (vm.SelectedSortOption == value)
                    return;

                vm.SelectedSortOption = value;
                OnPropertyChanged();
                _ = vm.RefreshAsync();
            }
        }

        public ObservableRangeCollection<MediaItem> MediaItems => vm.MediaItems;
        public List<TimelineEntry> TimelineEntries => vm.TimelineEntries;
        public string? CurrentMediaSource => vm.CurrentMediaSource;
        public string CurrentMediaName => vm.CurrentMediaName;
        public MediaType CurrentMediaType => vm.CurrentMediaType;
        public bool IsInternalPlayerEnabled => vm.IsInternalPlayerEnabled;
        public bool IsPlayerFullscreen => vm.IsPlayerFullscreen;
        public bool ShowVideoPlayer => vm.ShowVideoPlayer;
        public bool ShowPhotoPreview => vm.ShowPhotoPreview;
        public bool ShowDocumentPreview => vm.ShowDocumentPreview;
        public bool ShowPreview => vm.ShowPreview;

        public bool IsTimelinePreviewVisible
        {
            get => isTimelinePreviewVisible;
            private set
            {
                if (isTimelinePreviewVisible == value)
                    return;

                isTimelinePreviewVisible = value;
                OnPropertyChanged();
            }
        }

        public string TimelinePreviewLabel
        {
            get => timelinePreviewLabel;
            private set
            {
                if (timelinePreviewLabel == value)
                    return;

                timelinePreviewLabel = value;
                OnPropertyChanged();
            }
        }

        public MediaType SelectedMediaTypes => vm.SelectedMediaTypes;
        public bool AllowFileChanges => vm.AllowFileChanges;
        public bool IsPeopleTaggingEnabled => vm.IsPeopleTaggingEnabled;
        public bool IsLocationEnabled => vm.IsLocationEnabled;
        public bool HasLocations => vm.HasLocations;
        public bool ShowLocationsHighlight => vm.IsLocationEnabled;

        public void SetTimelinePreview(TimelineEntry entry)
        {
            TimelinePreviewLabel = entry.PreviewLabel;
            UpdateTimelinePreviewVisibility();
        }

        private void UpdateTimelinePreviewVisibility()
        {
            IsTimelinePreviewVisible = vm.TimelineEntries.Count >= TimelinePreviewMinCount
                                       && !string.IsNullOrWhiteSpace(timelinePreviewLabel);
        }

        private async Task OpenSourcesAsync()
        {
            // Do not depend on page.Handler/MauiContext being available.
            // In some startup/runtime scenarios (especially on Windows), Handler can be null which makes
            // the app look "unclickable" because navigation commands fault immediately.
            var target = serviceProvider.GetService<SourcesPage>()
                         ?? ActivatorUtilities.CreateInstance<SourcesPage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task RequestReindexAsync()
        {
            var confirm = await dialogService.DisplayAlertAsync(
                AppResources.ReindexTitle,
                AppResources.ReindexPrompt,
                AppResources.OkButton,
                AppResources.CancelButton).ConfigureAwait(false);

            if (!confirm)
                return;

            await vm.RunIndexAsync().ConfigureAwait(false);
        }

        private async Task OpenAlbumsAsync()
        {
            var target = serviceProvider.GetService<AlbumsPage>()
                         ?? ActivatorUtilities.CreateInstance<AlbumsPage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task OpenSettingsAsync()
        {
            var target = serviceProvider.GetService<SettingsPage>()
                         ?? ActivatorUtilities.CreateInstance<SettingsPage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task OpenProUpgradeAsync()
        {
            var target = serviceProvider.GetService<ProUpgradePage>()
                         ?? ActivatorUtilities.CreateInstance<ProUpgradePage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task OpenPeopleAsync()
        {
            var target = serviceProvider.GetService<PeoplePage>()
                         ?? ActivatorUtilities.CreateInstance<PeoplePage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task OpenMapAsync()
        {
            var target = serviceProvider.GetService<MapPage>()
                         ?? ActivatorUtilities.CreateInstance<MapPage>(serviceProvider);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(target));
        }

        private async Task OpenPersonFromTagAsync(TagNavigationContext? context)
        {
            if (context?.MediaItem != null)
                page.RememberScrollTarget(context.MediaItem);

            var trimmed = (context?.TagName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var found = await peopleData.FindPersonByNameAsync(trimmed, cts.Token).ConfigureAwait(false);

                if (found != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var personPage = ActivatorUtilities.CreateInstance<PersonPage>(serviceProvider);
                        personPage.Initialize(found.Value.Id, found.Value.Name);
                        await page.Navigation.PushAsync(personPage);
                    });
                    return;
                }

                // Fallback: open the people list filtered by the tapped name.
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var peoplePage = serviceProvider.GetService<PeoplePage>()
                                     ?? ActivatorUtilities.CreateInstance<PeoplePage>(serviceProvider);
                    if (peoplePage.BindingContext is PeopleViewModel pvm)
                        pvm.SearchText = trimmed;
                    await page.Navigation.PushAsync(peoplePage);
                });
            }
            catch
            {
                // Ignore
            }
        }

        private async Task OpenTagEditorAsync(MediaItem? item)
        {
            if (item == null)
                return;

            page.RememberScrollTarget(item);
            var editor = serviceProvider.GetService<PhotoPeopleEditorPage>()
                         ?? ActivatorUtilities.CreateInstance<PhotoPeopleEditorPage>(serviceProvider);
            editor.Initialize(item);
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushAsync(editor));
        }

        private void EnsureIndexingWindow()
        {
            if (indexingWindow != null)
            {
                // If the window already exists, bring it to front instead of doing nothing.
                try
                {
                    WindowFocusHelper.TryBringToFront(indexingWindow);
                }
                catch
                {
                    // Best-effort only.
                }

                return;
            }

            var indexingPage = new IndexingProgressPage(this)
            {
                Title = AppResources.Indexing
            };

            var window = new Window(indexingPage)
            {
                Title = AppResources.Indexing,
                Width = 460,
                Height = 360
            };

            try
            {
                var display = DeviceDisplay.MainDisplayInfo;
                var density = display.Density <= 0 ? 1 : display.Density;
                var screenWidth = display.Width / density;
                var screenHeight = display.Height / density;

                var x = (screenWidth - window.Width) / 2.0;
                var y = (screenHeight - window.Height) / 2.0;

                window.X = Math.Max(0, x);
                window.Y = Math.Max(0, y);
            }
            catch
            {
                // Best-effort only.
            }

            window.Destroying += (_, _) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    indexingWindow = null;
                    if (isIndexingOverlayVisible)
                        IsIndexingOverlayVisible = false;
                    if (vm.IsIndexing)
                        isIndexingOverlaySuppressed = true;
                    OnPropertyChanged(nameof(ShowIndexingBanner));
                });
            };

            indexingWindow = window;
            Application.Current?.OpenWindow(window);

            // Bring to foreground (best-effort).
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    WindowFocusHelper.TryBringToFront(window);
                }
                catch
                {
                    // Best-effort only.
                }
            });
        }

        private void CloseIndexingWindow()
        {
            if (indexingWindow == null)
                return;

            var window = indexingWindow;
            indexingWindow = null;
            Application.Current?.CloseWindow(window);
        }

        private void MarkIndexRefreshPending()
        {
            if (!vm.IsIndexing)
                return;

            var now = Environment.TickCount64;
            if (now - lastLiveRefreshMs < 900)
                return;

            lastLiveRefreshMs = now;
            IsIndexRefreshPending = true;
        }

        private async Task RefreshIndexAsync()
        {
            IsIndexRefreshPending = false;
            await vm.RefreshAsync().ConfigureAwait(false);
        }

        public Task ApplyGridSpanAsync()
        {
            // Compute grid span based on display width and mode.
            var mode = deviceMode.GetUiMode();

            if (mode == UiMode.Phone)
            {
                GridSpan = 1;
                return Task.CompletedTask;
            }

            var width = page.MediaItemsView?.Width ?? 0;
            if (width <= 0)
            {
                width = page.Width;
                if (width <= 0)
                    width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

                if (page.TimelineSidebar?.IsVisible == true)
                {
                    var timelineWidth = page.TimelineSidebar.Width;
                    if (timelineWidth <= 0)
                        timelineWidth = 120;
                    width = Math.Max(0, width - timelineWidth);
                }
            }

            var minTileWidth = 240;
            var tilePadding = 20;
            var targetTile = minTileWidth + tilePadding;
            if (mode == UiMode.Tv)
                targetTile = 340;
            else if (mode == UiMode.Tablet)
                targetTile = 300;

            var span = Math.Max(2, (int)(width / targetTile));

            GridSpan = Math.Min(8, span);
            return Task.CompletedTask;
        }
    }
}
