using CommunityToolkit.Mvvm.Input;
using MauiMediaSource = Microsoft.Maui.Controls.MediaSource;
using Microsoft.Maui.Controls;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel vm;

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
        _ = vm.InitializeAsync();
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

        VideosView.ScrollTo(entry.AnchorVideo, position: ScrollToPosition.Start, animate: true);
        ((CollectionView)sender).SelectedItem = null;
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
            RequestPermissionCommand = vm.RequestPermissionCommand;
            CopyMarkedCommand = vm.CopyMarkedCommand;
            MoveMarkedCommand = vm.MoveMarkedCommand;
            ClearMarkedCommand = vm.ClearMarkedCommand;
            RenameCommand = vm.RenameCommand;
            SelectSourceCommand = vm.SelectSourceCommand;
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

                        IsIndexingOverlayVisible = vm.IsIndexing && vm.IndexedCount > 0 && !isIndexingOverlaySuppressed;
                        OnPropertyChanged(nameof(IsIndexing));
                        OnPropertyChanged(nameof(ShowIndexingBanner));
                        break;
                    case nameof(MainViewModel.IndexedCount):
                        if (vm.IsIndexing && vm.IndexedCount > 0 && !isIndexingOverlaySuppressed)
                            IsIndexingOverlayVisible = true;

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
                    case nameof(MainViewModel.Videos):
                        OnPropertyChanged(nameof(Videos));
                        break;
                    case nameof(MainViewModel.VideoCount):
                        OnPropertyChanged(nameof(VideoCount));
                        break;
                    case nameof(MainViewModel.IndexedVideoCount):
                        OnPropertyChanged(nameof(IndexedVideoCount));
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
                        break;
                    case nameof(MainViewModel.IsInternalPlayerEnabled):
                        OnPropertyChanged(nameof(IsInternalPlayerEnabled));
                        OnPropertyChanged(nameof(ShowInternalPlayer));
                        break;
                    case nameof(MainViewModel.CurrentVideoSource):
                        OnPropertyChanged(nameof(CurrentVideoSource));
                        OnPropertyChanged(nameof(ShowInternalPlayer));
                        break;
                    case nameof(MainViewModel.CurrentVideoName):
                        OnPropertyChanged(nameof(CurrentVideoName));
                        break;
                    case nameof(MainViewModel.IsPlayerFullscreen):
                        OnPropertyChanged(nameof(IsPlayerFullscreen));
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
        public IAsyncRelayCommand CopyMarkedCommand { get; }
        public IAsyncRelayCommand MoveMarkedCommand { get; }
        public IRelayCommand ClearMarkedCommand { get; }
        public IAsyncRelayCommand RenameCommand { get; }
        public IAsyncRelayCommand SelectSourceCommand { get; }

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

        public bool ShowIndexingBanner => vm.IsIndexing && vm.IndexedCount > 0 && !IsIndexingOverlayVisible;
        public int IndexedCount => vm.IndexedCount;
        public int IndexProcessed => vm.IndexProcessed;
        public int IndexTotal => vm.IndexTotal;
        public double IndexRatio => vm.IndexRatio;
        public string IndexStatus => vm.IndexStatus;
        public string IndexCurrentFolder => vm.IndexCurrentFolder;
        public string IndexCurrentFile => vm.IndexCurrentFile;
        public bool HasMediaPermission => vm.HasMediaPermission;
        public int VideoCount => vm.VideoCount;
        public int IndexedVideoCount => vm.IndexedVideoCount;
        public int EnabledSourceCount => vm.EnabledSourceCount;
        public string SourcesSummary => vm.SourcesSummary;
        public int MarkedCount => vm.MarkedCount;
        public bool HasMarked => vm.HasMarked;
        public IReadOnlyList<SortOption> SortOptions => vm.SortOptions;
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

        public List<VideoItem> Videos => vm.Videos;
        public List<TimelineEntry> TimelineEntries => vm.TimelineEntries;
        public MauiMediaSource? CurrentVideoSource => vm.CurrentVideoSource;
        public string CurrentVideoName => vm.CurrentVideoName;
        public bool IsInternalPlayerEnabled => vm.IsInternalPlayerEnabled;
        public bool IsPlayerFullscreen => vm.IsPlayerFullscreen;
        public bool ShowInternalPlayer => IsInternalPlayerEnabled && CurrentVideoSource != null;

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
