using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Time;

public class DurationTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(6.3199997, 6_320)]
    [InlineData(2063.04, 2_063_040)]
    [InlineData(0.0005, 1)]
    [InlineData(0.0004, 0)]
    public void Seconds_from_a_provider_land_on_the_nearest_millisecond(double seconds, long milliseconds)
    {
        Duration.FromSeconds(seconds).Milliseconds.ShouldBe(milliseconds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_length_that_is_not_a_number_is_not_a_duration(double seconds)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Duration.FromSeconds(seconds));
    }

    [Fact]
    public void A_negative_number_of_seconds_is_still_not_a_duration()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Duration.FromSeconds(-0.5));
    }

    [Fact]
    public void A_duration_is_whole_milliseconds()
    {
        Duration.FromMilliseconds(7_200_000).Milliseconds.ShouldBe(7_200_000);
        Duration.Zero.Milliseconds.ShouldBe(0);
    }

    [Fact]
    public void A_negative_duration_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Duration.FromMilliseconds(-1));
    }

    [Fact]
    public void A_timespan_is_truncated_to_whole_milliseconds()
    {
        var almostFour = TimeSpan.FromTicks((TimeSpan.TicksPerMillisecond * 3) + 9_999);

        Duration.FromTimeSpan(almostFour).Milliseconds.ShouldBe(3);
    }

    [Fact]
    public void A_duration_survives_the_trip_through_a_timespan()
    {
        var duration = Duration.FromMilliseconds(50);

        Duration.FromTimeSpan(duration.ToTimeSpan()).ShouldBe(duration);
    }

    [Fact]
    public void Durations_add_and_compare()
    {
        var blink = Duration.FromMilliseconds(50);
        var twoHours = Duration.FromMilliseconds(7_200_000);

        (blink + twoHours).Milliseconds.ShouldBe(7_200_050);
        (twoHours - blink).Milliseconds.ShouldBe(7_199_950);
        (blink < twoHours).ShouldBeTrue();
        (twoHours >= blink).ShouldBeTrue();
    }

    [Fact]
    public void Subtracting_more_than_there_is_does_not_wrap_around()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Duration.FromMilliseconds(50) - Duration.FromMilliseconds(51));
    }
}
