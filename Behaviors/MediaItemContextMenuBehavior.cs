#if WINDOWS
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using Windows.System;
using MauiControls = Microsoft.Maui.Controls;
using WinUIControls = Microsoft.UI.Xaml.Controls;
using WinUIPrimitives = Microsoft.UI.Xaml.Controls.Primitives;

namespace UltimateVideoBrowser.Behaviors;

/// <summary>
///     WinUI-only: shows a native right-click context menu for media tiles.
///     Keeps the tile UI clean (no per-tile buttons/checkboxes) and routes actions
///     through the existing commands on the page binding context.
/// </summary>
public sealed class MediaItemContextMenuBehavior : MauiControls.Behavior<MauiControls.Frame>
{
    public static readonly MauiControls.BindableProperty HostCollectionViewProperty =
        MauiControls.BindableProperty.Create(
        nameof(HostCollectionView),
        typeof(MauiControls.CollectionView),
        typeof(MediaItemContextMenuBehavior),
        default(MauiControls.CollectionView));

    private MauiControls.Frame? attachedFrame;
    private FrameworkElement? nativeElement;

    public MauiControls.CollectionView? HostCollectionView
    {
        get => (MauiControls.CollectionView?)GetValue(HostCollectionViewProperty);
        set => SetValue(HostCollectionViewProperty, value);
    }

    protected override void OnAttachedTo(MauiControls.Frame bindable)
    {
        base.OnAttachedTo(bindable);
        attachedFrame = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        TryHookNative(bindable);
    }

    protected override void OnDetachingFrom(MauiControls.Frame bindable)
    {
        UnhookNative();
        bindable.HandlerChanged -= OnHandlerChanged;
        attachedFrame = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        UnhookNative();
        if (sender is MauiControls.Frame frame)
            TryHookNative(frame);
    }

    private void TryHookNative(MauiControls.Frame frame)
    {
        try
        {
            if (frame.Handler?.PlatformView is FrameworkElement fe)
            {
                nativeElement = fe;
                fe.RightTapped += OnRightTapped;
            }
        }
        catch
        {
            // Best-effort: never crash due to platform-specific hooking.
            nativeElement = null;
        }
    }

