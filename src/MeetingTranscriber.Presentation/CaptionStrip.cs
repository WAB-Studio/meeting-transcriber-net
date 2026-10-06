namespace MeetingTranscriber.Presentation;

/// <summary>A rectangle in physical pixels, from the window's top-left corner.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// The one rule for which part of the window drags it: the whole strip from the window's top edge
/// to the bottom of the bar, across the full width, except the back button and the width the
/// caption buttons take.
/// </summary>
/// <remarks>
/// <para>
/// Plain arithmetic and nothing of a screen, so a build agent can run it. The window's root has
/// padding, so the bar starts below the top edge and the strip above it is client area that moves
/// nothing, which is exactly where a hand goes to grab a window (fb-106, ISC-225). The strip is
/// handed to the window as its caption, in pixels, because that is what the platform's
/// non-client input source takes.
/// </para>
/// <para>
/// The back button is left out by cutting the strip around it, so the result is one rectangle
/// when it is hidden and two when it is shown, and a press on it is never a drag.
/// </para>
/// </remarks>
public static class CaptionStrip
{
    /// <summary>The rectangles that drag the window.</summary>
    /// <param name="scale">Physical pixels per layout unit.</param>
    /// <param name="windowWidth">The window's content width, in layout units.</param>
    /// <param name="barBottom">The bottom of the bar row, in layout units from the window's top.</param>
    /// <param name="back">The back button's left and right edges in layout units, or null when it is not shown.</param>
    /// <param name="rightInset">The width the caption buttons take, in layout units.</param>
    public static IReadOnlyList<PixelRect> For(
        double scale,
        double windowWidth,
        double barBottom,
        (double Left, double Right)? back,
        double rightInset)
    {
        var right = Pixels(windowWidth, scale) - Pixels(Math.Max(0, rightInset), scale);
        var height = Pixels(barBottom, scale);

        if (right <= 0 || height <= 0)
        {
            return [];
        }

        if (back is not { } button)
        {
            return [new PixelRect(0, 0, right, height)];
        }

        var from = Math.Clamp(Pixels(button.Left, scale), 0, right);
        var to = Math.Clamp(Pixels(button.Right, scale), from, right);
        var strips = new List<PixelRect>(2);

        if (from > 0)
        {
            strips.Add(new PixelRect(0, 0, from, height));
        }

        if (right > to)
        {
            strips.Add(new PixelRect(to, 0, right - to, height));
        }

        return strips;
    }

    private static int Pixels(double units, double scale) => (int)Math.Round(units * scale, MidpointRounding.AwayFromZero);
}
