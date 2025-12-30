using System.Diagnostics;
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
        {
            Debug.WriteLine($"Unhandled exception: {exception}");
            Debug.WriteLine($"StackTrace: {exception.StackTrace}");
        }
        else
        {
            Debug.WriteLine($"Unhandled exception message: {args.Message}");
        }
    }
}