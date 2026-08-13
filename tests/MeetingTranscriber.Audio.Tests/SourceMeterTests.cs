namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The two questions a meter answers, which are not the same question: how loud it is now, and
/// how loud it has ever been.
/// </summary>
public class SourceMeterTests
{
    [Fact]
    public void A_meter_nothing_reached_is_silent()
    {
        var meter = new SourceMeter();

        meter.Read().IsSilent.ShouldBeTrue();
        meter.Loudest.IsSilent.ShouldBeTrue();
    }

    [Fact]
    public void A_reading_is_the_loudest_block_since_the_previous_one()
    {
        var meter = new SourceMeter();

        meter.Add(new LevelReading(0.2f));
        meter.Add(new LevelReading(0.6f));
        meter.Add(new LevelReading(0.1f));

        meter.Read().Peak.ShouldBe(0.6f);
    }

    /// <summary>
    /// What makes a meter a meter. A peak that was never cleared shows the loudest moment of the
    /// meeting for the rest of the meeting, and a person watching it cannot tell that their
    /// microphone died ten minutes ago.
    /// </summary>
    [Fact]
    public void Reading_it_starts_the_next_stretch()
    {
        var meter = new SourceMeter();
        meter.Add(new LevelReading(0.6f));
        meter.Read();

        meter.Read().IsSilent.ShouldBeTrue();
    }

    [Fact]
    public void The_loudest_it_has_been_survives_being_read()
    {
        var meter = new SourceMeter();
        meter.Add(new LevelReading(0.6f));
        meter.Read();
        meter.Add(new LevelReading(0.1f));

        meter.Loudest.Peak.ShouldBe(0.6f);
        meter.Read().Peak.ShouldBe(0.1f);
        meter.Loudest.Peak.ShouldBe(0.6f);
    }
}
