using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using UltimateVideoBrowser.Services;
using WinRT.Interop;

namespace UltimateVideoBrowser.Platforms.Windows;

public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<IReadOnlyList<FolderPickResult>> PickFoldersAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window == null)
            return Array.Empty<FolderPickResult>();

        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();

        ct.ThrowIfCancellationRequested();

        if (folder == null)
            return Array.Empty<FolderPickResult>();

        string? token = null;
        try
        {
            token = StorageApplicationPermissions.FutureAccessList.Add(folder);
        }
        catch
        {
            // Ignore failures (e.g. unpackaged apps or network locations without access list support).
        }

        return new[] { new FolderPickResult(folder.Path, folder.DisplayName, token) };
    }
}