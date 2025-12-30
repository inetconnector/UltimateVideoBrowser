using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;
using UltimateVideoBrowser.ViewModels;
using UltimateVideoBrowser.Views;
#if ANDROID
using UltimateVideoBrowser.Platforms.Android;

#elif WINDOWS
using UltimateVideoBrowser.Platforms.Windows;
#endif

namespace UltimateVideoBrowser;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement();

#if WINDOWS
        SvgImageSourceFix.Configure();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Services
        builder.Services.AddSingleton<AppDb>();
        builder.Services.AddSingleton<AppSettingsService>();
        builder.Services.AddSingleton<DeviceModeService>();
        builder.Services.AddSingleton<PermissionService>();
        builder.Services.AddSingleton<MediaStoreScanner>();
        builder.Services.AddSingleton<ThumbnailService>();
        builder.Services.AddSingleton<IndexService>();
        builder.Services.AddSingleton<PeopleTagService>();
        builder.Services.AddSingleton<ModelFileService>();
        builder.Services.AddSingleton<YuNetFaceDetector>(sp =>
        {
            var modelService = sp.GetRequiredService<ModelFileService>();
            var detectorPath = modelService.GetYuNetModelAsync(CancellationToken.None).GetAwaiter().GetResult();
            var postPath = modelService.GetYuNetPostModelAsync(CancellationToken.None).GetAwaiter().GetResult();
            return new YuNetFaceDetector(detectorPath, postPath);
        });
        builder.Services.AddSingleton<SFaceRecognizer>(sp =>
        {
            var modelService = sp.GetRequiredService<ModelFileService>();
            var modelPath = modelService.GetSFaceModelAsync(CancellationToken.None).GetAwaiter().GetResult();
            return new SFaceRecognizer(modelPath);
        });
        builder.Services.AddSingleton<PeopleRecognitionService>();
        builder.Services.AddSingleton<ISourceService, SourceService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<PlaybackService>();
        builder.Services.AddSingleton<IFileExportService, FileExportService>();
#if ANDROID && !WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
#elif WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
#endif

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<SourcesPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }
}
