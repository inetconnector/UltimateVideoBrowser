using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;

namespace UltimateVideoBrowser.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDb db;
    private readonly IDialogService dialogService;
    private readonly ModelFileService modelFileService;
    private readonly PeopleRecognitionService peopleRecognitionService;
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    [ObservableProperty] private bool allowFileChanges;
    [ObservableProperty] private bool canDownloadPeopleModels;
    [ObservableProperty] private DateTime dateFilterFrom;
    [ObservableProperty] private DateTime dateFilterTo;
    [ObservableProperty] private string documentExtensionsText = string.Empty;
    [ObservableProperty] private bool isDatabaseResetting;
    [ObservableProperty] private bool isDateFilterEnabled;
    [ObservableProperty] private bool isDocumentsIndexed;
    [ObservableProperty] private bool isInternalPlayerEnabled;
    [ObservableProperty] private bool isLocationEnabled;
    [ObservableProperty] private bool isPeopleModelsDownloading;
    [ObservableProperty] private bool isPeopleTaggingEnabled;
    [ObservableProperty] private bool isPhotosIndexed;
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
        IDialogService dialogService)
    {
        this.settingsService = settingsService;
        this.modelFileService = modelFileService;
        this.peopleRecognitionService = peopleRecognitionService;
        this.db = db;
        this.sourceService = sourceService;
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
        IsInternalPlayerEnabled = settingsService.InternalPlayerEnabled;

        var indexed = settingsService.IndexedMediaTypes;
        IsVideosIndexed = indexed.HasFlag(MediaType.Videos);
        IsPhotosIndexed = indexed.HasFlag(MediaType.Photos);
        IsDocumentsIndexed = indexed.HasFlag(MediaType.Documents);

        VideoExtensionsText = settingsService.VideoExtensions;
        PhotoExtensionsText = settingsService.PhotoExtensions;
        DocumentExtensionsText = settingsService.DocumentExtensions;
        AllowFileChanges = settingsService.AllowFileChanges;
        IsPeopleTaggingEnabled = settingsService.PeopleTaggingEnabled;
        IsLocationEnabled = settingsService.LocationsEnabled;

        RefreshPeopleModelsStatus();
    }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }
    public IReadOnlyList<SortOption> SortOptions { get; }

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
        settingsService.NeedsReindex = value;
    }

    partial void OnIsInternalPlayerEnabledChanged(bool value)
    {
        settingsService.InternalPlayerEnabled = value;
    }

    partial void OnIsVideosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsPhotosIndexed, IsDocumentsIndexed, () => IsVideosIndexed = true))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsPhotosIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsVideosIndexed, IsDocumentsIndexed, () => IsPhotosIndexed = true))
            return;

        ApplyIndexedMediaTypes();
    }

    partial void OnIsDocumentsIndexedChanged(bool value)
    {
        if (!EnsureAtLeastOneIndexed(value, IsVideosIndexed, IsPhotosIndexed, () => IsDocumentsIndexed = true))
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

    partial void OnIsLocationEnabledChanged(bool value)
    {
        settingsService.LocationsEnabled = value;
        if (value && !NeedsReindex)
            NeedsReindex = true;
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

        IsDatabaseResetting = true;

        try
        {
            await db.ResetAsync().ConfigureAwait(false);
            await sourceService.EnsureDefaultSourceAsync().ConfigureAwait(false);
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

    private void RefreshPeopleModelsStatus()
    {
        ApplyPeopleModelsSnapshot(modelFileService.GetStatusSnapshot());
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
        if (IsDocumentsIndexed)
            mediaTypes |= MediaType.Documents;
        return mediaTypes;
    }

    private static bool EnsureAtLeastOneIndexed(bool currentValue, bool otherOne, bool otherTwo, Action restore)
    {
        if (!currentValue && !otherOne && !otherTwo)
        {
            restore();
            return false;
        }

        return true;
    }

    public sealed record ThemeOption(string Key, string Display);
}
