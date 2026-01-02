using Android.Provider;
using Microsoft.Maui.Storage;

namespace UltimateVideoBrowser.Services;

public sealed class AndroidDeviceFingerprintService : IDeviceFingerprintService
{
    private const string InstallationIdKey = "device_installation_id";

    public Task<string> GetFingerprintAsync(CancellationToken ct)
    {
        var androidId = Secure.GetString(Android.App.Application.Context.ContentResolver, Secure.AndroidId) ?? string.Empty;
        var installationId = Preferences.Default.Get(InstallationIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(installationId))
        {
            installationId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(InstallationIdKey, installationId);
        }

        var payload = $"android|{androidId}|{installationId}";
        return Task.FromResult(DeviceFingerprintHasher.Hash(payload));
    }
}
