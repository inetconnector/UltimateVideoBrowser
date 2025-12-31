namespace UltimateVideoBrowser.Helpers;

public static class Tooltip
{
    public static readonly BindableProperty TextProperty = BindableProperty.CreateAttached(
        "Text",
        typeof(string),
        typeof(Tooltip),
        default(string),
        propertyChanged: OnTextChanged);

    public static string? GetText(BindableObject view)
    {
        return (string?)view.GetValue(TextProperty);
    }

    public static void SetText(BindableObject view, string? value)
    {
        view.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not VisualElement element)
            return;

        // Apply immediately if a handler exists; otherwise apply when the handler is created.
        ApplyTooltip(element, newValue as string);

        element.HandlerChanged -= OnHandlerChanged;
        element.HandlerChanged += OnHandlerChanged;
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not VisualElement element)
            return;

        ApplyTooltip(element, GetText(element));
    }

    private static void ApplyTooltip(VisualElement element, string? text)
    {
#if WINDOWS
        try
        {
            if (element.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, null);
                }
                else
                {
                    var tt = new Microsoft.UI.Xaml.Controls.ToolTip { Content = text };
                    Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(fe, tt);
                }
            }
        }
        catch
        {
            // Ignore tooltip failures.
        }
#else
        // Tooltips are not supported on all platforms; no-op.
        _ = element;
        _ = text;
#endif
    }
}