using CommunityToolkit.Mvvm.Input;
using System.Linq;
using UltimateVideoBrowser.Models;
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.InitializeAsync();
        await ((MainPageBinding)BindingContext).ApplyGridSpanAsync();
    }

    private void OnTimelineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TimelineEntry entry)
            return;

        VideosView.ScrollTo(entry.AnchorVideo, position: ScrollToPosition.Start, animate: true);
        ((CollectionView)sender).SelectedItem = null;
    }

    private sealed class MainPageBinding : BindableObject
    {
        private readonly DeviceModeService deviceMode;
        private readonly Page page;
        private readonly MainViewModel vm;

        private int gridSpan = 3;
        private bool isIndexingOverlayVisible;

        public MainPageBinding(MainViewModel vm, DeviceModeService deviceMode, Page page)
        {
            this.vm = vm;
            this.deviceMode = deviceMode;
            this.page = page;

            OpenSourcesCommand = new AsyncRelayCommand(OpenSourcesAsync);
            RefreshCommand = vm.RefreshCommand;
            RunIndexCommand = vm.RunIndexCommand;
            PlayCommand = vm.PlayCommand;
            RequestPermissionCommand = vm.RequestPermissionCommand;
            DismissIndexOverlayCommand = new RelayCommand(() => IsIndexingOverlayVisible = false);
            ShowIndexOverlayCommand = new RelayCommand(() => IsIndexingOverlayVisible = true);

            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainViewModel.IsIndexing):
                        IsIndexingOverlayVisible = vm.IsIndexing;
                        OnPropertyChanged(nameof(IsIndexing));
                        OnPropertyChanged(nameof(ShowIndexingBanner));
                        break;
                    case nameof(MainViewModel.IndexedCount):
                        OnPropertyChanged(nameof(IndexedCount));
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
                    case nameof(MainViewModel.TimelineEntries):
                        OnPropertyChanged(nameof(TimelineEntries));
                        break;
                    case nameof(MainViewModel.EnabledSourceCount):
                        OnPropertyChanged(nameof(EnabledSourceCount));
                        break;
                    case nameof(MainViewModel.SourcesSummary):
                        OnPropertyChanged(nameof(SourcesSummary));
                        break;
                }
            };
        }

        public IAsyncRelayCommand OpenSourcesCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand RunIndexCommand { get; }
        public IAsyncRelayCommand RequestPermissionCommand { get; }
        public IRelayCommand DismissIndexOverlayCommand { get; }
        public IRelayCommand ShowIndexOverlayCommand { get; }
        public IRelayCommand PlayCommand { get; }

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

        public bool IsIndexing => vm.IsIndexing;
        public bool IsIndexingOverlayVisible
        {
            get => isIndexingOverlayVisible;
            set
            {
                if (isIndexingOverlayVisible == value)
                    return;

                isIndexingOverlayVisible = value;
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
        public int VideoCount => vm.VideoCount;
        public int EnabledSourceCount => vm.EnabledSourceCount;
        public string SourcesSummary => vm.SourcesSummary;
        public IReadOnlyList<SortOption> SortOptions => vm.SortOptions;

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

        private async Task OpenSourcesAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<SourcesPage>()!);
        }

        public Task ApplyGridSpanAsync()
        {
            // Compute grid span based on display width and mode.
            var mode = deviceMode.GetUiMode();

            var width = page.Width;
            if (width <= 0)
                width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

            var targetTile = mode == UiMode.Tv ? 260 : mode == UiMode.Tablet ? 220 : 180;
            var span = Math.Max(2, (int)(width / targetTile));

            GridSpan = Math.Min(8, span);
            return Task.CompletedTask;
        }
    }               
}
