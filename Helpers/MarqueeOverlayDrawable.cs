using Microsoft.Maui.Graphics;

namespace UltimateVideoBrowser.Helpers;

/// <summary>
///     Draws the WinUI marquee (drag) selection rectangle.
///     This is purely visual; pointer handling and selection logic are implemented in platform code.
/// </summary>
public sealed class MarqueeOverlayDrawable : IDrawable
{
    /// <summary>
    ///     Whether the rectangle should currently be rendered.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    ///     Current marquee rectangle in view coordinates.
    /// </summary>
    public RectF Rect { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!IsVisible)
            return;

        var r = Rect;
        if (r.Width <= 1 || r.Height <= 1)
            return;

        // Use a subtle Windows-like selection color.
        // Note: this is intentionally simple to keep the drawable lightweight.
        canvas.SaveState();
        canvas.FillColor = new Color(0f, 120f / 255f, 215f / 255f, 0.14f);
        canvas.StrokeColor = new Color(0f, 120f / 255f, 215f / 255f, 0.85f);
        canvas.StrokeSize = 1;
        canvas.FillRectangle(r);
        canvas.DrawRectangle(r);
        canvas.RestoreState();
    }
}
