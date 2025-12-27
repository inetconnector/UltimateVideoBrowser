using System;
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
    private readonly ISourceService sourceService;
    private readonly AppSettingsService settingsService;
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
        Sources = await sourceService.GetSourcesAsync();
    }

    [RelayCommand]
    public async Task ToggleAsync(MediaSource src)
    {
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
}
