using System.IO;

namespace UltimateVideoBrowser.Services;

public static class LegalDocumentLoader
{
    private const string ImprintPath = "legal/LegalImprint.txt";

    public static async Task<string> LoadImprintAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(ImprintPath).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
