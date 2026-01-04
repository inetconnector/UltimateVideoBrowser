using System.IO;

namespace UltimateVideoBrowser.Services;

public static class LegalDocumentLoader
{
    private const string ImprintPath = "legal/LegalImprint.txt";
    private const string LicenseInfoPath = "legal/LicenseInfo.txt";

    public static async Task<string> LoadImprintAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(ImprintPath).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    public static async Task<string> LoadLicenseInfoAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(LicenseInfoPath).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
