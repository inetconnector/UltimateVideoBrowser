using System.Text;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Helpers;

public static class ScanLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? logPath;

    public static string LogPath => logPath ??= Path.Combine(AppDataPaths.Root, "scan.log");

    public static void LogScan(string path, string? name, string source, string result, MediaType mediaType)
    {
        _ = LogScanAsync(path, name, source, result, mediaType);
    }

    public static async Task LogScanAsync(string path, string? name, string source, string result, MediaType mediaType)
    {
        try
        {
            var entry = BuildEntry(path, name, source, result, mediaType);
            await AppendAsync(entry).ConfigureAwait(false);
        }
        catch
        {
            // Avoid throwing during logging.
        }
    }

    public static async Task<string> ReadLogAsync()
    {
        try
        {
            if (!File.Exists(LogPath))
                return string.Empty;

            return await File.ReadAllTextAsync(LogPath).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task ClearLogAsync()
    {
        try
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            if (File.Exists(LogPath))
                File.Delete(LogPath);
        }
        catch
        {
            // Ignore cleanup errors.
        }
        finally
        {
            if (Gate.CurrentCount == 0)
                Gate.Release();
        }
    }

    private static async Task AppendAsync(string entry)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.AppendAllTextAsync(LogPath, entry).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string BuildEntry(string path, string? name, string source, string result, MediaType mediaType)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----");
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Result: {result}");
        builder.AppendLine($"MediaType: {mediaType}");
        if (!string.IsNullOrWhiteSpace(name))
            builder.AppendLine($"Name: {name}");
        builder.AppendLine($"Path: {path}");
        builder.AppendLine();
        return builder.ToString();
    }
}