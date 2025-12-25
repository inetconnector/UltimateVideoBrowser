namespace UltimateVideoBrowser.Services;

public sealed class PermissionService
{
    public async Task<bool> EnsureMediaReadAsync()
    {
        // Android 9 uses READ_EXTERNAL_STORAGE.
        var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
        if (status == PermissionStatus.Granted)
            return true;

        status = await Permissions.RequestAsync<Permissions.StorageRead>();
        return status == PermissionStatus.Granted;
    }
}
