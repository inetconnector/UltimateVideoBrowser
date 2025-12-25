using Microsoft.Maui.Platform;
using UltimateVideoBrowser.Services;
using Windows.Storage.Pickers;

namespace UltimateVideoBrowser.Platforms.Windows;

public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<FolderPickResult?> PickFolderAsync(CancellationToken ct = default)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window == null)
            return null;

        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        ct.ThrowIfCancellationRequested();

        if (folder == null)
            return null;

        return new FolderPickResult(folder.Path, folder.DisplayName);
    }
}