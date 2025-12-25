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
    [ObservableProperty] private bool hasMediaPermission;

    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private bool supportsManualPath;

    public SourcesViewModel(
        ISourceService sourceService,
        PermissionService permissionService,
        IFolderPickerService folderPickerService,
        IDialogService dialogService)
    {
        this.sourceService = sourceService;
        this.permissionService = permissionService;
        this.folderPickerService = folderPickerService;
        this.dialogService = dialogService;
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
        var results = await folderPickerService.PickFoldersAsync();
        if (results.Count == 0)
            return;

        foreach (var result in results)
        {
            await AddSourceFromPickAsync(result);
        }
    }

    [RelayCommand]
    public async Task AddPathAsync()
    {
        var results = await folderPickerService.PickFoldersAsync();
        if (results.Count == 0)
            return;

        foreach (var result in results)
        {
            await AddSourceFromPickAsync(result);
        }
    }

    private async Task AddSourceFromPickAsync(FolderPickResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Path))
            return;

        var path = result.Path.Trim();
        if (!Directory.Exists(path))
        {
            await dialogService.DisplayAlertAsync(
                AppResources.PathInvalidTitle,
                AppResources.PathInvalidMessage,
                AppResources.OkButton);
            return;
        }

        var existing = (await sourceService.GetSourcesAsync()).FirstOrDefault(s => s.LocalFolderPath == path);
        if (existing != null)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.SourceExistsTitle,
                AppResources.SourceExistsMessage,
                AppResources.OkButton);
            return;
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
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(displayName))
            return;

        var src = new MediaSource
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName.Trim(),
            LocalFolderPath = path,
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };

        await sourceService.UpsertAsync(src);
        await InitializeAsync();
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
        await InitializeAsync();
    }
}
