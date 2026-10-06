namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-173.4.1: an open list sits under its pill or over it, whole, inside the window, at any height.
/// </summary>
/// <remarks>
/// The arithmetic only, which is the part a build agent can run. Where the pill is, and what
/// the window's root is, come from a screen; what is decided from them is here.
/// </remarks>
public class ListPlacementTests
{
    private const double Root = 800;

    private static readonly ListAnchor Pill = new(100, 100, 200, 34);

    [Fact]
    public void A_list_with_room_below_opens_below()
    {
        var place = ListPlacement.For(Pill, Root, 600, 150, 100, 280);

        place.Above.ShouldBeFalse();
        place.Top.ShouldBe(100 + 34 + ListPlacement.Gap);
        place.Height.ShouldBe(100);
    }

    [Fact]
    public void A_list_near_the_foot_with_more_room_above_opens_above()
    {
        var pill = new ListAnchor(100, 500, 200, 34);

        var place = ListPlacement.For(pill, Root, 600, 150, 200, 280);

        place.Above.ShouldBeTrue();
        place.Height.ShouldBe(200);
        (place.Top + place.Height).ShouldBe(500 - ListPlacement.Gap);
    }

    [Fact]
    public void A_list_that_fits_neither_side_is_cut_where_there_is_most_room()
    {
        // It does not hold what it wants either side, and below has the most: it is cut there.
        var place = ListPlacement.For(Pill, Root, 400, 150, 280, 280);

        place.Above.ShouldBeFalse();
        place.Top.ShouldBe(100 + 34 + ListPlacement.Gap);
        place.Height.ShouldBe(400 - ListPlacement.Edge - (100 + 34 + ListPlacement.Gap));
    }

    [Fact]
    public void A_list_never_leaves_the_window()
    {
        // A pill at the right edge with an entry wider than the pill: moved left as far as it must,
        // and never past the margin kept from either edge.
        var pill = new ListAnchor(700, 100, 90, 34);

        var place = ListPlacement.For(pill, Root, 600, 400, 100, 280);

        place.Left.ShouldBeGreaterThanOrEqualTo(ListPlacement.Edge);
        (place.Left + place.Width).ShouldBeLessThanOrEqualTo(Root - ListPlacement.Edge);
        place.Width.ShouldBe(400);

        var wider = ListPlacement.For(pill, Root, 600, 5000, 100, 280);

        wider.Width.ShouldBe(Root - (2 * ListPlacement.Edge));
        wider.Left.ShouldBe(ListPlacement.Edge);
    }

    [Fact]
    public void A_list_is_never_narrower_than_its_pill()
    {
        var place = ListPlacement.For(Pill, Root, 600, 20, 100, 280);

        place.Width.ShouldBe(Pill.Width);
        place.Left.ShouldBe(Pill.Left);
    }

    [Fact]
    public void A_list_taller_than_its_room_is_cut_to_it()
    {
        var pill = new ListAnchor(100, 40, 200, 34);

        var place = ListPlacement.For(pill, Root, 200, 150, 1000, 280);

        place.Above.ShouldBeFalse();
        place.Height.ShouldBe(200 - ListPlacement.Edge - (40 + 34 + ListPlacement.Gap));
        (place.Top + place.Height).ShouldBeLessThanOrEqualTo(200 - ListPlacement.Edge);
    }

    [Fact]
    public void A_list_is_no_taller_than_its_ceiling()
    {
        var place = ListPlacement.For(Pill, Root, 2000, 150, 1000, 280);

        place.Height.ShouldBe(280);
    }
}
