using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    private readonly IFolderPickerService folderPickerService;
    private readonly PermissionService permissionService;
    private readonly SourceService sourceService;
    [ObservableProperty] private bool hasMediaPermission;

    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private bool supportsManualPath;

    public SourcesViewModel(SourceService sourceService, PermissionService permissionService,
        IFolderPickerService folderPickerService)
    {
        this.sourceService = sourceService;
        this.permissionService = permissionService;
        this.folderPickerService = folderPickerService;
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
        var result = await folderPickerService.PickFolderAsync();
        if (result == null || string.IsNullOrWhiteSpace(result.Path))
            return;

        var existing = (await sourceService.GetSourcesAsync()).FirstOrDefault(s => s.LocalFolderPath == result.Path);
        if (existing != null)
        {
            await Shell.Current.DisplayAlert(AppResources.SourceExistsTitle, AppResources.SourceExistsMessage,
                AppResources.OkButton);
            return;
        }

        var suggestedName = string.IsNullOrWhiteSpace(result.DisplayName)
            ? AppResources.NewSourceDefaultName
            : result.DisplayName;

        var displayName = await Shell.Current.DisplayPromptAsync(
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
            LocalFolderPath = result.Path,
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };

        await sourceService.UpsertAsync(src);
        await InitializeAsync();
    }

    [RelayCommand]
    public async Task AddPathAsync()
    {
        var path = await Shell.Current.DisplayPromptAsync(
            AppResources.AddPathTitle,
            AppResources.AddPathPrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            "");

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!Directory.Exists(path))
        {
            await Shell.Current.DisplayAlert(AppResources.PathInvalidTitle, AppResources.PathInvalidMessage,
                AppResources.OkButton);
            return;
        }

        var existing = (await sourceService.GetSourcesAsync()).FirstOrDefault(s => s.LocalFolderPath == path);
        if (existing != null)
        {
            await Shell.Current.DisplayAlert(AppResources.SourceExistsTitle, AppResources.SourceExistsMessage,
                AppResources.OkButton);
            return;
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
            name = path;

        var displayName = await Shell.Current.DisplayPromptAsync(
            AppResources.NewSourceTitle,
            AppResources.NewSourcePrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            name,
            60,
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(displayName))
            return;

        var src = new MediaSource
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName.Trim(),
            LocalFolderPath = path.Trim(),
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

        var ok = await Shell.Current.DisplayAlert(
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