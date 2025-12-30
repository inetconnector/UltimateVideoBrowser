#if ANDROID
using UltimateVideoBrowser.Platforms.Android;
#elif WINDOWS
using UltimateVideoBrowser.Platforms.Windows;
#endif
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;
using UltimateVideoBrowser.ViewModels;
using UltimateVideoBrowser.Views;

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
        builder.Services.AddSingleton<YuNetFaceDetector>();
        builder.Services.AddSingleton<SFaceRecognizer>();
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