namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// The rule behind a pill opening under a pointer: "never dismissed" once overflowed into the
/// window, so every press on a fresh pill was read as a dismissal and no pill opened (O-20261006-11).
/// </summary>
public class PillPressTests
{
    private const long Now = 10_000;

    [Fact]
    public void A_press_on_a_pill_whose_list_is_open_closes_it()
    {
        PillPress.ClosesTheList(true, null, Now).ShouldBeTrue();
        PillPress.ClosesTheList(true, long.MinValue, Now).ShouldBeTrue();
    }

    [Fact]
    public void A_pill_nothing_has_dismissed_opens_on_a_press() =>
        PillPress.ClosesTheList(false, null, Now).ShouldBeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(299)]
    public void A_press_inside_the_window_is_the_one_that_dismissed_the_list(long before) =>
        PillPress.ClosesTheList(false, Now - before, Now).ShouldBeTrue();

    [Theory]
    [InlineData(300)]
    [InlineData(3_600_000)]
    public void A_press_at_or_past_the_window_opens_the_list(long before) =>
        PillPress.ClosesTheList(false, Now - before, Now).ShouldBeFalse();

    [Fact]
    public void A_dismissal_at_the_furthest_past_tick_does_not_overflow_into_the_window() =>
        PillPress.ClosesTheList(false, long.MinValue, 5_000_000).ShouldBeFalse();

    [Fact]
    public void A_dismissal_later_than_the_press_is_not_the_one_it_came_with() =>
        PillPress.ClosesTheList(false, Now + 1, Now).ShouldBeFalse();
}
