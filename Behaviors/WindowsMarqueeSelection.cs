#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using UltimateVideoBrowser.Helpers;

namespace UltimateVideoBrowser.Behaviors;

/// <summary>
///     WinUI-only: enables Explorer-like marquee (drag) selection for the media grid.
///     - Left mouse drag on empty space draws a rectangle and selects intersecting tiles.
///     - Ctrl keeps the existing selection and adds items under the rectangle.
///     Pointer handling is attached to the native ListView used by MAUI CollectionView.
/// </summary>
public static class WindowsMarqueeSelection
{
    public static IDisposable Attach(CollectionView host, GraphicsView overlay, MarqueeOverlayDrawable drawable)
    {
        var sub = new Subscription(host, overlay, drawable);
        sub.Attach();
        return sub;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly CollectionView host;
        private readonly GraphicsView overlay;
        private readonly MarqueeOverlayDrawable drawable;

        private ListViewBase? listView;
        private bool isArmed;
        private bool isDragging;
        private bool ctrlAtStart;
        private bool shiftAtStart;
        private Windows.Foundation.Point start;
        private Windows.Foundation.Point current;

        private readonly HashSet<object> baseSelection = new();
        private readonly HashSet<object> latestTarget = new();
        private readonly HashSet<object> lastAppliedTarget = new();
        private long lastApplyTick;

        public Subscription(CollectionView host, GraphicsView overlay, MarqueeOverlayDrawable drawable)
        {
            this.host = host;
            this.overlay = overlay;
            this.drawable = drawable;
        }

        public void Attach()
        {
            host.HandlerChanged += OnHandlerChanged;
            TryHook();
        }

        public void Dispose()
        {
            try
            {
                host.HandlerChanged -= OnHandlerChanged;
            }
            catch
            {
                // Ignore
            }

            Unhook();
        }

        private void OnHandlerChanged(object? sender, EventArgs e)
        {
            Unhook();
            TryHook();
        }

        private void TryHook()
        {
            try
            {
                // MAUI CollectionView uses a WinUI ListViewBase on Windows.
                listView = host.Handler?.PlatformView as ListViewBase;
                if (listView == null)
                    return;

                listView.PointerPressed += OnPointerPressed;
                listView.PointerMoved += OnPointerMoved;
                listView.PointerReleased += OnPointerReleased;
                listView.PointerCanceled += OnPointerCanceled;
                listView.PointerCaptureLost += OnPointerCaptureLost;
            }
            catch
            {
                // Best-effort: marquee selection must never crash the app.
                listView = null;
            }
        }

        private void Unhook()
        {
            try
            {
                if (listView != null)
                {
                    listView.PointerPressed -= OnPointerPressed;
                    listView.PointerMoved -= OnPointerMoved;
                    listView.PointerReleased -= OnPointerReleased;
                    listView.PointerCanceled -= OnPointerCanceled;
                    listView.PointerCaptureLost -= OnPointerCaptureLost;
                }
            }
            catch
            {
                // Ignore
            }
            finally
            {
                EndDrag();
                listView = null;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (listView == null)
                    return;

                if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
                    return;

                var pt = e.GetCurrentPoint(listView);
                if (!pt.Properties.IsLeftButtonPressed)
                    return;

                // Only start marquee if the user clicked on empty space (not on an item).
                var overItem = IsOverItem(pt.Position);
                if (overItem)
                {
                    isArmed = false;
                    return;
                }

                ctrlAtStart = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != 0;
                shiftAtStart = (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Shift) != 0;
                baseSelection.Clear();
                latestTarget.Clear();
                lastAppliedTarget.Clear();
                lastApplyTick = 0;

                var selected = host.SelectedItems;
                if (selected != null)
                {
                    // Ctrl/Shift: additive selection (keep existing selection as a base).
                    if (ctrlAtStart || shiftAtStart)
                    {
                        foreach (var x in selected.Cast<object>())
                            baseSelection.Add(x);
                    }
                    else
                    {
                        selected.Clear();
                    }
                }

                start = pt.Position;
                current = pt.Position;
                isArmed = true;
                isDragging = false;

                // Capture pointer so we keep receiving move events.
                listView.CapturePointer(e.Pointer);
                e.Handled = true;
            }
            catch
            {
                // Ignore
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                if (listView == null || !isArmed)
                    return;

                var pt = e.GetCurrentPoint(listView);
                if (!pt.Properties.IsLeftButtonPressed)
                {
                    EndDrag();
                    return;
                }

                current = pt.Position;

                // Small threshold to avoid accidental marquee when the user simply clicks.
                var dx = current.X - start.X;
                var dy = current.Y - start.Y;
                if (!isDragging && (dx * dx + dy * dy) < 16)
                    return;

                isDragging = true;

                var rect = NormalizeRect(start, current);
                UpdateOverlay(rect);

                // Build the latest selection target.
                latestTarget.Clear();
                if (ctrlAtStart || shiftAtStart)
                {
                    foreach (var x in baseSelection)
                        latestTarget.Add(x);
                }

                foreach (var x in HitTest(rect))
                    latestTarget.Add(x);

                // Throttle selection updates to avoid churn while the pointer is moving.
                // Overlay remains smooth because it's purely drawn.
                var now = Environment.TickCount64;
                if ((now - lastApplyTick) >= 33)
                {
                    ApplySelectionIfChanged(latestTarget);
                    lastApplyTick = now;
                }

                e.Handled = true;
            }
            catch
            {
                // Ignore
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndDrag();
        }

        private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndDrag();
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            try
            {
                if (isDragging)
                {
                    // Ensure the final rectangle selection is applied even if throttling skipped the last move.
                    ApplySelectionIfChanged(latestTarget);
                }

                isArmed = false;
                isDragging = false;
                baseSelection.Clear();
                latestTarget.Clear();
                lastAppliedTarget.Clear();
                UpdateOverlay(null);

                try
                {
                    listView?.ReleasePointerCaptures();
                }
                catch
                {
                    // Ignore
                }
            }
            catch
            {
                // Ignore
            }
        }

        private bool IsOverItem(Windows.Foundation.Point point)
        {
            try
            {
                if (listView == null)
                    return false;

                var elements = VisualTreeHelper.FindElementsInHostCoordinates(point, listView);
                foreach (var el in elements)
                {
                    if (el is DependencyObject dep)
                    {
                        var container = ItemsControl.ContainerFromElement(listView, dep);
                        if (container is ListViewItem)
                            return true;
                    }
                }
            }
            catch
            {
                // Ignore
            }

            return false;
        }

        private IEnumerable<object> HitTest(Windows.Foundation.Rect rect)
        {
            if (listView == null)
                yield break;

            try
            {
                // ItemsPanelRoot contains the realized (visible) containers.
                var panel = listView.ItemsPanelRoot as Panel;
                if (panel == null)
                    yield break;

                foreach (var child in panel.Children)
                {
                    if (child is not FrameworkElement fe)
                        continue;

                    // Compute bounds of the container in ListView coordinates.
                    var t = fe.TransformToVisual(listView);
                    var p = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                    var b = new Windows.Foundation.Rect(p.X, p.Y, fe.ActualWidth, fe.ActualHeight);

                    if (b.Width <= 1 || b.Height <= 1)
                        continue;

                    if (!b.IntersectsWith(rect))
                        continue;

                    var dc = fe.DataContext;
                    if (dc != null)
                        yield return dc;
                }
            }
            catch
            {
                // Ignore
            }
        }

        private void ApplySelectionIfChanged(HashSet<object> target)
        {
            var selected = host.SelectedItems;
            if (selected == null)
                return;

            try
            {
                // Skip work if nothing has changed.
                if (lastAppliedTarget.Count == target.Count && lastAppliedTarget.SetEquals(target))
                    return;

                // Remove items not in target.
                if (selected is System.Collections.IList list)
                {
                    for (var i = list.Count - 1; i >= 0; i--)
                    {
                        var x = list[i];
                        if (x != null && !target.Contains(x))
                            list.RemoveAt(i);
                    }

                    // Add missing items.
                    foreach (var x in target)
                    {
                        if (!list.Contains(x))
                            list.Add(x);
                    }
                }
                else
                {
                    // Fallback (should rarely happen)
                    var toRemove = selected.Cast<object>().Where(x => !target.Contains(x)).ToList();
                    foreach (var x in toRemove)
                        selected.Remove(x);

                    foreach (var x in target)
                    {
                        if (!selected.Contains(x))
                            selected.Add(x);
                    }
                }

                lastAppliedTarget.Clear();
                foreach (var x in target)
                    lastAppliedTarget.Add(x);
            }
            catch
            {
                // Ignore
            }
        }

        private void UpdateOverlay(Windows.Foundation.Rect? rect)
        {
            try
            {
                if (rect == null)
                {
                    drawable.IsVisible = false;
                    overlay.IsVisible = false;
                    overlay.Invalidate();
                    return;
                }

                drawable.IsVisible = true;
                overlay.IsVisible = true;
                drawable.Rect = new RectF((float)rect.Value.X, (float)rect.Value.Y, (float)rect.Value.Width,
                    (float)rect.Value.Height);
                overlay.Invalidate();
            }
            catch
            {
                // Ignore
            }
        }

        private static Windows.Foundation.Rect NormalizeRect(Windows.Foundation.Point a, Windows.Foundation.Point b)
        {
            var x1 = Math.Min(a.X, b.X);
            var y1 = Math.Min(a.Y, b.Y);
            var x2 = Math.Max(a.X, b.X);
            var y2 = Math.Max(a.Y, b.Y);
            return new Windows.Foundation.Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }
    }
}

#endif