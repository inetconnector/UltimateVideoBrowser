using System;
using Microsoft.Maui.Platform;
using UltimateVideoBrowser.Services;
using Windows.Storage.Pickers;

namespace UltimateVideoBrowser.Platforms.Windows;

public sealed class FolderPickerService : IFolderPickerService
{
    public async Task<IReadOnlyList<FolderPickResult>> PickFoldersAsync(CancellationToken ct = default)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as MauiWinUIWindow;
        if (window == null)
            return Array.Empty<FolderPickResult>();

        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var folders = await picker.PickMultipleFoldersAsync();
        ct.ThrowIfCancellationRequested();

        if (folder == null)
            return Array.Empty<FolderPickResult>();

        return new[] { new FolderPickResult(folder.Path, folder.DisplayName) };
    }
}
