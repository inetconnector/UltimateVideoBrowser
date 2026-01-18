#if WINDOWS
using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;

namespace UltimateVideoBrowser.Platforms.Windows;

internal static class WinUiScrollBarMapper
{
    public static void Apply()
    {
        CollectionViewHandler.Mapper.AppendToMapping("ForceLeftVisibleScrollbar", (handler, view) =>
        {
            if (handler?.PlatformView is not ListViewBase lv)
                return;

            lv.Loaded += (_, __) =>
            {
                var sv = FindScrollViewer(lv);
                if (sv == null)
                    return;

                // Always show scrollbar (no auto-hide overlay behavior as much as possible).
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            };
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
            return sv;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }

        return null;
    }
}
#endif
