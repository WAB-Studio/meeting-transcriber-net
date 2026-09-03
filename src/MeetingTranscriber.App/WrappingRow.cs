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
    /// <summary>
    /// The width the last measure broke its lines against.
    /// </summary>
    /// <remarks>
    /// Arranging breaks against the same width and not against the one it is given, which is what
    /// keeps the two passes agreeing. What is measured is a height, and a height is only true of
    /// one set of line breaks: a parent that hands back less room than it offered — anything that
    /// arranges a child at its desired size rather than stretching it — would otherwise wrap one
    /// more line here than the height reserved for, and the last line would be cut off with nothing
    /// to say so. Both panels using this stretch today, so the two widths agree; this is what keeps
    /// that from being the reason it works.
    /// </remarks>
    private double _brokeAt = double.PositiveInfinity;

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
        _brokeAt = available.Width;
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
        // The width measure broke against, so the lines are the lines whose height was reported.
        var room = double.IsInfinity(_brokeAt) ? given.Width : _brokeAt;
        var x = 0d;
        var y = 0d;
        var lineHeight = 0d;

        foreach (var child in Children)
        {
            var wanted = child.DesiredSize;

            if (x > 0 && x + HorizontalGap + wanted.Width > room)
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
