using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class MainPage : ContentPage
{
    readonly MainViewModel vm;

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

    sealed class MainPageBinding : BindableObject
    {
        readonly MainViewModel vm;
        readonly DeviceModeService deviceMode;
        readonly Page page;

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

            vm.PropertyChanged += (_, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(MainViewModel.IsIndexing):
                        OnPropertyChanged(nameof(IsIndexing));
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
                    case nameof(MainViewModel.HasMediaPermission):
                        OnPropertyChanged(nameof(HasMediaPermission));
                        break;
                    case nameof(MainViewModel.Videos):
                        OnPropertyChanged(nameof(Videos));
                        break;
                }
            };
        }

        public IAsyncRelayCommand OpenSourcesCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand RunIndexCommand { get; }
        public IAsyncRelayCommand RequestPermissionCommand { get; }
        public IRelayCommand PlayCommand { get; }

        int gridSpan = 3;
        public int GridSpan
        {
            get => gridSpan;
            set { gridSpan = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => vm.SearchText;
            set { vm.SearchText = value; OnPropertyChanged(); }
        }

        public bool IsIndexing => vm.IsIndexing;
        public int IndexedCount => vm.IndexedCount;
        public int IndexProcessed => vm.IndexProcessed;
        public int IndexTotal => vm.IndexTotal;
        public double IndexRatio => vm.IndexRatio;
        public string IndexStatus => vm.IndexStatus;
        public bool HasMediaPermission => vm.HasMediaPermission;
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

        public List<Models.VideoItem> Videos => vm.Videos;

        async Task OpenSourcesAsync()
        {
            await page.Navigation.PushAsync(page.Handler!.MauiContext!.Services.GetService<Views.SourcesPage>()!);
        }

        public Task ApplyGridSpanAsync()
        {
            // Compute grid span based on display width and mode.
            var mode = deviceMode.GetUiMode();

            var width = page.Width;
            if (width <= 0)
                width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

            var targetTile = mode == UiMode.Tv ? 260 : (mode == UiMode.Tablet ? 220 : 180);
            var span = Math.Max(2, (int)(width / targetTile));

            GridSpan = Math.Min(8, span);
            return Task.CompletedTask;
        }
    }
}
