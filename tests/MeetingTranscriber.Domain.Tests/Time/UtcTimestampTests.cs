using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Time;

public class UtcTimestampTests
{
    [Fact]
    public void An_offset_moves_to_utc_without_moving_the_instant()
    {
        var madridAfternoon = new DateTimeOffset(2026, 8, 5, 14, 30, 0, TimeSpan.FromHours(2));

        var timestamp = UtcTimestamp.From(madridAfternoon);

        timestamp.Value.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.Value.Hour.ShouldBe(12);
        timestamp.UnixMilliseconds.ShouldBe(madridAfternoon.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void A_local_datetime_is_converted_and_not_relabelled()
    {
        var local = new DateTime(2026, 8, 5, 14, 30, 0, DateTimeKind.Local);

        var timestamp = UtcTimestamp.From(local);

        timestamp.Value.Offset.ShouldBe(TimeSpan.Zero);
        timestamp.UnixMilliseconds.ShouldBe(new DateTimeOffset(local).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void A_datetime_without_a_kind_is_rejected()
    {
        var unspecified = new DateTime(2026, 8, 5, 14, 30, 0, DateTimeKind.Unspecified);

        Should.Throw<ArgumentException>(() => UtcTimestamp.From(unspecified));
    }

    [Fact]
    public void Sub_millisecond_precision_is_dropped()
    {
        var midnight = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);

        var timestamp = UtcTimestamp.From(midnight.AddTicks((TimeSpan.TicksPerMillisecond * 1) + 9_999));

        timestamp.Value.ShouldBe(midnight.AddMilliseconds(1));
    }

    [Fact]
    public void Timestamps_round_trip_through_the_text_they_are_stored_as()
    {
        var timestamp = UtcTimestamp.From(new DateTimeOffset(2026, 8, 5, 14, 30, 12, 345, TimeSpan.Zero));

        timestamp.ToString().ShouldBe("2026-08-05T14:30:12.345Z");
        UtcTimestamp.Parse(timestamp.ToString()).ShouldBe(timestamp);
    }

    [Fact]
    public void Text_that_is_not_a_utc_timestamp_is_rejected()
    {
        Should.Throw<ArgumentException>(() => UtcTimestamp.Parse("2026-08-05 14:30:12"));
    }

    [Fact]
    public void The_gap_between_two_instants_is_a_duration()
    {
        var start = UtcTimestamp.From(new DateTimeOffset(2026, 8, 5, 14, 0, 0, TimeSpan.Zero));

        var end = start + Duration.FromMilliseconds(7_200_000);

        (end - start).ShouldBe(Duration.FromMilliseconds(7_200_000));
        end.ToString().ShouldBe("2026-08-05T16:00:00.000Z");
        (start < end).ShouldBeTrue();
    }

    [Fact]
    public void Unix_milliseconds_survive_the_round_trip()
    {
        var timestamp = UtcTimestamp.From(new DateTimeOffset(2026, 8, 5, 14, 30, 12, 345, TimeSpan.Zero));

        UtcTimestamp.FromUnixMilliseconds(timestamp.UnixMilliseconds).ShouldBe(timestamp);
    }
}
