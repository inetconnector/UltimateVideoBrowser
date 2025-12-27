using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using System.Globalization;
using System.IO;

namespace UltimateVideoBrowser.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IndexService indexService;
    private readonly AppSettingsService settingsService;
    private readonly PermissionService permissionService;
    private readonly PlaybackService playbackService;
    private readonly IFileExportService fileExportService;
    private readonly ISourceService sourceService;
    private readonly ThumbnailService thumbnailService;
    [ObservableProperty] private string activeSourceId = "";
    [ObservableProperty] private int enabledSourceCount;
    [ObservableProperty] private bool hasMediaPermission = true;
    [ObservableProperty] private int indexedCount;
    [ObservableProperty] private int indexProcessed;
    [ObservableProperty] private double indexRatio;
    [ObservableProperty] private string indexStatus = "";
    [ObservableProperty] private int indexTotal;
    [ObservableProperty] private string indexCurrentFolder = "";
    [ObservableProperty] private string indexCurrentFile = "";
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private DateTime dateFilterFrom = DateTime.Today.AddMonths(-1);
    [ObservableProperty] private DateTime dateFilterTo = DateTime.Today;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private SortOption? selectedSortOption;
    [ObservableProperty] private string sourcesSummary = "";
    [ObservableProperty] private int totalSourceCount;
    [ObservableProperty] private int videoCount;
    [ObservableProperty] private List<TimelineEntry> timelineEntries = new();

    private CancellationTokenSource? indexCts;
    private CancellationTokenSource? thumbCts;

    [ObservableProperty] private List<VideoItem> videos = new();

    public MainViewModel(
        ISourceService sourceService,
        IndexService indexService,
        AppSettingsService settingsService,
        ThumbnailService thumbnailService,
        PlaybackService playbackService,
        PermissionService permissionService,
        IFileExportService fileExportService)
    {
        this.sourceService = sourceService;
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.thumbnailService = thumbnailService;
        this.playbackService = playbackService;
        this.permissionService = permissionService;
        this.fileExportService = fileExportService;

        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };
        SelectedSortOption = SortOptions.FirstOrDefault();
    }

    public IReadOnlyList<SortOption> SortOptions { get; }

    public async Task InitializeAsync()
    {
        await sourceService.EnsureDefaultSourceAsync();

        ApplySavedSettings();
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);

        HasMediaPermission = await permissionService.CheckMediaReadAsync();

        if (!HasMediaPermission)
        {
            Videos = new List<VideoItem>();
            return;
        }

        await RefreshAsync();
        StartThumbnailPipeline();

        if (settingsService.NeedsReindex)
            _ = RunIndexAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var sources = await sourceService.GetSourcesAsync();
        ActiveSourceId = NormalizeActiveSourceId(sources, ActiveSourceId);
        await UpdateSourceStatsAsync(sources);

        var sortKey = SelectedSortOption?.Key ?? "name";
        var dateFrom = IsDateFilterEnabled ? DateFilterFrom : null;
        var dateTo = IsDateFilterEnabled ? DateFilterTo : null;
        var videos = await indexService.QueryAsync(SearchText, ActiveSourceId, sortKey, dateFrom, dateTo);

        if (string.IsNullOrWhiteSpace(ActiveSourceId))
        {
            var enabledIds = sources.Where(s => s.IsEnabled).Select(s => s.Id).ToHashSet();
            videos = videos.Where(v => v.SourceId != null && enabledIds.Contains(v.SourceId)).ToList();
        }

        Videos = videos;
        StartThumbnailPipeline();
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

        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (!HasMediaPermission)
        {
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
            await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.DisplayAlert(AppResources.PermissionTitle, AppResources.PermissionMessage,
                    AppResources.OkButton));
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
            var sources = (await sourceService.GetSourcesAsync()).Where(s => s.IsEnabled).ToList();
            var lastRefresh = DateTime.UtcNow;
            var lastInserted = 0;
            var progress = new Progress<IndexProgress>(p =>
            {
                IndexProcessed = p.Processed;
                IndexTotal = p.Total;
                IndexRatio = p.Ratio;
                IndexStatus = string.Format(AppResources.IndexingStatusFormat, p.SourceName, p.Processed, p.Total);
                UpdateIndexLocation(p.SourceName, p.CurrentPath);
                IndexedCount = p.Inserted;
                if (p.Inserted > lastInserted &&
                    DateTime.UtcNow - lastRefresh > TimeSpan.FromMilliseconds(400))
                {
                    lastInserted = p.Inserted;
                    lastRefresh = DateTime.UtcNow;
                    _ = RefreshAsync();
                }
            });
            indexCts?.Cancel();
            indexCts?.Dispose();
            indexCts = new CancellationTokenSource();

            await indexService.IndexSourcesAsync(sources, progress, indexCts.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            completed = false;
        }
        finally
        {
            indexCts?.Dispose();
            indexCts = null;
            IsIndexing = false;
            IndexStatus = "";
            IndexRatio = 0;
            IndexCurrentFolder = "";
            IndexCurrentFile = "";
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
    public async Task RequestPermissionAsync()
    {
        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
        if (HasMediaPermission)
            await RunIndexAsync();
    }

    private void StartThumbnailPipeline()
    {
        thumbCts?.Cancel();
        thumbCts?.Dispose();
        thumbCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                var ct = thumbCts.Token;
                // Generate thumbnails for the first N visible items quickly, then continue.
                foreach (var item in Videos.Take(80))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
                        continue;

                    var p = await thumbnailService.EnsureThumbnailAsync(item, ct);
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        MainThread.BeginInvokeOnMainThread(() => item.ThumbnailPath = p);
                    }
                }
            }
            catch
            {
                // Ignore cancellation or retrieval failures.
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
        if (string.IsNullOrWhiteSpace(activeSourceId))
            return "";

        var exists = sources.Any(s => s.Id == activeSourceId && s.IsEnabled);
        return exists ? activeSourceId : "";
    }

    partial void OnVideosChanged(List<VideoItem> value)
    {
        VideoCount = value?.Count ?? 0;
        TimelineEntries = BuildTimelineEntries(value);
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
}
