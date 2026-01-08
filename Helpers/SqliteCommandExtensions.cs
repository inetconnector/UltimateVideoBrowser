using System;
using System.Reflection;
using SQLite;

namespace UltimateVideoBrowser.Helpers;

public static class SqliteCommandExtensions
{
    public static void Bind(this SQLiteCommand command, params object?[] args)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));

        if (args == null || args.Length == 0)
            return;

        var bindAll = command.GetType().GetMethod("BindAll", new[] { typeof(object[]) });
        if (bindAll != null)
        {
            bindAll.Invoke(command, new object[] { args });
            return;
        }

        var bindSingle = command.GetType().GetMethod("Bind", new[] { typeof(object) });
        if (bindSingle == null)
            return;

        foreach (var arg in args)
            bindSingle.Invoke(command, new[] { arg });
    }
}
