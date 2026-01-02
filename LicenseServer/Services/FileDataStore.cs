using System.Text.Json;
using UltimateVideoBrowser.LicenseServer.Models;

namespace UltimateVideoBrowser.LicenseServer.Services;

public sealed class FileDataStore
{
    private readonly string basePath;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileDataStore(IConfiguration configuration)
    {
        var options = configuration.GetSection("DataStorage").Get<DataStorageOptions>() ?? new DataStorageOptions();
        basePath = Path.GetFullPath(options.BasePath);
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(GetPurchasesPath());
        Directory.CreateDirectory(GetLicensesPath());
    }

    public Task SavePurchaseRecordAsync(PurchaseRecord record, CancellationToken ct)
    {
        var path = Path.Combine(GetPurchasesPath(), $"{record.OrderId}.json");
        return WriteAsync(path, record, ct);
    }

    public Task SaveLicenseRecordAsync(LicenseRecord record, CancellationToken ct)
    {
        var path = Path.Combine(GetLicensesPath(), $"{record.LicenseId}.json");
        return WriteAsync(path, record, ct);
    }

    public async Task<LicenseRecord?> GetLicenseRecordAsync(string licenseId, CancellationToken ct)
    {
        var path = Path.Combine(GetLicensesPath(), $"{licenseId}.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LicenseRecord>(stream, jsonOptions, ct).ConfigureAwait(false);
    }

    private Task WriteAsync<T>(string path, T payload, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, payload, jsonOptions, ct).ConfigureAwait(false);
        }, ct);
    }

    private string GetPurchasesPath() => Path.Combine(basePath, "purchases");
    private string GetLicensesPath() => Path.Combine(basePath, "licenses");
}
