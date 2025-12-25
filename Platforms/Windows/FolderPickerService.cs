using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using UltimateVideoBrowser.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

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

        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.WindowHandle);

        StorageFolder? folder = await picker.PickSingleFolderAsync();

        ct.ThrowIfCancellationRequested();

        if (folder == null)
            return Array.Empty<FolderPickResult>();

        return new[] { new FolderPickResult(folder.Path, folder.DisplayName) };
    }
}
