using Microsoft.Maui.Storage;

namespace UltimateVideoBrowser.Services;

public sealed class DefaultDeviceFingerprintService : IDeviceFingerprintService
{
    private const string InstallationIdKey = "device_installation_id";

    public Task<string> GetFingerprintAsync(CancellationToken ct)
    {
        var installationId = Preferences.Default.Get(InstallationIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(installationId))
        {
            installationId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(InstallationIdKey, installationId);
        }

        var payload = $"default|{installationId}";
        return Task.FromResult(DeviceFingerprintHasher.Hash(payload));
    }
}
