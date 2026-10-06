namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-225: dragging the bar at the top of the window moves it, anywhere on the bar except its
/// buttons. The arithmetic only; where the bar and the back button stand come from a screen.
/// </summary>
public class CaptionStripTests
{
    [Fact]
    public void With_no_back_button_the_strip_is_the_whole_width_from_the_top_edge()
    {
        var strips = CaptionStrip.For(1, 800, 60, null, 0);

        strips.ShouldBe([new PixelRect(0, 0, 800, 60)]);
    }

    [Fact]
    public void The_strip_includes_the_top_edge_above_the_bar()
    {
        CaptionStrip.For(1, 800, 60, null, 0)[0].Y.ShouldBe(0);
    }

    [Fact]
    public void The_caption_buttons_inset_is_left_out_of_the_right_end()
    {
        var strips = CaptionStrip.For(1, 800, 60, null, 138);

        strips.ShouldBe([new PixelRect(0, 0, 662, 60)]);
    }

    [Fact]
    public void The_back_button_cuts_the_strip_in_two()
    {
        var strips = CaptionStrip.For(1, 800, 60, (6, 46), 138);

        strips.ShouldBe([new PixelRect(0, 0, 6, 60), new PixelRect(46, 0, 616, 60)]);
    }

    [Fact]
    public void A_back_button_at_the_left_edge_leaves_no_empty_strip_before_it()
    {
        var strips = CaptionStrip.For(1, 800, 60, (0, 40), 0);

        strips.ShouldBe([new PixelRect(40, 0, 760, 60)]);
    }

    [Fact]
    public void The_scale_turns_layout_units_into_pixels()
    {
        var strips = CaptionStrip.For(1.5, 800, 60, (6, 46), 92);

        strips.ShouldBe([new PixelRect(0, 0, 9, 90), new PixelRect(69, 0, 993, 90)]);
    }

    [Fact]
    public void A_window_narrower_than_the_caption_buttons_has_nothing_to_drag()
    {
        CaptionStrip.For(1, 100, 60, null, 138).ShouldBeEmpty();
    }

    [Fact]
    public void The_columns_width_is_the_inset_over_the_scale()
    {
        CaptionStrip.ReservedWidth(138, 1.5).ShouldBe(92);
    }

    [Theory]
    [InlineData(138, 0)]
    [InlineData(138, -1)]
    [InlineData(138, double.NaN)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(-4, 1)]
    public void A_window_with_no_scale_has_no_width_to_give_and_is_left_alone(double inset, double scale)
    {
        // A minimised window reads a scale of zero, and a width over zero is infinity: the layout
        // refuses it with E_INVALIDARG from a callback nothing of ours catches (O-20261006-09).
        CaptionStrip.ReservedWidth(inset, scale).ShouldBeNull();
    }
}
