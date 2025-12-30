#if ANDROID && !WINDOWS
using Android;
using Android.OS;
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

#if ANDROID && !WINDOWS
    private static async Task<bool> IsMediaPermissionGrantedAsync()
    {
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
            return mediaStatus == PermissionStatus.Granted;
        }

        var status = await Permissions.RequestAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
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