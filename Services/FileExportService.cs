using System.Linq;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
#if WINDOWS
using Microsoft.Maui.Platform;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class FileExportService : IFileExportService
{
    private readonly IDialogService dialogService;

    public FileExportService(IDialogService dialogService)
    {
        this.dialogService = dialogService;
    }

    public async Task SaveAsAsync(VideoItem item)
    {
#if WINDOWS
        try
        {
            var picker = new FileSavePicker();
            var extension = Path.GetExtension(item.Path);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".mp4";

            picker.FileTypeChoices.Add(AppResources.SaveAsFileTypeLabel, new List<string> { extension });

            var suggestedName = string.IsNullOrWhiteSpace(item.Name)
                ? Path.GetFileNameWithoutExtension(item.Path)
                : item.Name;
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName);

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is not MauiWinUIWindow mauiWindow)
            {
                await dialogService.DisplayAlertAsync(
                    AppResources.SaveAsFailedTitle,
                    AppResources.SaveAsFailedMessage,
                    AppResources.OkButton);
                return;
            }

            InitializeWithWindow.Initialize(picker, mauiWindow.WindowHandle);
            var destination = await picker.PickSaveFileAsync();
            if (destination is null)
                return;

            await using var sourceStream = File.OpenRead(item.Path);
            await using var destinationStream = await destination.OpenStreamForWriteAsync();
            destinationStream.SetLength(0);
            await sourceStream.CopyToAsync(destinationStream);
        }
        catch
        {
            await dialogService.DisplayAlertAsync(
                AppResources.SaveAsFailedTitle,
                AppResources.SaveAsFailedMessage,
                AppResources.OkButton);
        }
#else
        await dialogService.DisplayAlertAsync(
            AppResources.SaveAsFailedTitle,
            AppResources.SaveAsNotSupportedMessage,
            AppResources.OkButton);
#endif
    }

    public async Task CopyToFolderAsync(IEnumerable<VideoItem> items)
    {
        await TransferToFolderAsync(items, isMove: false);
    }

    public Task<IReadOnlyList<VideoItem>> MoveToFolderAsync(IEnumerable<VideoItem> items)
    {
        return TransferToFolderAsync(items, isMove: true);
    }

    private async Task<IReadOnlyList<VideoItem>> TransferToFolderAsync(IEnumerable<VideoItem> items, bool isMove)
    {
#if WINDOWS
        var list = items.Where(i => i != null && !string.IsNullOrWhiteSpace(i.Path)).ToList();
        if (list.Count == 0)
            return Array.Empty<VideoItem>();

        var folderName = await dialogService.DisplayPromptAsync(
            AppResources.TransferFolderTitle,
            AppResources.TransferFolderMessage,
            AppResources.CreateButton,
            AppResources.CancelButton,
            AppResources.TransferFolderPlaceholder,
            64,
            Keyboard.Text);

        if (string.IsNullOrWhiteSpace(folderName))
            return Array.Empty<VideoItem>();

        folderName = folderName.Trim();

        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not MauiWinUIWindow mauiWindow)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.TransferFailedTitle,
                AppResources.TransferFailedMessage,
                AppResources.OkButton);
            return Array.Empty<VideoItem>();
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, mauiWindow.WindowHandle);
        var rootFolder = await picker.PickSingleFolderAsync();
        if (rootFolder is null)
            return Array.Empty<VideoItem>();

        StorageFolder targetFolder;
        try
        {
            targetFolder = await rootFolder.CreateFolderAsync(folderName, CreationCollisionOption.OpenIfExists);
        }
        catch
        {
            await dialogService.DisplayAlertAsync(
                AppResources.TransferFailedTitle,
                AppResources.TransferFailedMessage,
                AppResources.OkButton);
            return Array.Empty<VideoItem>();
        }

        var targetPath = targetFolder.Path;
        var succeeded = new List<VideoItem>();
        var skipped = 0;
        var failed = 0;

        foreach (var item in list)
        {
            try
            {
                if (!File.Exists(item.Path))
                {
                    failed++;
                    continue;
                }

                var destinationPath = Path.Combine(targetPath, Path.GetFileName(item.Path));
                if (File.Exists(destinationPath))
                {
                    skipped++;
                    continue;
                }

                if (isMove)
                    File.Move(item.Path, destinationPath);
                else
                    File.Copy(item.Path, destinationPath);

                succeeded.Add(item);
            }
            catch
            {
                failed++;
            }
        }

        var title = isMove ? AppResources.TransferMoveCompletedTitle : AppResources.TransferCopyCompletedTitle;
        var message = string.Format(
            AppResources.TransferCompletedMessageFormat,
            succeeded.Count,
            skipped,
            failed);

        await dialogService.DisplayAlertAsync(title, message, AppResources.OkButton);
        return succeeded;
#else
        await dialogService.DisplayAlertAsync(
            AppResources.TransferFailedTitle,
            AppResources.TransferNotSupportedMessage,
            AppResources.OkButton);
        return Array.Empty<VideoItem>();
#endif
    }
}
