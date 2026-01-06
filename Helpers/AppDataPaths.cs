namespace UltimateVideoBrowser.Helpers;

public static class AppDataPaths
{
    private static readonly Lazy<string> AppDataRoot = new(() =>
    {
        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InetConnector",
            "UltimateVideoBrowser");
        Directory.CreateDirectory(basePath);
        return basePath;
    });

    public static string Root => AppDataRoot.Value;
}