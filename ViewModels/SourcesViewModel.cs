using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    private readonly IDialogService dialogService;
    private readonly IFolderPickerService folderPickerService;
    private readonly PermissionService permissionService;
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    private List<MediaSource> allSources = new();
    [ObservableProperty] private bool hasMediaPermission;

    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private bool supportsManualPath;

    public SourcesViewModel(
        ISourceService sourceService,
        PermissionService permissionService,
        IFolderPickerService folderPickerService,
        IDialogService dialogService,
        AppSettingsService settingsService)
    {
        this.sourceService = sourceService;
        this.permissionService = permissionService;
        this.folderPickerService = folderPickerService;
        this.dialogService = dialogService;
        this.settingsService = settingsService;
        SupportsManualPath = DeviceInfo.Platform == DevicePlatform.WinUI;
    }

    public async Task InitializeAsync()
    {
        HasMediaPermission = await permissionService.CheckMediaReadAsync();
        allSources = await sourceService.GetSourcesAsync();
        ApplySystemSourceVisibility();
    }

    [RelayCommand]
    public async Task ToggleAsync(MediaSource src)
    {
        if (src.IsSystemSource)
        {
            await ToggleSystemSourceAsync(src);
            return;
        }

        src.IsEnabled = !src.IsEnabled;
        await sourceService.UpsertAsync(src);
        settingsService.NeedsReindex = true;
        await InitializeAsync();
    }

    [RelayCommand]
    public async Task RequestPermissionAsync()
    {
        HasMediaPermission = await permissionService.EnsureMediaReadAsync();
    }

    [RelayCommand]
    public async Task AddSourceAsync()
    {
        await AddSourcesAsync();
    }

    [RelayCommand]
    public async Task AddPathAsync()
    {
        await AddSourcesAsync();
    }

    private async Task AddSourcesAsync()
    {
        while (true)
        {
            var results = await folderPickerService.PickFoldersAsync();
            if (results.Count == 0)
                return;

            var addedAny = false;
            foreach (var result in results)
                if (await AddSourceFromPickAsync(result))
                    addedAny = true;

            if (!addedAny)
                return;

            var addMore = await dialogService.DisplayAlertAsync(
                AppResources.AddAnotherFolderTitle,
                AppResources.AddAnotherFolderMessage,
                AppResources.AddAnotherFolderConfirm,
                AppResources.AddAnotherFolderCancel);

            if (!addMore)
                return;
        }
    }

    private async Task<bool> AddSourceFromPickAsync(FolderPickResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Path))
            return false;

        var path = result.Path.Trim();
        var isContentUri = path.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
        if (!isContentUri && !Directory.Exists(path))
        {
            await dialogService.DisplayAlertAsync(
                AppResources.PathInvalidTitle,
                AppResources.PathInvalidMessage,
                AppResources.OkButton);
            return false;
        }

        var existing = (await sourceService.GetSourcesAsync()).FirstOrDefault(s => s.LocalFolderPath == path);
        if (existing != null)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.SourceExistsTitle,
                AppResources.SourceExistsMessage,
                AppResources.OkButton);
            return false;
        }

        var suggestedName = string.IsNullOrWhiteSpace(result.DisplayName)
            ? AppResources.NewSourceDefaultName
            : result.DisplayName;

        var displayName = await dialogService.DisplayPromptAsync(
            AppResources.NewSourceTitle,
            AppResources.NewSourcePrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            suggestedName,
            60,
            Keyboard.Text,
            suggestedName);

        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        var src = new MediaSource
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName.Trim(),
            LocalFolderPath = path,
            AccessToken = result.AccessToken,
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };

        await sourceService.UpsertAsync(src);
        settingsService.NeedsReindex = true;
        await InitializeAsync();
        return true;
    }

    [RelayCommand]
    public async Task RemoveAsync(MediaSource src)
    {
        if (src.IsSystemSource)
            return;

        var ok = await dialogService.DisplayAlertAsync(
            AppResources.RemoveSourceTitle,
            string.Format(AppResources.RemoveSourceMessage, src.DisplayName),
            AppResources.RemoveSourceConfirm,
            AppResources.NewSourceCancel);

        if (!ok)
            return;

        await sourceService.DeleteAsync(src);
        settingsService.NeedsReindex = true;
        await InitializeAsync();
    }

    private async Task ToggleSystemSourceAsync(MediaSource src)
    {
        var isEnabling = !src.IsEnabled;
        src.IsEnabled = isEnabling;
        await sourceService.UpsertAsync(src);

        var sources = await sourceService.GetSourcesAsync();
        if (isEnabling)
        {
            var storedEnabledIds = settingsService.DeviceMediaChildEnabledIds;
            if (storedEnabledIds.Count > 0)
            {
                var enabledSet = new HashSet<string>(storedEnabledIds, StringComparer.OrdinalIgnoreCase);
                foreach (var child in sources.Where(source => !source.IsSystemSource))
                {
                    var shouldEnable = enabledSet.Contains(child.Id);
                    if (child.IsEnabled == shouldEnable)
                        continue;

                    child.IsEnabled = shouldEnable;
                    await sourceService.UpsertAsync(child);
                }
            }

            settingsService.DeviceMediaChildEnabledIds = Array.Empty<string>();
        }
        else
        {
            var enabledIds = sources
                .Where(source => !source.IsSystemSource && source.IsEnabled)
                .Select(source => source.Id)
                .ToList();

            settingsService.DeviceMediaChildEnabledIds = enabledIds;

            foreach (var child in sources.Where(source => !source.IsSystemSource))
            {
                if (!child.IsEnabled)
                    continue;

                child.IsEnabled = false;
                await sourceService.UpsertAsync(child);
            }
        }

        settingsService.NeedsReindex = true;
        await InitializeAsync();
    }

    private void ApplySystemSourceVisibility()
    {
        var systemSource = allSources.FirstOrDefault(source => source.IsSystemSource);
        if (systemSource is { IsEnabled: false })
        {
            Sources = allSources
                .Where(source => source.IsSystemSource || !IsDeviceLocalSource(source))
                .ToList();
            return;
        }

        Sources = allSources;
    }

    private static bool IsDeviceLocalSource(MediaSource source)
    {
        if (source.IsSystemSource)
            return false;

        var path = source.LocalFolderPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (DeviceInfo.Platform == DevicePlatform.Android)
            return path.StartsWith("content://", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/storage", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith("/sdcard", StringComparison.OrdinalIgnoreCase);

        return false;
    }
}
