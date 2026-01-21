using System.Linq;
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
    private readonly NetworkShareCredentialStore credentialStore;
    private readonly NetworkShareScanner networkShareScanner;
    private readonly PermissionService permissionService;
    private readonly AppSettingsService settingsService;
    private readonly ISourceService sourceService;
    [ObservableProperty] private bool hasMediaPermission;
    [ObservableProperty] private bool isNetworkScanRunning;

    [ObservableProperty] private List<MediaSource> sources = new();
    [ObservableProperty] private bool supportsManualPath;

    public SourcesViewModel(
        ISourceService sourceService,
        PermissionService permissionService,
        IFolderPickerService folderPickerService,
        IDialogService dialogService,
        AppSettingsService settingsService,
        NetworkShareCredentialStore credentialStore,
        NetworkShareScanner networkShareScanner)
    {
        this.sourceService = sourceService;
        this.permissionService = permissionService;
        this.folderPickerService = folderPickerService;
        this.dialogService = dialogService;
        this.settingsService = settingsService;
        this.credentialStore = credentialStore;
        this.networkShareScanner = networkShareScanner;
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
        var choice = await dialogService.DisplayActionSheetAsync(
            AppResources.AddSourceActionTitle,
            AppResources.NewSourceCancel,
            null,
            AppResources.AddSourceLocalOption,
            AppResources.AddSourceNetworkOption);

        if (string.Equals(choice, AppResources.AddSourceLocalOption, StringComparison.Ordinal))
            await AddSourcesAsync();
        else if (string.Equals(choice, AppResources.AddSourceNetworkOption, StringComparison.Ordinal))
            await AddNetworkShareAsync();
    }

    [RelayCommand]
    public async Task AddPathAsync()
    {
        var path = await dialogService.DisplayPromptAsync(
            AppResources.AddPathTitle,
            AppResources.AddPathPrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            "smb://server/share",
            256,
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(path))
            return;

        await AddSourceFromPathAsync(path.Trim(), null, true);
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

        return await AddSourceFromPathAsync(path, result.DisplayName, false, result.AccessToken);
    }

    private async Task<bool> AddSourceFromPathAsync(
        string path,
        string? suggestedDisplayName,
        bool isManualPath,
        string? accessToken = null)
    {
        var isContentUri = path.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
        var isNetworkPath = IsNetworkPath(path);
        if (!isNetworkPath && !isContentUri && !Directory.Exists(path))
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

        var suggestedName = string.IsNullOrWhiteSpace(suggestedDisplayName)
            ? AppResources.NewSourceDefaultName
            : suggestedDisplayName;

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
            AccessToken = accessToken,
            IsEnabled = true,
            LastIndexedUtcSeconds = 0
        };

        await sourceService.UpsertAsync(src);
        settingsService.NeedsReindex = true;
        await InitializeAsync();

        if (isNetworkPath && isManualPath)
            await EnsureNetworkCredentialsAsync(src, null);

        return true;
    }

    [RelayCommand]
    public async Task EditNetworkCredentialsAsync(MediaSource src)
    {
        if (!src.IsNetworkSource)
            return;

        var existing = await credentialStore.GetAsync(src.Id);
        await EnsureNetworkCredentialsAsync(src, existing);
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
        credentialStore.Remove(src.Id);
        settingsService.NeedsReindex = true;
        await InitializeAsync();
    }

    private async Task AddNetworkShareAsync()
    {
        var choice = await dialogService.DisplayActionSheetAsync(
            AppResources.NetworkShareAddTitle,
            AppResources.NewSourceCancel,
            null,
            AppResources.NetworkShareScanOption,
            AppResources.NetworkShareManualOption);

        if (string.IsNullOrWhiteSpace(choice))
            return;

        string? server = null;
        if (string.Equals(choice, AppResources.NetworkShareScanOption, StringComparison.Ordinal))
        {
            List<NetworkServerInfo> servers;
            IsNetworkScanRunning = true;
            try
            {
                servers = await networkShareScanner.ScanAsync();
            }
            finally
            {
                IsNetworkScanRunning = false;
            }
            if (servers.Count == 0)
            {
                await dialogService.DisplayAlertAsync(
                    AppResources.NetworkShareScanEmptyTitle,
                    AppResources.NetworkShareScanEmptyMessage,
                    AppResources.OkButton);
                return;
            }

            var selection = await dialogService.DisplayActionSheetAsync(
                AppResources.NetworkShareScanPickTitle,
                AppResources.NewSourceCancel,
                null,
                servers.Select(s => s.DisplayName).ToArray());

            var selectedServer = servers.FirstOrDefault(s => s.DisplayName == selection);
            server = selectedServer?.Address ?? selection;
        }
        else if (string.Equals(choice, AppResources.NetworkShareManualOption, StringComparison.Ordinal))
        {
            server = await dialogService.DisplayPromptAsync(
                AppResources.NetworkShareServerTitle,
                AppResources.NetworkShareServerPrompt,
                AppResources.NewSourceConfirm,
                AppResources.NewSourceCancel,
                "NAS",
                80,
                Keyboard.Text);
        }

        if (string.IsNullOrWhiteSpace(server))
            return;

        var share = await dialogService.DisplayPromptAsync(
            AppResources.NetworkShareNameTitle,
            AppResources.NetworkShareNamePrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            "Videos",
            80,
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(share))
            return;

        var path = BuildSmbPath(server.Trim(), share.Trim());
        var nameSuggestion = $"{server.Trim()} / {share.Trim()}";

        var added = await AddSourceFromPathAsync(path, nameSuggestion, false);
        if (!added)
            return;

        var src = (await sourceService.GetSourcesAsync()).FirstOrDefault(s => s.LocalFolderPath == path);
        if (src == null)
            return;

        await EnsureNetworkCredentialsAsync(src, null);
    }

    private async Task EnsureNetworkCredentialsAsync(MediaSource src, NetworkShareCredentials? existing)
    {
        var username = await dialogService.DisplayPromptAsync(
            AppResources.NetworkShareCredentialsTitle,
            AppResources.NetworkShareUsernamePrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            existing?.Username ?? string.Empty,
            80,
            Keyboard.Text,
            existing?.Username);

        if (username == null)
            return;

        var password = await dialogService.DisplayPromptAsync(
            AppResources.NetworkShareCredentialsTitle,
            AppResources.NetworkSharePasswordPrompt,
            AppResources.NewSourceConfirm,
            AppResources.NewSourceCancel,
            string.Empty,
            120,
            Keyboard.Text);

        if (password == null)
            return;

        var saved = await credentialStore.SaveAsync(src.Id, username, password);
        if (!saved)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.NetworkShareSaveFailedTitle,
                AppResources.NetworkShareSaveFailedMessage,
                AppResources.OkButton);
        }
    }

    private static bool IsNetworkPath(string path)
    {
        return path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSmbPath(string server, string share)
    {
        var normalizedServer = server.Trim().Trim('/');
        var normalizedShare = share.Trim().Trim('/');
        return $"smb://{normalizedServer}/{normalizedShare}";
    }
}
