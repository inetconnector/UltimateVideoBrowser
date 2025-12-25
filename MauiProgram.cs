using Microsoft.Extensions.Logging;
using UltimateVideoBrowser.Platforms.Android;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;
using UltimateVideoBrowser.Views;

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
        builder.Services.AddSingleton<SourceService>();
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
