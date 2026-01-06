namespace UltimateVideoBrowser.Services;

public interface IDeviceFingerprintService
{
    Task<string> GetFingerprintAsync(CancellationToken ct);
}