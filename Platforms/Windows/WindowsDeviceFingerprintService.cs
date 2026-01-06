using Microsoft.Win32;
using Microsoft.Maui.Storage;

namespace UltimateVideoBrowser.Services;

public sealed class WindowsDeviceFingerprintService : IDeviceFingerprintService
{
    private const string InstallationIdKey = "device_installation_id";

    public Task<string> GetFingerprintAsync(CancellationToken ct)
    {
        var machineGuid = Registry.GetValue(
            "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Cryptography",
            "MachineGuid",
            string.Empty) as string ?? string.Empty;

        var installationId = Preferences.Default.Get(InstallationIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(installationId))
        {
            installationId = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(InstallationIdKey, installationId);
        }

        var payload = $"windows|{machineGuid}|{installationId}";
        return Task.FromResult(DeviceFingerprintHasher.Hash(payload));
    }
}