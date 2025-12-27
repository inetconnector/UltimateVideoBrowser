using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
#if WINDOWS
using Microsoft.Maui.Platform;
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
}