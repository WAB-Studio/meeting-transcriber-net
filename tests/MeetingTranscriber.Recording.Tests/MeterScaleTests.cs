namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Where a level sits on the meter, which is the one arithmetic both halves of that component read:
/// how full the bar is, and where the numbers under it go.
/// </summary>
public class MeterScaleTests
{
    [Theory]
    [InlineData(-60f, 0d)]
    [InlineData(-40f, 1d / 3)]
    [InlineData(-20f, 2d / 3)]
    [InlineData(-12f, 0.8d)]
    [InlineData(0f, 1d)]
    public void Each_mark_falls_where_the_design_says_it_does(float decibels, double along)
    {
        // `docs/design.md` §The scale writes these five out as percentages — 0, 33.3, 66.7, 80 and
        // 100 — so the scale under the bar and the level drawn over it cannot disagree about where
        // −12 is, which is the boundary the colour changes at.
        MeterScale.Along(decibels).ShouldBe(along, tolerance: 0.0001);
    }

    [Fact]
    public void The_marks_are_the_five_that_are_written_under_the_bar()
    {
        MeterScale.Marks.ShouldBe([-60f, -40f, -20f, -12f, 0f]);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(0.1f)]
    [InlineData(float.PositiveInfinity)]
    public void Anything_past_full_scale_is_full_and_never_more(float decibels)
    {
        // A reading that clipped is something to see and not something to draw off the end of the
        // meter, so the right-hand end is where it stops.
        MeterScale.Along(decibels).ShouldBe(1);
    }

    [Theory]
    [InlineData(-61f)]
    [InlineData(-200f)]
    [InlineData(float.NegativeInfinity)]
    public void Anything_below_the_floor_is_the_left_hand_end(float decibels)
    {
        // Negative infinity is what a stretch where no sample moved reads as, and it clamps like
        // any other level below the floor rather than coming back as a number that is not one —
        // which would put the bar, the peak and every mark at NaN.
        MeterScale.Along(decibels).ShouldBe(0);
    }

    [Fact]
    public void The_hot_zone_starts_where_the_scale_puts_its_own_mark()
    {
        // The one number on the scale that is a judgement rather than a round figure, and the
        // segments change colour at it. Said here because the meter reads this constant to place
        // two different things — the boundary of the coloured layers and the number under it — and
        // a component that computed one of them separately is how they come apart.
        MeterScale.Marks.ShouldContain(MeterScale.HotFrom);
        MeterScale.Along(MeterScale.HotFrom).ShouldBe(0.8, tolerance: 0.0001);
    }
}
