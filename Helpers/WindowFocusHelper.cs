using System;
using System.Runtime.InteropServices;

#if ANDROID
using Android.Views;
using Microsoft.Maui.ApplicationModel;
#endif

#if MACCATALYST
using UIKit;
#endif
using Window = Microsoft.Maui.Controls.Window;

namespace UltimateVideoBrowser.Helpers;

/// <summary>
///     Best-effort helper to bring a MAUI <see cref="Microsoft.Maui.Controls.Window" /> to the foreground.
///     This is intentionally resilient: failures must never crash the app.
/// </summary>
internal static class WindowFocusHelper
{
    public static void TryBringToFront(Window window)
    {
        if (window?.Handler?.PlatformView == null)
            return;

#if WINDOWS
        TryBringToFrontWindows(window);
#elif ANDROID
        TryBringToFrontAndroid();
#elif MACCATALYST
        TryBringToFrontMacCatalyst();
#else
        // No-op on other platforms.
#endif
    }

#if WINDOWS
    private static void TryBringToFrontWindows(Microsoft.Maui.Controls.Window window)
    {
        // MAUI on Windows uses a WinUI window as its native platform view.
        if (window.Handler.PlatformView is not Microsoft.UI.Xaml.Window xamlWindow)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(xamlWindow);
        if (hwnd == IntPtr.Zero)
            return;

        // Ensure size/position requests are applied (MAUI may ignore Width/Height on first open).
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            var density = Microsoft.Maui.Devices.DeviceDisplay.MainDisplayInfo.Density;
            if (density <= 0)
                density = 1;

            var w = (int)Math.Round(Math.Max(320, window.Width) * density);
            var h = (int)Math.Round(Math.Max(240, window.Height) * density);
            var x = (int)Math.Round(Math.Max(0, window.X) * density);
            var y = (int)Math.Round(Math.Max(0, window.Y) * density);

            appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        }
        catch
        {
            // Best-effort only.
        }

        // Ensure it is not minimized and then request foreground.
        ShowWindow(hwnd, SW_RESTORE);

        // "TopMost toggling" is a common trick to reliably bring a window in front.
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetForegroundWindow(hwnd);

        // Finally, request activation on the WinUI window.
        try
        {
            xamlWindow.Activate();
        }
        catch
        {
            // Best-effort only.
        }
    }

    private const int SW_RESTORE = 9;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
        uint uFlags);
#endif

#if ANDROID
    private static void TryBringToFrontAndroid()
    {
        // Android doesn't allow arbitrary "bring to front" without user interaction.
        // This is best-effort: if the Activity exists, we just request attention.
        try
        {
            var activity = Platform.CurrentActivity;
            activity?.RunOnUiThread(() => { activity.Window?.AddFlags(WindowManagerFlags.TurnScreenOn); });
        }
        catch
        {
            // Best-effort only.
        }
    }
#endif

#if MACCATALYST
    private static void TryBringToFrontMacCatalyst()
    {
        try
        {
            UIKit.UIApplication.SharedApplication.KeyWindow?.MakeKeyAndVisible();
        }
        catch
        {
            // Best-effort only.
        }
    }
#endif
}