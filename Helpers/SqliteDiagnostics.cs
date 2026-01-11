using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using SQLite;

namespace UltimateVideoBrowser.Helpers;

/// <summary>
///     Captures SQLite exceptions (including first-chance exceptions) and writes them to error.log.
///     This helps diagnose issues that would otherwise only appear in debugger output.
/// </summary>
public static class SqliteDiagnostics
{
    private static int initialized;
    private static readonly ConcurrentDictionary<string, long> LastSeen = new(StringComparer.Ordinal);

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 1)
            return;

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not SQLiteException ex)
            return;

        try
        {
            if (ex.Message?.Contains("not an error", StringComparison.OrdinalIgnoreCase) == true)
                return;

            var key = ex.Message ?? ex.GetType().FullName ?? "SQLiteException";
            var now = Environment.TickCount64;
            var last = LastSeen.GetOrAdd(key, 0);
            if (now - last < 1500)
                return;

            LastSeen[key] = now;
            ErrorLog.LogException(ex, "SQLite.FirstChance");
        }
        catch
        {
            // Avoid throwing from diagnostics.
        }
    }
}