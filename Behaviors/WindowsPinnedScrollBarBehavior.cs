using Microsoft.Maui.Controls;
using MauiItemsView = Microsoft.Maui.Controls.ItemsView;

#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using WinScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using WinThickness = Microsoft.UI.Xaml.Thickness;
#endif

namespace UltimateVideoBrowser.Behaviors;

public class WindowsPinnedScrollBarBehavior : Behavior<MauiItemsView>
{
#if WINDOWS
    protected override void OnAttachedTo(MauiItemsView bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
        TryApply(bindable);
    }

    protected override void OnDetachingFrom(MauiItemsView bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is MauiItemsView itemsView)
            TryApply(itemsView);
    }

    private static void TryApply(MauiItemsView itemsView)
    {
        if (itemsView.Handler?.PlatformView is ListViewBase listView)
        {
            ScrollViewer.SetVerticalScrollBarVisibility(listView, WinScrollBarVisibility.Visible);
            ScrollViewer.SetHorizontalScrollBarVisibility(listView, WinScrollBarVisibility.Disabled);
            ScrollViewer.SetIsVerticalRailEnabled(listView, true);
            listView.Padding = new WinThickness(0, 0, 16, 0);
            listView.Margin = new WinThickness(0);
        }
    }
#endif
}
