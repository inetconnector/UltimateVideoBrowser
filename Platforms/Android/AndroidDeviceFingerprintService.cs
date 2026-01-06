using Android.Provider;
using Application = Android.App.Application;

namespace UltimateVideoBrowser.Services;

public sealed class AndroidDeviceFingerprintService : IDeviceFingerprintService
{
    private const string InstallationIdKey = "device_installation_id";

    public Task<string> GetFingerprintAsync(CancellationToken ct)
    {
        var androidId = Settings.Secure.GetString(
            Application.Context.ContentResolver,
            Settings.Secure.AndroidId) ?? string.Empty;
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