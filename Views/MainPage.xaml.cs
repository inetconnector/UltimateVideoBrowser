using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel vm;
    private bool isTimelineSelectionSyncing;

    public MainPage(MainViewModel vm, DeviceModeService deviceMode)
    {
        InitializeComponent();
        this.vm = vm;
        BindingContext = new MainPageBinding(vm, deviceMode, this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        vm.ApplyPlaybackSettings();
        vm.ApplyFileChangeSettings();
        vm.ApplyPeopleTaggingSettings();
        _ = vm.InitializeAsync();
        _ = vm.RefreshAsync();
        _ = ((MainPageBinding)BindingContext).ApplyGridSpanAsync();
        SizeChanged += OnPageSizeChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SizeChanged -= OnPageSizeChanged;
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

    private void OnSortChipTapped(object sender, TappedEventArgs e)
    {
        SortPicker?.Focus();
    }

    private sealed class MainPageBinding : BindableObject
    {
        private readonly DeviceModeService deviceMode;
        private readonly Page page;
        private readonly MainViewModel vm;

        private int gridSpan = 3;
        private Window? indexingWindow;
        private bool isIndexingOverlaySuppressed;
        private bool isIndexingOverlayVisible;
        private bool isLoadingWindowSuppressed;
        private Window? loadingWindow;

        public MainPageBinding(MainViewModel vm, DeviceModeService deviceMode, Page page)
        {
            this.vm = vm;
            this.deviceMode = deviceMode;
            this.page = page;

            OpenSourcesCommand = new AsyncRelayCommand(OpenSourcesAsync);
            OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
            RefreshCommand = vm.RefreshCommand;
            RunIndexCommand = vm.RunIndexCommand;
            CancelIndexCommand = vm.CancelIndexCommand;
            PlayCommand = vm.PlayCommand;
            TogglePlayerFullscreenCommand = vm.TogglePlayerFullscreenCommand;
            ToggleMediaTypeFilterCommand = vm.ToggleMediaTypeFilterCommand;
            LoadMoreCommand = vm.LoadMoreCommand;
            RequestPermissionCommand = vm.RequestPermissionCommand;
            CopyMarkedCommand = vm.CopyMarkedCommand;
            MoveMarkedCommand = vm.MoveMarkedCommand;
            DeleteMarkedCommand = vm.DeleteMarkedCommand;
            ClearMarkedCommand = vm.ClearMarkedCommand;
            RenameCommand = vm.RenameCommand;
            TagPeopleCommand = vm.TagPeopleCommand;
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
                            isLoadingWindowSuppressed = false;
                            UpdateLoadingWindow();
                        }
                        else
                        {
                            CloseLoadingWindow();
                            isLoadingWindowSuppressed = true;
                            IsIndexingOverlayVisible = !isIndexingOverlaySuppressed;
                        }

                        OnPropertyChanged(nameof(IsIndexing));
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
                    case nameof(MainViewModel.MarkedCount):
                        OnPropertyChanged(nameof(MarkedCount));
                        OnPropertyChanged(nameof(HasMarked));
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
                        UpdateLoadingWindow();
                        break;
                    case nameof(MainViewModel.IsRefreshing):
                        OnPropertyChanged(nameof(IsRefreshing));
                        UpdateLoadingWindow();
                        break;
                    case nameof(MainViewModel.IsInternalPlayerEnabled):
                        OnPropertyChanged(nameof(IsInternalPlayerEnabled));
                        OnPropertyChanged(nameof(ShowVideoPlayer));
                        OnPropertyChanged(nameof(ShowPreview));
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
                }
            };
        }

        public IAsyncRelayCommand OpenSourcesCommand { get; }
        public IAsyncRelayCommand OpenSettingsCommand { get; }
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
        public IAsyncRelayCommand MoveMarkedCommand { get; }
        public IAsyncRelayCommand DeleteMarkedCommand { get; }
        public IRelayCommand ClearMarkedCommand { get; }
        public IAsyncRelayCommand RenameCommand { get; }
        public IAsyncRelayCommand TagPeopleCommand { get; }
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
        public string SourcesSummary => vm.SourcesSummary;
        public int MarkedCount => vm.MarkedCount;
        public bool HasMarked => vm.HasMarked;
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

        public List<MediaItem> MediaItems => vm.MediaItems;
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

        private async Task OpenSourcesAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<SourcesPage>()!);
        }

        private async Task OpenSettingsAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<SettingsPage>()!);
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

        private void UpdateLoadingWindow()
        {
            if (vm.IsIndexing)
            {
                CloseLoadingWindow();
                isLoadingWindowSuppressed = true;
                return;
            }

            if (vm.IsRefreshing || vm.IsSourceSwitching)
            {
                if (isLoadingWindowSuppressed)
                    return;

                EnsureLoadingWindow();
                return;
            }

            isLoadingWindowSuppressed = false;
            CloseLoadingWindow();
        }

        private void EnsureLoadingWindow()
        {
            if (loadingWindow != null)
                return;

            var loadingPage = new LoadingProgressPage
            {
                BindingContext = this
            };

            var window = new Window(loadingPage)
            {
                Title = AppResources.LoadingMedia,
                Width = 320,
                Height = 220
            };

            window.Destroying += (_, _) =>
            {
                loadingWindow = null;
                if (vm.IsRefreshing || vm.IsSourceSwitching)
                    isLoadingWindowSuppressed = true;
            };

            loadingWindow = window;
            Application.Current?.OpenWindow(window);
        }

        private void CloseLoadingWindow()
        {
            if (loadingWindow == null)
                return;

            var window = loadingWindow;
            loadingWindow = null;
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

            var width = page.Width;
            if (width <= 0)
                width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

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