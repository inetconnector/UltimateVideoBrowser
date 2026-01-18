using Microsoft.Maui.Controls;
using MauiItemsView = Microsoft.Maui.Controls.ItemsView;

#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
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
            ScrollViewer.SetVerticalScrollBarVisibility(listView, ScrollBarVisibility.Visible);
            ScrollViewer.SetHorizontalScrollBarVisibility(listView, ScrollBarVisibility.Disabled);
            ScrollViewer.SetIsVerticalRailEnabled(listView, true);
            listView.Padding = new Thickness(0);
            listView.Margin = new Thickness(0);
        }
    }
#endif
}
