using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.Services.Faces;
using UltimateVideoBrowser.ViewModels;
using UltimateVideoBrowser.Views;

#if WINDOWS
using Microsoft.UI;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Windowing;
using WinRT.Interop;
#endif

#if ANDROID
using UltimateVideoBrowser.Platforms.Android;
#elif WINDOWS
using UltimateVideoBrowser.Platforms.Windows;
#endif

namespace UltimateVideoBrowser;

public static class MauiProgram
{
#if WINDOWS
    private static bool _mainWindowMaximized;
#endif

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement();

#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows =>
            {
                windows.OnWindowCreated(window =>
                {
                    try
                    {
                        // Only maximize the FIRST window (main window). Auxiliary windows (e.g. indexing progress)
                        // must stay as sized popup windows.
                        if (_mainWindowMaximized)
                            return;

                        // In MAUI's Windows lifecycle events, the window parameter is a WinUI window.
                        // Avoid MAUI handler APIs here to keep this compiling across target frameworks.
                        var hwnd = WindowNative.GetWindowHandle(window);
                        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                        var appWindow = AppWindow.GetFromWindowId(windowId);

                        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                        if (appWindow.Presenter is OverlappedPresenter presenter)
                            presenter.Maximize();

                        _mainWindowMaximized = true;
                    }
                    catch
                    {
                        // Best-effort only.
                    }
                });
            });
        });
#endif

#if WINDOWS
        SvgImageSourceFix.Configure();
#endif

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Services
        builder.Services.AddSingleton<AppDb>();
        builder.Services.AddSingleton<FileSettingsStore>();
        builder.Services.AddSingleton<AppSettingsService>();
        builder.Services.AddSingleton<DeviceModeService>();
        builder.Services.AddSingleton<PermissionService>();
        builder.Services.AddSingleton<MediaStoreScanner>();
        builder.Services.AddSingleton<ThumbnailService>();
        builder.Services.AddSingleton<VideoDurationService>();
        builder.Services.AddSingleton<ImageEditService>();
        builder.Services.AddSingleton<LocationMetadataService>();
        builder.Services.AddSingleton<IndexService>();
        builder.Services.AddSingleton<AlbumService>();
        builder.Services.AddSingleton<PeopleTagService>();
        builder.Services.AddSingleton<FaceThumbnailService>();
        builder.Services.AddSingleton<PeopleDataService>();
        builder.Services.AddSingleton<ModelFileService>();
        builder.Services.AddSingleton<YuNetFaceDetector>();
        builder.Services.AddSingleton<SFaceRecognizer>();
        builder.Services.AddSingleton<PeopleRecognitionService>();
        builder.Services.AddSingleton<FaceScanQueueService>();
        builder.Services.AddSingleton<ISourceService, SourceService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<ILegalConsentService, LegalConsentService>();
        builder.Services.AddSingleton<PlaybackService>();
        builder.Services.AddSingleton<IFileExportService, FileExportService>();
        builder.Services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<LicenseServerClient>();
        builder.Services.AddSingleton<IProUpgradeService, ProUpgradeService>();
#if ANDROID && !WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
#elif WINDOWS
        builder.Services.AddSingleton<IFolderPickerService, FolderPickerService>();
#endif
#if ANDROID
        builder.Services.AddSingleton<IDeviceFingerprintService, AndroidDeviceFingerprintService>();
#elif WINDOWS
        builder.Services.AddSingleton<IDeviceFingerprintService, WindowsDeviceFingerprintService>();
#else
        builder.Services.AddSingleton<IDeviceFingerprintService, DefaultDeviceFingerprintService>();
#endif

        // ViewModels
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AlbumsViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<ProUpgradeViewModel>();
        builder.Services.AddTransient<PeopleViewModel>();
        builder.Services.AddTransient<PersonViewModel>();
        builder.Services.AddTransient<PhotoPeopleEditorViewModel>();
        builder.Services.AddTransient<TaggedPhotosViewModel>();
        builder.Services.AddTransient<MapViewModel>();

        // Pages
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AlbumsPage>();
        builder.Services.AddSingleton<SourcesPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<AboutPage>();
        builder.Services.AddSingleton<ProUpgradePage>();
        builder.Services.AddSingleton<PeoplePage>();
        builder.Services.AddTransient<PersonPage>();
        builder.Services.AddTransient<PhotoPeopleEditorPage>();
        builder.Services.AddTransient<TaggedPhotosPage>();
        builder.Services.AddTransient<MapPage>();

        return builder.Build();
    }
}