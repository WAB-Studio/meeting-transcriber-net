namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// How the meter's shown level falls: at once up, and 20 dB in a second and a half down —
/// <c>docs/design.md</c> §Its ballistics.
/// </summary>
public class MeterBallisticsTests
{
    [Fact]
    public void A_louder_reading_is_shown_at_once()
    {
        MeterBallistics.Fall(-30, -10, 16).ShouldBe(-10);
    }

    [Fact]
    public void Three_quarters_of_a_second_falls_ten_decibels()
    {
        MeterBallistics.Fall(-10, -60, 750).ShouldBe(-20, 1e-9);
    }

    [Fact]
    public void A_second_and_a_half_falls_twenty_decibels()
    {
        MeterBallistics.Fall(-10, -60, 1500).ShouldBe(-30, 1e-9);
    }

    [Fact]
    public void The_level_never_falls_under_what_is_read()
    {
        MeterBallistics.Fall(-10, -15, 1500).ShouldBe(-15);
    }

    [Fact]
    public void The_level_never_falls_under_the_floor_the_scale_draws()
    {
        MeterBallistics.Fall(-55, -90, 1500).ShouldBe(MeterScale.Quietest);
    }

    [Fact]
    public void No_time_passing_moves_nothing()
    {
        MeterBallistics.Fall(-20, -60, 0).ShouldBe(-20);
    }
}
