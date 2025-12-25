using Microsoft.Extensions.Logging;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;
using UltimateVideoBrowser.Views;
#if ANDROID && !WINDOWS
using UltimateVideoBrowser.Platforms.Android;
#endif

namespace UltimateVideoBrowser;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Services
        builder.Services.AddSingleton<AppDb>();
        builder.Services.AddSingleton<DeviceModeService>();
        builder.Services.AddSingleton<PermissionService>();
        builder.Services.AddSingleton<MediaStoreScanner>();
        builder.Services.AddSingleton<ThumbnailService>();
        builder.Services.AddSingleton<IndexService>();
        builder.Services.AddSingleton<ISourceService, SourceService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<PlaybackService>();
#if ANDROID && !WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
#elif WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, UltimateVideoBrowser.Platforms.Windows.FolderPickerService>();
#endif

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<SourcesPage>();

        return builder.Build();
    }
}
