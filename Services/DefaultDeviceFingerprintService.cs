namespace UltimateVideoBrowser.Services;

public sealed class DefaultDeviceFingerprintService : IDeviceFingerprintService
{
    private const string InstallationIdKey = "device_installation_id";
    private readonly FileSettingsStore store;

    public DefaultDeviceFingerprintService(FileSettingsStore store)
    {
        this.store = store;
    }

    public Task<string> GetFingerprintAsync(CancellationToken ct)
    {
        var installationId = store.GetString(InstallationIdKey, string.Empty);
        if (string.IsNullOrWhiteSpace(installationId))
        {
            installationId = Guid.NewGuid().ToString("N");
            store.SetString(InstallationIdKey, installationId);
        }

        var payload = $"default|{installationId}";
        return Task.FromResult(DeviceFingerprintHasher.Hash(payload));
    }
}
