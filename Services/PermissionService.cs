
#if ANDROID && !WINDOWS
using Android;
using Environment = Android.OS.Environment;
using Uri = Android.Net.Uri;
using Android.OS;
using Android.Content;
using Android.Provider;
using System.Runtime.Versioning; 
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PermissionService
{
    public Task<bool> CheckMediaReadAsync()
    {
#if ANDROID && !WINDOWS
        return IsMediaPermissionGrantedAsync();
#else
        return Task.FromResult(true);
#endif
    }

    public async Task<bool> EnsureMediaReadAsync()
    {
#if ANDROID && !WINDOWS
        if (await IsMediaPermissionGrantedAsync())
            return true;

        return await RequestMediaPermissionAsync();
#else
        return true;
#endif
    }

    public void OpenAppSettings()
    {
        AppInfo.ShowSettingsUI();
    }

#if ANDROID && !WINDOWS
    private static async Task<bool> IsMediaPermissionGrantedAsync()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R &&
            Environment.IsExternalStorageManager)
            return true;

        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (status == PermissionStatus.Granted)
            return true;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var mediaStatus = await Permissions.CheckStatusAsync<MediaLibraryPermission>();
            return mediaStatus == PermissionStatus.Granted;
        }

        return false;
    }

    private static async Task<bool> RequestMediaPermissionAsync()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var mediaStatus = await Permissions.RequestAsync<MediaLibraryPermission>();
            if (mediaStatus == PermissionStatus.Granted)
                return true;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                RequestAllFilesAccess();

            return false;
        }

        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status == PermissionStatus.Granted)
            return true;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            RequestAllFilesAccess();

        return false;
    }

    private static bool RequestAllFilesAccess()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
            return false;

        try
        {
            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission);
            intent.SetData(Uri.Parse($"package:{activity.PackageName}"));
            activity.StartActivity(intent);
            return Environment.IsExternalStorageManager;
        }
        catch (ActivityNotFoundException)
        {
            try
            {
                var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                activity.StartActivity(intent);
            }
            catch
            {
                return false;
            }
        }

        return Environment.IsExternalStorageManager;
    }

    [SupportedOSPlatform("android33.0")]
    private sealed class MediaLibraryPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            new[]
            {
                (Manifest.Permission.ReadMediaVideo, true),
                (Manifest.Permission.ReadMediaImages, true)
            };
    }
#endif
}