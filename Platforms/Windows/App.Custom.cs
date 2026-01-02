using UltimateVideoBrowser.Helpers;
using UnhandledExceptionEventArgs = Microsoft.UI.Xaml.UnhandledExceptionEventArgs;

namespace UltimateVideoBrowser.WinUI;

public partial class App
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.Exception;
        if (exception != null)
            ErrorLog.LogException(exception, "WinUI.UnhandledException");
        else
            ErrorLog.LogMessage(args.Message, "WinUI.UnhandledException");
    }
}