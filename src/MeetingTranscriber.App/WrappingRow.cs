using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.Foundation;

namespace MeetingTranscriber.App;

/// <summary>
/// A row of things that runs on to the next line when it runs out of width.
/// </summary>
/// <remarks>
/// <para>
/// Written because WinUI has no panel that does it and the design asks for one: the shapes a
/// meeting can be filed under are fourteen chips of fourteen different widths, laid out as a
/// flowing row on <c>docs/design/Clasificar.dc.html</c>. The two panels the platform does have are
/// each wrong in the same way — a <c>StackPanel</c> never wraps, and a
/// <c>VariableSizedWrapGrid</c> wraps into cells of one fixed size, which is a grid of the widest
/// chip and reads as a keypad rather than as a set of words.
/// </para>
/// <para>
/// It measures every child unbounded in the direction it flows, which is what lets a chip be as
/// wide as the words in it. Nothing here is a value the design owns: the two gaps are laid out by
/// whoever uses it, the way every other <c>Spacing</c> on every other screen is.
/// </para>
/// </remarks>
public sealed partial class WrappingRow : Panel
{
    /// <summary>How much room is left between two things on one line.</summary>
    public double HorizontalGap { get; set; }

    /// <summary>How much room is left between one line and the next.</summary>
    public double VerticalGap { get; set; }

    protected override Size MeasureOverride(Size available)
    {
        // Unbounded across, because what decides a child's width is its own content and never the
        // room left on the line it happens to land on. A child measured against the remaining width
        // would be squeezed by where it fell, so the same chip would be a different size depending
        // on how many stood before it.
        var room = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var line = 0d;
        var lineHeight = 0d;
        var width = 0d;
        var height = 0d;

        foreach (var child in Children)
        {
            child.Measure(room);
            var wanted = child.DesiredSize;

            if (line > 0 && line + HorizontalGap + wanted.Width > available.Width)
            {
                width = Math.Max(width, line);
                height += lineHeight + (height > 0 ? VerticalGap : 0);
                line = 0;
                lineHeight = 0;
            }

            line += (line > 0 ? HorizontalGap : 0) + wanted.Width;
            lineHeight = Math.Max(lineHeight, wanted.Height);
        }

        width = Math.Max(width, line);
        height += lineHeight + (height > 0 ? VerticalGap : 0);

        // Never wider than what there is. A line that could not be broken — one child wider than
        // the whole panel — is clipped rather than pushing the screen out from under everything
        // beside it.
        return new Size(
            double.IsInfinity(available.Width) ? width : Math.Min(width, available.Width),
            height);
    }

    protected override Size ArrangeOverride(Size given)
    {
        var x = 0d;
        var y = 0d;
        var lineHeight = 0d;

        foreach (var child in Children)
        {
            var wanted = child.DesiredSize;

            if (x > 0 && x + HorizontalGap + wanted.Width > given.Width)
            {
                x = 0;
                y += lineHeight + VerticalGap;
                lineHeight = 0;
            }

            x += x > 0 ? HorizontalGap : 0;
            child.Arrange(new Rect(x, y, wanted.Width, wanted.Height));
            x += wanted.Width;
            lineHeight = Math.Max(lineHeight, wanted.Height);
        }

        return given;
    }
}
