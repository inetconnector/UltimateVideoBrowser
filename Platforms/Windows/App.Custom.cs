namespace UltimateVideoBrowser.WinUI;

public partial class App
{
    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        var exception = args.Exception;
        if (exception != null)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {exception}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {exception.StackTrace}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception message: {args.Message}");
        }
    }
}