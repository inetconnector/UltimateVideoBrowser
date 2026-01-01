using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Collections;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class MainPage : ContentPage
{
    private readonly PeopleDataService peopleData;
    private readonly IServiceProvider serviceProvider;
    private readonly MainViewModel vm;
    private CancellationTokenSource? appearingCts;
    private bool isHeaderSizeHooked;
    private bool isTimelineSelectionSyncing;

    // Remember the origin tile when navigating away (e.g. tagging) so we can restore
    // the scroll position when the user returns.
    private string? pendingScrollToMediaPath;

    public MainPage(MainViewModel vm, DeviceModeService deviceMode, IServiceProvider serviceProvider,
        PeopleDataService peopleData)
    {
        InitializeComponent();
        this.vm = vm;
        this.serviceProvider = serviceProvider;
        this.peopleData = peopleData;
        BindingContext = new MainPageBinding(vm, deviceMode, this, serviceProvider, peopleData);

        // The header lives inside the MediaItemsView header. We keep a spacer above the timeline
        // so both columns align and scrolling feels natural.
        TryHookHeaderSize();
    }

    internal void RememberScrollTarget(MediaItem item)
    {
        pendingScrollToMediaPath = item?.Path;
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

                // Restore scroll position after returning from a detail page (e.g. tagging).
                var path = pendingScrollToMediaPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    pendingScrollToMediaPath = null;
                    var target = await vm.EnsureMediaItemLoadedAsync(path, ct);
                    if (target != null)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            MediaItemsView.ScrollTo(target, position: ScrollToPosition.MakeVisible, animate: false);
                            MediaItemsView.Focus();
                        });
                    }
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
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        appearingCts?.Cancel();
        appearingCts?.Dispose();
        appearingCts = null;
        SizeChanged -= OnPageSizeChanged;

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
    }

    private void OnMediaItemsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        // Drive the thumbnail priority queue based on what the user is currently seeing.
        vm.UpdateVisibleRange(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);

        if (vm.MediaItems.Count == 0 || vm.TimelineEntries.Count == 0)
            return;

        var index = Math.Clamp(e.FirstVisibleItemIndex, 0, vm.MediaItems.Count - 1);
        var item = vm.MediaItems[index];
        var date = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, item.DateAddedSeconds))
            .ToLocalTime()
            .DateTime;
        var entry = vm.TimelineEntries.FirstOrDefault(t => t.Year == date.Year && t.Month == date.Month);
        if (entry == null || TimelineView.SelectedItem == entry)
            return;

        isTimelineSelectionSyncing = true;
        TimelineView.SelectedItem = entry;
        isTimelineSelectionSyncing = false;
    }

    public void OnTimelineScrollUpClicked(object sender, EventArgs e)
    {
        ScrollTimelineToStart();
    }

    public void OnTimelineScrollUpClicked(object sender, TappedEventArgs e)
    {
        ScrollTimelineToStart();
    }

    public void OnTimelineScrollDownClicked(object sender, EventArgs e)
    {
        ScrollTimelineToEnd();
    }

    public void OnTimelineScrollDownClicked(object sender, TappedEventArgs e)
    {
        ScrollTimelineToEnd();
    }

    private void ScrollTimelineToStart()
    {
        if (vm.TimelineEntries.Count == 0)
            return;

        var first = vm.TimelineEntries[0];
        TimelineView.ScrollTo(first, position: ScrollToPosition.Start, animate: true);
    }

    private void ScrollTimelineToEnd()
    {
        if (vm.TimelineEntries.Count == 0)
            return;

        var last = vm.TimelineEntries[^1];
        TimelineView.ScrollTo(last, position: ScrollToPosition.End, animate: true);
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
        if (HeaderContainer.Height > 0)
            binding.HeaderHeight = HeaderContainer.Height;
    }

    private void OnSortChipTapped(object sender, TappedEventArgs e)
    {
        SearchSortView?.SortPickerControl?.Focus();
    }

    private sealed class MainPageBinding : BindableObject
    {
        private readonly DeviceModeService deviceMode;
        private readonly MainPage page;
        private readonly PeopleDataService peopleData;
        private readonly IServiceProvider serviceProvider;
        private readonly MainViewModel vm;

        private int gridSpan = 3;
        private double headerHeight;
        private Window? indexingWindow;
        private bool isIndexingOverlaySuppressed;
        private bool isIndexingOverlayVisible;

        public MainPageBinding(MainViewModel vm, DeviceModeService deviceMode, MainPage page,
            IServiceProvider serviceProvider, PeopleDataService peopleData)
        {
            this.vm = vm;
            this.deviceMode = deviceMode;
            this.page = page;
            this.serviceProvider = serviceProvider;
            this.peopleData = peopleData;

            OpenSourcesCommand = new AsyncRelayCommand(OpenSourcesAsync);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
            OpenPeopleCommand = new AsyncRelayCommand(OpenPeopleAsync);
            OpenMapCommand = new AsyncRelayCommand(OpenMapAsync);
            RefreshCommand = vm.RefreshCommand;
            RunIndexCommand = vm.RunIndexCommand;
            CancelIndexCommand = vm.CancelIndexCommand;
            PlayCommand = vm.PlayCommand;
            TogglePlayerFullscreenCommand = vm.TogglePlayerFullscreenCommand;
            ToggleMediaTypeFilterCommand = vm.ToggleMediaTypeFilterCommand;
            LoadMoreCommand = vm.LoadMoreCommand;
            RequestPermissionCommand = vm.RequestPermissionCommand;
            SaveAsMarkedCommand = vm.SaveAsMarkedCommand;
            CopyMarkedCommand = vm.CopyMarkedCommand;
            MoveMarkedCommand = vm.MoveMarkedCommand;
            DeleteMarkedCommand = vm.DeleteMarkedCommand;
            ClearMarkedCommand = vm.ClearMarkedCommand;
            RenameCommand = vm.RenameCommand;
            TagPeopleCommand = new AsyncRelayCommand<MediaItem>(OpenTagEditorAsync);
            OpenPersonFromTagCommand = new AsyncRelayCommand<TagNavigationContext>(OpenPersonFromTagAsync);
            OpenFolderCommand = vm.OpenFolderCommand;
            SelectSourceCommand = vm.SelectSourceCommand;
            ShareCommand = vm.ShareCommand;
            SaveAsCommand = vm.SaveAsCommand;
            CopyItemCommand = vm.CopyItemCommand;
            DeleteItemCommand = vm.DeleteItemCommand;
            OpenItemMenuCommand = vm.OpenItemMenuCommand;
            DismissIndexOverlayCommand = new RelayCommand(() => IsIndexingOverlayVisible = false);
            ShowIndexOverlayCommand = new RelayCommand(() =>
            {
                isIndexingOverlaySuppressed = false;
                IsIndexingOverlayVisible = true;
            });

            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainViewModel.IsIndexing):
                        if (!vm.IsIndexing)
                        {
                            isIndexingOverlaySuppressed = false;
                            IsIndexingOverlayVisible = false;
                        }
                        else
                        {
                            IsIndexingOverlayVisible = !isIndexingOverlaySuppressed;
                        }

                        OnPropertyChanged(nameof(IsIndexing));
                        OnPropertyChanged(nameof(IsTopBusy));
                        OnPropertyChanged(nameof(ShowIndexingBanner));
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
                        break;
                    case nameof(MainViewModel.MediaItems):
                        OnPropertyChanged(nameof(MediaItems));
                        break;
                    case nameof(MainViewModel.MediaCount):
                        OnPropertyChanged(nameof(MediaCount));
                        break;
                    case nameof(MainViewModel.IndexedMediaCount):
                        OnPropertyChanged(nameof(IndexedMediaCount));
                        break;
                    case nameof(MainViewModel.TimelineEntries):
                        OnPropertyChanged(nameof(TimelineEntries));
                        break;
                    case nameof(MainViewModel.EnabledSourceCount):
                        OnPropertyChanged(nameof(EnabledSourceCount));
                        break;
                    case nameof(MainViewModel.SourcesSummary):
                        OnPropertyChanged(nameof(SourcesSummary));
                        break;
                    case nameof(MainViewModel.TaggedPeopleCount):
                        OnPropertyChanged(nameof(TaggedPeopleCount));
                        break;
                    case nameof(MainViewModel.MarkedCount):
                        OnPropertyChanged(nameof(MarkedCount));
                        OnPropertyChanged(nameof(HasMarked));
                        OnPropertyChanged(nameof(ShowBottomDock));
                        break;
                    case nameof(MainViewModel.Sources):
                        OnPropertyChanged(nameof(Sources));
                        break;
                    case nameof(MainViewModel.ActiveSourceId):
                        OnPropertyChanged(nameof(ActiveSourceId));
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
                        break;
                    case nameof(MainViewModel.IsRefreshing):
                        OnPropertyChanged(nameof(IsRefreshing));
                        OnPropertyChanged(nameof(IsTopBusy));
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
                        break;
                }
            };
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
        public IAsyncRelayCommand OpenSettingsCommand { get; }
        public IAsyncRelayCommand OpenPeopleCommand { get; }
        public IAsyncRelayCommand OpenMapCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand RunIndexCommand { get; }
        public IRelayCommand CancelIndexCommand { get; }
        public IAsyncRelayCommand RequestPermissionCommand { get; }
        public IRelayCommand DismissIndexOverlayCommand { get; }
        public IRelayCommand ShowIndexOverlayCommand { get; }
        public IRelayCommand PlayCommand { get; }
        public IRelayCommand TogglePlayerFullscreenCommand { get; }
        public IRelayCommand ToggleMediaTypeFilterCommand { get; }
        public IAsyncRelayCommand LoadMoreCommand { get; }
        public IAsyncRelayCommand CopyMarkedCommand { get; }
        public IAsyncRelayCommand SaveAsMarkedCommand { get; }
        public IAsyncRelayCommand MoveMarkedCommand { get; }
        public IAsyncRelayCommand DeleteMarkedCommand { get; }
        public IRelayCommand ClearMarkedCommand { get; }
        public IAsyncRelayCommand RenameCommand { get; }
        public IAsyncRelayCommand TagPeopleCommand { get; }
        public IAsyncRelayCommand<TagNavigationContext> OpenPersonFromTagCommand { get; }
        public IAsyncRelayCommand OpenFolderCommand { get; }
        public IAsyncRelayCommand SelectSourceCommand { get; }

        public IAsyncRelayCommand ShareCommand { get; }
        public IAsyncRelayCommand SaveAsCommand { get; }
        public IAsyncRelayCommand CopyItemCommand { get; }
        public IAsyncRelayCommand DeleteItemCommand { get; }
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
                    EnsureIndexingWindow();
                }
                else
                {
                    CloseIndexingWindow();
                    if (vm.IsIndexing)
                        isIndexingOverlaySuppressed = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowIndexingBanner));
            }
        }

        public bool ShowIndexingBanner => vm.IsIndexing && !IsIndexingOverlayVisible;
        public int IndexedCount => vm.IndexedCount;
        public int IndexProcessed => vm.IndexProcessed;
        public int IndexTotal => vm.IndexTotal;
        public double IndexRatio => vm.IndexRatio;
        public string IndexStatus => vm.IndexStatus;
        public string IndexCurrentFolder => vm.IndexCurrentFolder;
        public string IndexCurrentFile => vm.IndexCurrentFile;
        public bool HasMediaPermission => vm.HasMediaPermission;
        public int MediaCount => vm.MediaCount;
        public int IndexedMediaCount => vm.IndexedMediaCount;
        public int EnabledSourceCount => vm.EnabledSourceCount;
        public int TaggedPeopleCount => vm.TaggedPeopleCount;
        public string SourcesSummary => vm.SourcesSummary;
        public int MarkedCount => vm.MarkedCount;
        public bool HasMarked => vm.HasMarked;
        public bool ShowBottomDock => vm.ShowBottomDock;
        public IReadOnlyList<SortOption> SortOptions => vm.SortOptions;
        public IReadOnlyList<MainViewModel.MediaTypeFilterOption> MediaTypeFilters => vm.MediaTypeFilters;
        public List<MediaSource> Sources => vm.Sources;
        public string ActiveSourceId => vm.ActiveSourceId;

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
        public MediaType SelectedMediaTypes => vm.SelectedMediaTypes;
        public bool AllowFileChanges => vm.AllowFileChanges;
        public bool IsPeopleTaggingEnabled => vm.IsPeopleTaggingEnabled;
        public bool IsLocationEnabled => vm.IsLocationEnabled;

        private async Task OpenSourcesAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<SourcesPage>()!);
        }

        private async Task OpenSettingsAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<SettingsPage>()!);
        }

        private async Task OpenPeopleAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<PeoplePage>()!);
        }

        private async Task OpenMapAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<MapPage>()!);
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
                    var peoplePage = page.Handler!.MauiContext!.Services.GetService<PeoplePage>()!;
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
            var editor = page.Handler!.MauiContext!.Services.GetService<PhotoPeopleEditorPage>()!;
            editor.Initialize(item);
            await page.Navigation.PushAsync(editor);
        }

        private void EnsureIndexingWindow()
        {
            if (indexingWindow != null)
                return;

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

            window.Destroying += (_, _) =>
            {
                indexingWindow = null;
                if (isIndexingOverlayVisible)
                    isIndexingOverlayVisible = false;
                if (vm.IsIndexing)
                    isIndexingOverlaySuppressed = true;
                OnPropertyChanged(nameof(ShowIndexingBanner));
            };

            indexingWindow = window;
            Application.Current?.OpenWindow(window);
        }

        private void CloseIndexingWindow()
        {
            if (indexingWindow == null)
                return;

            var window = indexingWindow;
            indexingWindow = null;
            Application.Current?.CloseWindow(window);
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

                if (page.TimelineView?.IsVisible == true)
                {
                    var timelineWidth = page.TimelineView.Width;
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
