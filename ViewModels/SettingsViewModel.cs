using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Helpers;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDb db;
    private readonly IDialogService dialogService;
    private readonly IBackupRestoreService backupRestoreService;
    private readonly ModelFileService modelFileService;
    private readonly PeopleRecognitionService peopleRecognitionService;
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    [ObservableProperty] private bool allowFileChanges;
    [ObservableProperty] private bool canDownloadPeopleModels;
    [ObservableProperty] private DateTime dateFilterFrom;
    [ObservableProperty] private DateTime dateFilterTo;
    [ObservableProperty] private string documentExtensionsText = string.Empty;
    [ObservableProperty] private string indexStatusMessage = string.Empty;
    [ObservableProperty] private string indexStatusTitle = string.Empty;
    private bool isApplyingLocationToggle;
    private bool isApplyingNeedsReindex;
    [ObservableProperty] private bool isDatabaseResetting;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isDocumentsIndexed;
    [ObservableProperty] private bool isGraphicsIndexed;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    [ObservableProperty] private bool isLocationEnabled;
    [ObservableProperty] private bool isPeopleModelsDownloading;
    [ObservableProperty] private bool isPeopleTaggingEnabled;
    [ObservableProperty] private bool isPhotosIndexed;
    private bool isApplyingSearchScope;
    [ObservableProperty] private bool isSearchAlbumsEnabled;
    [ObservableProperty] private bool isSearchNameEnabled;
    [ObservableProperty] private bool isSearchPeopleEnabled;
    [ObservableProperty] private bool isVideosIndexed;
    [ObservableProperty] private bool needsReindex;
    [ObservableProperty] private string peopleModelsDetailText = string.Empty;

    [ObservableProperty] private string peopleModelsStatusText = string.Empty;
    [ObservableProperty] private string photoExtensionsText = string.Empty;
    [ObservableProperty] private SortOption? selectedSortOption;

    [ObservableProperty] private ThemeOption? selectedTheme;
    [ObservableProperty] private string videoExtensionsText = string.Empty;

    public SettingsViewModel(AppSettingsService settingsService, ModelFileService modelFileService,
        PeopleRecognitionService peopleRecognitionService, AppDb db, ISourceService sourceService,
        IBackupRestoreService backupRestoreService, IDialogService dialogService)
    {
        this.settingsService = settingsService;
        this.modelFileService = modelFileService;
        this.peopleRecognitionService = peopleRecognitionService;
        this.db = db;
        this.sourceService = sourceService;
        this.backupRestoreService = backupRestoreService;
        this.dialogService = dialogService;
        ThemeOptions = new[]
        {
            new ThemeOption("light", AppResources.ThemeLight),
            new ThemeOption("dark", AppResources.ThemeDark)
        };

        SortOptions = new[]
        {
            new SortOption("name", AppResources.SortName),
            new SortOption("date", AppResources.SortDate),
            new SortOption("duration", AppResources.SortDuration)
        };

        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Key == settingsService.ThemePreference)
                        ?? ThemeOptions.First();
        ApplyTheme(SelectedTheme.Key);

        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Key == settingsService.SelectedSortOptionKey)
                             ?? SortOptions.First();
        IsDateFilterEnabled = settingsService.DateFilterEnabled;
        DateFilterFrom = settingsService.DateFilterFrom;
        DateFilterTo = settingsService.DateFilterTo;
        NeedsReindex = settingsService.NeedsReindex;
        IsIndexing = settingsService.IsIndexing;
        IsInternalPlayerEnabled = settingsService.InternalPlayerEnabled;

        var indexed = settingsService.IndexedMediaTypes;
        IsVideosIndexed = indexed.HasFlag(MediaType.Videos);
        IsPhotosIndexed = indexed.HasFlag(MediaType.Photos);
        IsGraphicsIndexed = indexed.HasFlag(MediaType.Graphics);
        IsDocumentsIndexed = indexed.HasFlag(MediaType.Documents);

        VideoExtensionsText = settingsService.VideoExtensions;
        PhotoExtensionsText = settingsService.PhotoExtensions;
        DocumentExtensionsText = settingsService.DocumentExtensions;
        AllowFileChanges = settingsService.AllowFileChanges;
        IsPeopleTaggingEnabled = settingsService.PeopleTaggingEnabled;
        var searchScope = settingsService.SearchScope == SearchScope.None
            ? SearchScope.All
            : settingsService.SearchScope;
        isApplyingSearchScope = true;
        IsSearchNameEnabled = searchScope.HasFlag(SearchScope.Name);
        IsSearchPeopleEnabled = searchScope.HasFlag(SearchScope.People);
        IsSearchAlbumsEnabled = searchScope.HasFlag(SearchScope.Albums);
        isApplyingSearchScope = false;
        isApplyingLocationToggle = true;
        IsLocationEnabled = settingsService.LocationsEnabled;
        isApplyingLocationToggle = false;

        RefreshPeopleModelsStatus();
        UpdateIndexStatusState();

        settingsService.IsIndexingChanged += (_, value) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsIndexing = value;
                UpdateIndexStatusState();
            });

        settingsService.NeedsReindexChanged += (_, value) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                isApplyingNeedsReindex = true;
                NeedsReindex = value;
                isApplyingNeedsReindex = false;
                UpdateIndexStatusState();
            });

    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public IReadOnlyList<SortOption> SortOptions { get; }
    public string ErrorLogPath => ErrorLog.LogPath;

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value == null)
            return;

        settingsService.ThemePreference = value.Key;
        ApplyTheme(value.Key);
    }

    partial void OnSelectedSortOptionChanged(SortOption? value)
    {
        if (value == null)
            return;

        settingsService.SelectedSortOptionKey = value.Key;
    }

    partial void OnIsDateFilterEnabledChanged(bool value)
    {
        settingsService.DateFilterEnabled = value;
    }

    partial void OnDateFilterFromChanged(DateTime value)
    {
        if (value > DateFilterTo)
            DateFilterTo = value;

        settingsService.DateFilterFrom = value;
    }

    partial void OnDateFilterToChanged(DateTime value)
    {
        if (value < DateFilterFrom)
            DateFilterFrom = value;

        settingsService.DateFilterTo = value;
    }

    partial void OnNeedsReindexChanged(bool value)
    {
        if (!isApplyingNeedsReindex)
            settingsService.NeedsReindex = value;

        UpdateIndexStatusState();
    }

    partial void OnIsInternalPlayerEnabledChanged(bool value)
    {
        settingsService.InternalPlayerEnabled = value;
    }

    partial void OnIsVideosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, () => IsVideosIndexed = true, IsPhotosIndexed, IsGraphicsIndexed,
                IsDocumentsIndexed))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsPhotosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, () => IsPhotosIndexed = true, IsVideosIndexed, IsGraphicsIndexed,
                IsDocumentsIndexed))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsGraphicsIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, () => IsGraphicsIndexed = true, IsVideosIndexed, IsPhotosIndexed,
                IsDocumentsIndexed))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsDocumentsIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, () => IsDocumentsIndexed = true, IsVideosIndexed, IsPhotosIndexed,
                IsGraphicsIndexed))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnVideoExtensionsTextChanged(string value)
    {
        settingsService.VideoExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnPhotoExtensionsTextChanged(string value)
    {
        settingsService.PhotoExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnDocumentExtensionsTextChanged(string value)
    {
        settingsService.DocumentExtensions = value;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnAllowFileChangesChanged(bool value)
    {
        settingsService.AllowFileChanges = value;
    }

    partial void OnIsPeopleTaggingEnabledChanged(bool value)
    {
        settingsService.PeopleTaggingEnabled = value;

        // Best-effort: if the user enables people tagging, try to ensure the models are available.
        if (value)
            _ = DownloadPeopleModelsAsync(CancellationToken.None);
    }

    partial void OnIsSearchNameEnabledChanged(bool value)
    {
        if (isApplyingSearchScope)
            return;

        if (!EnsureAtLeastOneSearchScope(value, () => IsSearchNameEnabled = true, IsSearchPeopleEnabled,
                IsSearchAlbumsEnabled))
            return;

        ApplySearchScope();
    }

    partial void OnIsSearchPeopleEnabledChanged(bool value)
    {
        if (isApplyingSearchScope)
            return;

        if (!EnsureAtLeastOneSearchScope(value, () => IsSearchPeopleEnabled = true, IsSearchNameEnabled,
                IsSearchAlbumsEnabled))
            return;

        ApplySearchScope();
    }

    partial void OnIsSearchAlbumsEnabledChanged(bool value)
    {
        if (isApplyingSearchScope)
            return;

        if (!EnsureAtLeastOneSearchScope(value, () => IsSearchAlbumsEnabled = true, IsSearchNameEnabled,
                IsSearchPeopleEnabled))
            return;

        ApplySearchScope();
    }

    partial void OnIsLocationEnabledChanged(bool value)
    {
        if (isApplyingLocationToggle)
        {
            settingsService.LocationsEnabled = value;
            return;
        }

        _ = HandleLocationToggleAsync(value);
    }

    private async Task HandleLocationToggleAsync(bool value)
    {
        if (!value)
        {
            settingsService.LocationsEnabled = false;
            return;
        }

        var accepted = await dialogService.DisplayAlertAsync(
            AppResources.LocationOptInTitle,
            AppResources.LocationOptInMessage,
            AppResources.LocationOptInAccept,
            AppResources.LocationOptInDecline);

        if (!accepted)
        {
            isApplyingLocationToggle = true;
            IsLocationEnabled = false;
            isApplyingLocationToggle = false;
            return;
        }

        settingsService.LocationsEnabled = true;
        if (!NeedsReindex)
            NeedsReindex = true;
    }

    partial void OnIsIndexingChanged(bool value)
    {
        UpdateIndexStatusState();
    }

    private void UpdateIndexStatusState()
    {
        var state = IsIndexing ? IndexingState.Running :
            NeedsReindex ? IndexingState.NeedsReindex :
            IndexingState.Ready;

        IndexStatusTitle = state switch
        {
            IndexingState.Running => AppResources.IndexStatusRunningTitle,
            IndexingState.NeedsReindex => AppResources.IndexStatusNeededTitle,
            _ => AppResources.IndexStatusReadyTitle
        };

        IndexStatusMessage = state switch
        {
            IndexingState.Running => AppResources.IndexStatusRunningMessage,
            IndexingState.NeedsReindex => AppResources.IndexStatusNeededMessage,
            _ => AppResources.IndexStatusReadyMessage
        };
    }

    [RelayCommand]
    private async Task DownloadPeopleModelsAsync(CancellationToken ct)
    {
        if (IsPeopleModelsDownloading)
            return;

        try
        {
            IsPeopleModelsDownloading = true;
            CanDownloadPeopleModels = false;
            PeopleModelsStatusText = AppResources.SettingsPeopleModelsStatusDownloading;

            var snapshot = await modelFileService.EnsureAllModelsAsync(ct).ConfigureAwait(false);
            // After the files are present, attempt to load the ONNX sessions so detection works immediately.
            await peopleRecognitionService.WarmupModelsAsync(ct).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => ApplyPeopleModelsSnapshot(snapshot));
        }
        catch
        {
            // Status is taken from the snapshot; do not throw into UI.
            await MainThread.InvokeOnMainThreadAsync(RefreshPeopleModelsStatus);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsPeopleModelsDownloading = false;
                RefreshPeopleModelsStatus();
            });
        }
    }

    [RelayCommand]
    private async Task ResetDatabaseAsync()
    {
        if (IsDatabaseResetting)
            return;

        var confirmed = await dialogService.DisplayAlertAsync(
            AppResources.SettingsDatabaseResetConfirmTitle,
            AppResources.SettingsDatabaseResetConfirmMessage,
            AppResources.SettingsDatabaseResetConfirmAccept,
            AppResources.CancelButton);

        if (!confirmed)
            return;

        var keepSources = await dialogService.DisplayAlertAsync(
            AppResources.SettingsDatabaseResetKeepSourcesTitle,
            AppResources.SettingsDatabaseResetKeepSourcesMessage,
            AppResources.SettingsDatabaseResetKeepSourcesAccept,
            AppResources.SettingsDatabaseResetKeepSourcesRemove);

        var sources = keepSources ? await sourceService.GetSourcesAsync().ConfigureAwait(false) : null;

        IsDatabaseResetting = true;

        try
        {
            await Task.Run(async () =>
            {
                await db.ResetAsync().ConfigureAwait(false);
                if (keepSources && sources is { Count: > 0 })
                    foreach (var source in sources)
                        await sourceService.UpsertAsync(source).ConfigureAwait(false);

                await sourceService.EnsureDefaultSourceAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => NeedsReindex = true);
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.SettingsDatabaseResetCompletedTitle,
                    AppResources.SettingsDatabaseResetCompletedMessage,
                    AppResources.OkButton));
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.SettingsDatabaseResetFailedTitle,
                    AppResources.SettingsDatabaseResetFailedMessage,
                    AppResources.OkButton));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsDatabaseResetting = false);
        }
    }

    [RelayCommand]
    private Task ExportBackupAsync(CancellationToken ct)
    {
        return backupRestoreService.ExportBackupAsync(ct);
    }

    [RelayCommand]
    private async Task ImportBackupAsync(CancellationToken ct)
    {
        await backupRestoreService.ImportBackupAsync(ct).ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(ReloadFromSettingsService);
    }

    private void ReloadFromSettingsService()
    {
        // Refresh the most relevant UI-bound values after a restore.
        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Key == settingsService.ThemePreference)
                        ?? ThemeOptions.First();

        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Key == settingsService.SelectedSortOptionKey)
                             ?? SortOptions.First();

        IsDateFilterEnabled = settingsService.DateFilterEnabled;
        DateFilterFrom = settingsService.DateFilterFrom;
        DateFilterTo = settingsService.DateFilterTo;

        isApplyingNeedsReindex = true;
        NeedsReindex = settingsService.NeedsReindex;
        isApplyingNeedsReindex = false;

        IsIndexing = settingsService.IsIndexing;
        IsInternalPlayerEnabled = settingsService.InternalPlayerEnabled;

        var indexed = settingsService.IndexedMediaTypes;
        IsVideosIndexed = indexed.HasFlag(MediaType.Videos);
        IsPhotosIndexed = indexed.HasFlag(MediaType.Photos);
        IsGraphicsIndexed = indexed.HasFlag(MediaType.Graphics);
        IsDocumentsIndexed = indexed.HasFlag(MediaType.Documents);

        VideoExtensionsText = settingsService.VideoExtensions;
        PhotoExtensionsText = settingsService.PhotoExtensions;
        DocumentExtensionsText = settingsService.DocumentExtensions;
        AllowFileChanges = settingsService.AllowFileChanges;
        IsPeopleTaggingEnabled = settingsService.PeopleTaggingEnabled;

        var searchScope = settingsService.SearchScope == SearchScope.None
            ? SearchScope.All
            : settingsService.SearchScope;
        isApplyingSearchScope = true;
        IsSearchNameEnabled = searchScope.HasFlag(SearchScope.Name);
        IsSearchPeopleEnabled = searchScope.HasFlag(SearchScope.People);
        IsSearchAlbumsEnabled = searchScope.HasFlag(SearchScope.Albums);
        isApplyingSearchScope = false;

        isApplyingLocationToggle = true;
        IsLocationEnabled = settingsService.LocationsEnabled;
        isApplyingLocationToggle = false;

        RefreshPeopleModelsStatus();
        UpdateIndexStatusState();
    }

    private void RefreshPeopleModelsStatus()
    {
        ApplyPeopleModelsSnapshot(modelFileService.GetStatusSnapshot());
    }

    [RelayCommand]
    private async Task ShowErrorLogAsync()
    {
        var log = await ErrorLog.ReadLogAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(log))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.ErrorLogTitle,
                    AppResources.ErrorLogEmptyMessage,
                    AppResources.OkButton));
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
            dialogService.DisplayAlertAsync(
                AppResources.ErrorLogTitle,
                log,
                AppResources.OkButton));
    }

    [RelayCommand]
    private async Task ShareErrorLogAsync()
    {
        var log = await ErrorLog.ReadLogAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(log))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.ErrorLogTitle,
                    AppResources.ErrorLogEmptyMessage,
                    AppResources.OkButton));
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
            Share.RequestAsync(new ShareTextRequest
            {
                Title = AppResources.ErrorLogShareTitle,
                Text = log
            }));
    }

    [RelayCommand]
    private async Task CopyErrorLogAsync()
    {
        var log = await ErrorLog.ReadLogAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(log))
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.ErrorLogTitle,
                    AppResources.ErrorLogEmptyMessage,
                    AppResources.OkButton));
            return;
        }

        try
        {
            // Clipboard APIs can fail on Windows (WinRT clipboard restrictions / background thread access).
            // Always execute on the UI thread and fall back to showing the log if the clipboard is unavailable.
            await MainThread.InvokeOnMainThreadAsync(async () => { await Clipboard.Default.SetTextAsync(log); });

            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.ErrorLogTitle,
                    AppResources.ErrorLogCopiedMessage,
                    AppResources.OkButton));
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "CopyErrorLogAsync");
            await MainThread.InvokeOnMainThreadAsync(() =>
                dialogService.DisplayAlertAsync(
                    AppResources.ErrorLogTitle,
                    AppResources.ErrorLogCopyFailedMessage,
                    AppResources.OkButton));
        }
    }

    [RelayCommand]
    private async Task ClearErrorLogAsync()
    {
        await ErrorLog.ClearLogAsync().ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() =>
            dialogService.DisplayAlertAsync(
                AppResources.ErrorLogTitle,
                AppResources.ErrorLogClearedMessage,
                AppResources.OkButton));
    }

    private void ApplyPeopleModelsSnapshot(ModelFileService.ModelStatusSnapshot s)
    {
        var ready = s.YuNet == ModelFileService.ModelStatus.Ready
                    && s.SFace == ModelFileService.ModelStatus.Ready;

        if (ready)
        {
            PeopleModelsStatusText = AppResources.SettingsPeopleModelsStatusReady;
            PeopleModelsDetailText = string.Format(AppResources.SettingsPeopleModelsFolderFormat, s.ModelsDirectory);
            CanDownloadPeopleModels = false;
            return;
        }

        PeopleModelsStatusText = AppResources.SettingsPeopleModelsStatusMissing;

        var parts = new List<string>();

        static string FormatError(string? error)
        {
            return string.IsNullOrWhiteSpace(error) ? string.Empty : $" ({error})";
        }

        if (s.YuNet != ModelFileService.ModelStatus.Ready)
            parts.Add(string.Format(AppResources.SettingsPeopleModelsFileFormat, "face_detection_yunet_2023mar.onnx",
                FormatError(s.YuNetError)));
        if (s.SFace != ModelFileService.ModelStatus.Ready)
            parts.Add(string.Format(AppResources.SettingsPeopleModelsFileFormat, "face_recognition_sface_2021dec.onnx",
                FormatError(s.SFaceError)));

        var files = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        PeopleModelsDetailText =
            string.Format(AppResources.SettingsPeopleModelsMissingDetailFormat, s.ModelsDirectory, files);

        CanDownloadPeopleModels = true;
    }

    private static string FormatError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return string.Empty;

        // Keep this short; full exception details are not user-friendly in settings.
        return $" ({error.Trim()})";
    }

    private static void ApplyTheme(string themeKey)
    {
        var theme = themeKey == "light" ? AppTheme.Light : AppTheme.Dark;
        App.ApplyTheme(theme);
    }

    private void ApplySearchScope()
    {
        var scope = SearchScope.None;
        if (IsSearchNameEnabled)
            scope |= SearchScope.Name;
        if (IsSearchPeopleEnabled)
            scope |= SearchScope.People;
        if (IsSearchAlbumsEnabled)
            scope |= SearchScope.Albums;

        settingsService.SearchScope = scope == SearchScope.None ? SearchScope.All : scope;
    }

    private void ApplyIndexedMediaTypes()
    {
        settingsService.IndexedMediaTypes = BuildIndexedMediaTypes();
        settingsService.NeedsReindex = true;
    }

    private MediaType BuildIndexedMediaTypes()
    {
        var mediaTypes = MediaType.None;
        if (IsVideosIndexed)
            mediaTypes |= MediaType.Videos;
        if (IsPhotosIndexed)
            mediaTypes |= MediaType.Photos;
        if (IsGraphicsIndexed)
            mediaTypes |= MediaType.Graphics;
        if (IsDocumentsIndexed)
            mediaTypes |= MediaType.Documents;
        return mediaTypes;
    }

    private static bool EnsureAtLeastOneIndexed(bool currentValue, Action restore, params bool[] otherFlags)
    {
        if (!currentValue && otherFlags.All(flag => !flag))
        {
            restore();
            return false;
        }

        return true;
    }

    private static bool EnsureAtLeastOneSearchScope(bool currentValue, Action restore, params bool[] otherFlags)
    {
        if (!currentValue && otherFlags.All(flag => !flag))
        {
            restore();
            return false;
        }

        return true;
    }

    public sealed record ThemeOption(string Key, string Display);
}
