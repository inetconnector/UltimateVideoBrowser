#if WINDOWS
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace UltimateVideoBrowser.Views;

public partial class MainPage
{
    private bool windowsKeyboardHooked;
    private UIElement? windowsKeyboardTarget;

    partial void TryHookPlatformKeyboard()
    {
        if (windowsKeyboardHooked)
            return;

        try
        {
            if (Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win)
                return;

            if (win.Content is not UIElement root)
                return;

            windowsKeyboardTarget = root;
            root.KeyDown += OnWindowsKeyDown;
            windowsKeyboardHooked = true;
        }
        catch
        {
            // Ignore: keyboard hook is best-effort.
        }
    }

    partial void UnhookPlatformKeyboard()
    {
        if (!windowsKeyboardHooked)
            return;

        try
        {
            if (windowsKeyboardTarget != null)
                windowsKeyboardTarget.KeyDown -= OnWindowsKeyDown;
        }
        catch
        {
            // Ignore
        }
        finally
        {
            windowsKeyboardTarget = null;
            windowsKeyboardHooked = false;
        }
    }

    private void OnWindowsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Do not hijack key presses when the user is typing.
        if (IsTextInputFocused())
            return;

        switch (e.Key)
        {
            case VirtualKey.PageDown:
                ScrollMediaByPage(true);
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                ScrollMediaByPage(false);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ScrollMediaByRows(1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ScrollMediaByRows(-1);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                ScrollToHeader();
                e.Handled = true;
                break;
            case VirtualKey.End:
                ScrollToEnd();
                e.Handled = true;
                break;
        }
    }

    private static bool IsTextInputFocused()
    {
        try
        {
            var focused = FocusManager.GetFocusedElement();
            return focused is TextBox or PasswordBox or RichEditBox or AutoSuggestBox;
        }
        catch
        {
            return false;
        }
    }

    private void ScrollMediaByRows(int rowDelta)
    {
        if (vm.MediaItems.Count == 0)
            return;

        var span = 4;
        try
        {
            var ctx = BindingContext;
            var prop = ctx?.GetType().GetProperty("GridSpan");
            if (prop?.GetValue(ctx) is int s && s > 0)
                span = s;
        }
        catch
        {
            span = 4;
        }

        var first = lastFirstVisibleIndex >= 0 ? lastFirstVisibleIndex : 0;
        var delta = Math.Max(1, Math.Abs(rowDelta)) * Math.Max(1, span);
        var targetIndex = rowDelta > 0
            ? Math.Min(vm.MediaItems.Count - 1, first + delta)
            : Math.Max(0, first - delta);

        var target = vm.MediaItems[targetIndex];
        MediaItemsView.ScrollTo(target, position: ScrollToPosition.Start, animate: true);
    }

    private void ScrollToEnd()
    {
        if (vm.MediaItems.Count == 0)
            return;

        var target = vm.MediaItems[^1];
        MediaItemsView.ScrollTo(target, position: ScrollToPosition.End, animate: true);
    }
}

#endif