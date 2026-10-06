namespace MeetingTranscriber.Presentation;

/// <summary>Where a picker's pill stands in the window it opens its list in.</summary>
/// <param name="Left">The pill's left edge, in the window's own coordinates.</param>
/// <param name="Top">The pill's top edge.</param>
/// <param name="Width">The pill's width.</param>
/// <param name="Height">The pill's height.</param>
public readonly record struct ListAnchor(double Left, double Top, double Width, double Height);

/// <summary>Where an open list goes, in the window's own coordinates.</summary>
/// <param name="Left">The list's left edge.</param>
/// <param name="Top">The list's top edge.</param>
/// <param name="Width">The list's width.</param>
/// <param name="Height">The list's height, already cut to the room on the side it opened.</param>
/// <param name="Above">Whether it opened over its pill and not under it.</param>
public readonly record struct ListPlace(double Left, double Top, double Width, double Height, bool Above);

/// <summary>
/// The one rule for where an open list sits: under its pill when there is room, over it when there
/// is not, and never outside the window.
/// </summary>
/// <remarks>
/// <para>
/// Plain arithmetic and nothing of a screen, so a build agent can run it. The platform's
/// <c>ComboBox</c> placed its own list for a carousel, in a window of its own, and three batches
/// moved it and the owner saw it misplaced three times; the application owns its drop-down now and
/// this is the part of it that decides where the list goes.
/// </para>
/// <para>
/// Eight pixels are kept from every edge of the window and two between the pill and the list. The
/// width is the larger of the pill's and the widest entry's, no wider than the window allows, and
/// the list starts at the pill's left edge and moves left only as far as it must to stay inside.
/// It opens below when the room below holds the height it wants or is at least the room above, and
/// otherwise above; either way its height is cut to the room on that side.
/// </para>
/// </remarks>
public static class ListPlacement
{
    /// <summary>What is kept from every edge of the window.</summary>
    public const double Edge = 8;

    /// <summary>What is kept between the pill and its list.</summary>
    public const double Gap = 2;

    /// <summary>Where the list opens.</summary>
    /// <param name="pill">The pill the list belongs to.</param>
    /// <param name="rootWidth">The width of the window's content.</param>
    /// <param name="rootHeight">The height of the window's content.</param>
    /// <param name="wantedWidth">The width of the widest entry.</param>
    /// <param name="wantedHeight">The height of all the entries.</param>
    /// <param name="ceiling">The tallest the list may be before it scrolls.</param>
    public static ListPlace For(
        ListAnchor pill,
        double rootWidth,
        double rootHeight,
        double wantedWidth,
        double wantedHeight,
        double ceiling)
    {
        var widest = Math.Max(0, rootWidth - (2 * Edge));
        var width = Math.Min(Math.Max(pill.Width, wantedWidth), widest);

        var left = Math.Min(pill.Left, rootWidth - Edge - width);
        left = Math.Max(left, Edge);

        var wanted = Math.Min(wantedHeight, ceiling);

        var below = pill.Top + pill.Height + Gap;
        var roomBelow = Math.Max(0, rootHeight - Edge - below);
        var roomAbove = Math.Max(0, pill.Top - Gap - Edge);

        var opensBelow = roomBelow >= wanted || roomBelow >= roomAbove;
        var height = Math.Min(wanted, opensBelow ? roomBelow : roomAbove);

        return new ListPlace(
            left,
            opensBelow ? below : pill.Top - Gap - height,
            width,
            height,
            !opensBelow);
    }
}
