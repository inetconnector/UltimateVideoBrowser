#if ANDROID && !WINDOWS
using Android;
using Android.OS;
using System.Runtime.Versioning;
using UltimateVideoBrowser.Models;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PermissionService
{
    public Task<bool> CheckMediaReadAsync()
    {
#if ANDROID && !WINDOWS
        return IsGrantedAsync(GetAndroidMediaPermissionTypes());
#else
        return Task.FromResult(true);
#endif
    }

    public async Task<bool> EnsureMediaReadAsync()
    {
#if ANDROID && !WINDOWS
        var types = GetAndroidMediaPermissionTypes();

        if (await IsGrantedAsync(types).ConfigureAwait(false))
            return true;

        return await RequestAsync(types).ConfigureAwait(false);
#else
        return true;
#endif
    }

    public void OpenAppSettings()
    {
        AppInfo.ShowSettingsUI();
    }

#if ANDROID && !WINDOWS
    private static MediaType GetAndroidMediaPermissionTypes()
        => MediaType.Photos | MediaType.Graphics | MediaType.Videos;

    // Summary: Checks runtime permissions required for MediaStore access based on requested media types.
    private static async Task<bool> IsGrantedAsync(MediaType types)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var ok = true;

            if (types.HasFlag(MediaType.Photos) || types.HasFlag(MediaType.Graphics))
            {
                var s = await Permissions.CheckStatusAsync<ReadImagesPermission>().ConfigureAwait(false);
                ok &= s == PermissionStatus.Granted;
            }

            if (types.HasFlag(MediaType.Videos))
            {
                var s = await Permissions.CheckStatusAsync<ReadVideosPermission>().ConfigureAwait(false);
                ok &= s == PermissionStatus.Granted;
            }

            return ok;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>().ConfigureAwait(false);
        return status == PermissionStatus.Granted;
    }

    // Summary: Requests runtime permissions required for MediaStore access based on requested media types.
    private static async Task<bool> RequestAsync(MediaType types)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var ok = true;

            if (types.HasFlag(MediaType.Photos) || types.HasFlag(MediaType.Graphics))
            {
                var s = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<ReadImagesPermission>()).ConfigureAwait(false);
                ok &= s == PermissionStatus.Granted;
            }

            if (types.HasFlag(MediaType.Videos))
            {
                var s = await MainThread.InvokeOnMainThreadAsync(
                    () => Permissions.RequestAsync<ReadVideosPermission>()).ConfigureAwait(false);
                ok &= s == PermissionStatus.Granted;
            }

            return ok;
        }

        var legacy = await MainThread.InvokeOnMainThreadAsync(
            () => Permissions.RequestAsync<Permissions.StorageRead>()).ConfigureAwait(false);

        return legacy == PermissionStatus.Granted;
    }

    [SupportedOSPlatform("android33.0")]
    private sealed class ReadImagesPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            new[] { (Manifest.Permission.ReadMediaImages, true) };
    }

    [SupportedOSPlatform("android33.0")]
    private sealed class ReadVideosPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            new[] { (Manifest.Permission.ReadMediaVideo, true) };
    }
#endif
}
