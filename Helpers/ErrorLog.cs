using System.Text;

namespace UltimateVideoBrowser.Helpers;

public static class ErrorLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? logPath;

    public static string LogPath => logPath ??= Path.Combine(AppDataPaths.Root, "error.log");

    public static void LogException(Exception exception, string context, string? details = null)
    {
        _ = LogExceptionAsync(exception, context, details);
    }

    public static async Task LogExceptionAsync(Exception exception, string context, string? details = null)
    {
        try
        {
            var entry = BuildExceptionEntry(exception, context, details);
            await AppendAsync(entry).ConfigureAwait(false);
        }
        catch
        {
            // Avoid throwing during logging.
        }
    }

    public static void LogMessage(string message, string context)
    {
        _ = LogMessageAsync(message, context);
    }

    public static async Task LogMessageAsync(string message, string context)
    {
        try
        {
            var entry = BuildMessageEntry(message, context);
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

    private static string BuildExceptionEntry(Exception exception, string context, string? details)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----");
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Context: {context}");
        builder.AppendLine($"AppVersion: {AppInfo.VersionString} ({AppInfo.BuildString})");
        builder.AppendLine($"Platform: {DeviceInfo.Platform} {DeviceInfo.VersionString}");
        builder.AppendLine($"Device: {DeviceInfo.Manufacturer} {DeviceInfo.Model}");
        if (!string.IsNullOrWhiteSpace(details))
            builder.AppendLine($"Details: {details}");

        AppendException(builder, exception, 0);
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildMessageEntry(string message, string context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("-----");
        builder.AppendLine($"Timestamp: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Context: {context}");
        builder.AppendLine($"AppVersion: {AppInfo.VersionString} ({AppInfo.BuildString})");
        builder.AppendLine($"Platform: {DeviceInfo.Platform} {DeviceInfo.VersionString}");
        builder.AppendLine($"Message: {message}");
        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        var prefix = depth == 0 ? string.Empty : new string('>', depth) + " ";
        builder.AppendLine($"{prefix}ExceptionType: {exception.GetType().FullName}");
        builder.AppendLine($"{prefix}Message: {exception.Message}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.AppendLine($"{prefix}StackTrace:");
            builder.AppendLine(exception.StackTrace);
        }

        if (exception.InnerException != null)
            AppendException(builder, exception.InnerException, depth + 1);
    }
}