    private void UnhookNative()
    {
        try
        {
            if (nativeElement != null)
                nativeElement.RightTapped -= OnRightTapped;
        }
        catch
        {
            // Ignore
        }
        finally
        {
            nativeElement = null;
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        try
        {
            if (attachedFrame?.BindingContext is not MediaItem item)
                return;

            var host = HostCollectionView;
            var binding = host?.BindingContext;
            if (binding == null)
                return;

            EnsureExplorerLikeSelection(host, item);

            var selected = GetSelectedItems(host);
            if (selected.Count == 0)
                selected.Add(item);

            var flyout = BuildFlyout(binding, item, selected);

            if (sender is FrameworkElement fe)
            {
                var position = e.GetPosition(fe);
                flyout.ShowAt(fe, new WinUIPrimitives.FlyoutShowOptions { Position = position });
            }
            else
            {
                flyout.ShowAt((FrameworkElement)sender);
            }

            e.Handled = true;
        }
        catch
        {
            // Best-effort: context menu should never crash the app.
        }
    }

    private static void EnsureExplorerLikeSelection(MauiControls.CollectionView? host, MediaItem item)
    {
        if (host == null)
            return;

        try
        {
            var selected = host.SelectedItems;
            if (selected == null)
                return;

            var isSelected = selected.Contains(item);
            if (isSelected)
                return;

            var ctrl = IsCtrlPressed();
            if (!ctrl)
                selected.Clear();

            selected.Add(item);
        }
        catch
        {
            // Ignore selection edge cases.
        }
    }

    private static System.Collections.Generic.List<MediaItem> GetSelectedItems(MauiControls.CollectionView? host)
    {
        try
        {
            return host?.SelectedItems?.OfType<MediaItem>().ToList() ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static bool IsCtrlPressed()
    {
        try
        {
            var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            // CoreVirtualKeyStates lives in Windows.UI.Core (WinRT) and is available on Windows.
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }
        catch
        {
            return false;
        }
    }

    private static WinUIControls.MenuFlyout BuildFlyout(object binding, MediaItem primaryItem,
        System.Collections.Generic.IReadOnlyList<MediaItem> selection)
    {
        var flyout = new WinUIControls.MenuFlyout();

        var allowFileChanges = GetBool(binding, "AllowFileChanges", defaultValue: true);
        var isMulti = selection.Count > 1;

        // Open / Share
        AddCommandItem(flyout, AppResources.OpenAction, binding, "PlayCommand", primaryItem);
        AddCommandItem(flyout, AppResources.ShareAction, binding, "ShareCommand", primaryItem);

        flyout.Items.Add(new WinUIControls.MenuFlyoutSeparator());

        if (isMulti)
        {
            AddCommandItem(flyout, AppResources.SaveAsAction, binding, "SaveAsMarkedCommand", null);
            AddCommandItem(flyout, AppResources.AddToAlbumAction, binding, "AddMarkedToAlbumCommand", null);

            if (allowFileChanges)
            {
                AddCommandItem(flyout, AppResources.CopyMarkedAction, binding, "CopyMarkedCommand", null);
                AddCommandItem(flyout, AppResources.MoveMarkedAction, binding, "MoveMarkedCommand", null);
                AddCommandItem(flyout, AppResources.DeleteMarkedAction, binding, "DeleteMarkedCommand", null);
            }

            AddCommandItem(flyout, AppResources.ClearMarkedAction, binding, "ClearMarkedCommand", null);
            return flyout;
        }

        // Single item actions
        AddCommandItem(flyout, AppResources.SaveAsAction, binding, "SaveAsCommand", primaryItem);
        AddCommandItem(flyout, AppResources.CopyMarkedAction, binding, "CopyItemCommand", primaryItem);

        flyout.Items.Add(new WinUIControls.MenuFlyoutSeparator());

        AddCommandItem(flyout, AppResources.OpenFolderAction, binding, "OpenFolderCommand", primaryItem);
        if (primaryItem.HasLocation)
            AddCommandItem(flyout, AppResources.OpenLocationAction, binding, "OpenLocationCommand", primaryItem);

        flyout.Items.Add(new WinUIControls.MenuFlyoutSeparator());

        // Tag people is meaningful for photos/graphics.
        if (primaryItem.MediaType is MediaType.Photos or MediaType.Graphics)
            AddCommandItem(flyout, AppResources.TagPeopleAction, binding, "TagPeopleCommand", primaryItem);

        if (allowFileChanges)
        {
            AddCommandItem(flyout, AppResources.RenameAction, binding, "RenameCommand", primaryItem);

            if (primaryItem.MediaType is MediaType.Photos or MediaType.Graphics)
            {
                flyout.Items.Add(new WinUIControls.MenuFlyoutSeparator());
                AddCommandItem(flyout, AppResources.RotateLeftAction, binding, "RotateLeftCommand", primaryItem);
                AddCommandItem(flyout, AppResources.RotateRightAction, binding, "RotateRightCommand", primaryItem);
                AddCommandItem(flyout, AppResources.MirrorAction, binding, "MirrorCommand", primaryItem);
            }

            flyout.Items.Add(new WinUIControls.MenuFlyoutSeparator());
            AddCommandItem(flyout, AppResources.DeleteMarkedAction, binding, "DeleteItemCommand", primaryItem);
        }

        return flyout;
    }

    private static bool GetBool(object binding, string propertyName, bool defaultValue)
    {
        try
        {
            var prop = binding.GetType().GetProperty(propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(binding) is bool b)
                return b;
        }
        catch
        {
            // Ignore
        }

        return defaultValue;
    }

    private static void AddCommandItem(WinUIControls.MenuFlyout flyout, string text, object binding,
        string commandProperty, object? parameter)
    {
        var item = new WinUIControls.MenuFlyoutItem { Text = text };
        item.Click += (_, _) => TryExecute(binding, commandProperty, parameter);
        flyout.Items.Add(item);
    }

    private static void TryExecute(object binding, string commandProperty, object? parameter)
    {
        try
        {
            var prop = binding.GetType().GetProperty(commandProperty,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(binding) is not ICommand cmd)
                return;

            if (cmd.CanExecute(parameter))
                cmd.Execute(parameter);
        }
        catch
        {
            // Ignore command execution failures.
        }
    }
}

#endif