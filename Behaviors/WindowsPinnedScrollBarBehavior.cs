using Microsoft.Maui.Controls;

#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
#endif

namespace UltimateVideoBrowser.Behaviors;

public class WindowsPinnedScrollBarBehavior : Behavior<ItemsView>
{
#if WINDOWS
    protected override void OnAttachedTo(ItemsView bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.HandlerChanged += OnHandlerChanged;
        TryApply(bindable);
    }

    protected override void OnDetachingFrom(ItemsView bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is ItemsView itemsView)
            TryApply(itemsView);
    }

    private static void TryApply(ItemsView itemsView)
    {
        if (itemsView.Handler?.PlatformView is ListViewBase listView)
        {
            ScrollViewer.SetVerticalScrollBarVisibility(listView, ScrollBarVisibility.Visible);
            ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Disabled);
            ScrollViewer.SetIsVerticalRailEnabled(listView, true);
            listView.Padding = new Thickness(0);
            listView.Margin = new Thickness(0);
        }
    }
#endif
}
