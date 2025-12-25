using Microsoft.Extensions.Logging;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;

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

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();

        // Pages
        builder.Services.AddSingleton<Views.MainPage>();
        builder.Services.AddSingleton<Views.SourcesPage>();

        return builder.Build();
    }
}
