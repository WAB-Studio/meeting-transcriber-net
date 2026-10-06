using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The words under a meter read the loudest of a quarter of a second, measured in time.
/// </summary>
public class MeterWordsTests
{
    private static readonly LevelReading Speech = new(0.05f);

    private static readonly LevelReading Nothing = new(0f);

    [Fact]
    public void A_pause_between_two_words_does_not_read_as_nothing_arriving()
    {
        var words = new MeterWords();

        words.Take([Reading(Speech)], 0).ShouldBeFalse();
        words.Take([Reading(Nothing)], 50).ShouldBeFalse();
        words.Take([Reading(Nothing)], 250).ShouldBeTrue();

        words.Said.Single().Level.ShouldBe(Speech);
    }

    [Fact]
    public void A_quiet_stretch_reads_as_quiet_once_the_loud_one_was_written()
    {
        var words = new MeterWords();

        words.Take([Reading(Speech)], 0);
        words.Take([Reading(Nothing)], 250);
        words.Take([Reading(Nothing)], 300);
        words.Take([Reading(Nothing)], 500).ShouldBeTrue();

        words.Said.Single().IsSilent.ShouldBeTrue();
    }

    [Fact]
    public void A_late_tick_does_not_stretch_the_stretch()
    {
        var words = new MeterWords();

        words.Take([Reading(Speech)], 0);

        // The timer ticked a second late: the words are due at once, on the clock and not on a count.
        words.Take([Reading(Nothing)], 1000).ShouldBeTrue();
    }

    [Fact]
    public void A_new_meeting_starts_with_nothing_standing()
    {
        var words = new MeterWords();

        words.Take([Reading(Speech)], 0);
        words.Take([Reading(Speech)], 250);
        words.Forget();

        words.Said.ShouldBeEmpty();
        words.Take([Reading(Nothing)], 10_000).ShouldBeFalse();
    }

    private static ChannelReading Reading(LevelReading level) =>
        new()
        {
            Channel = AudioChannel.Microphone,
            Capturing = "A microphone",
            WasCapturing = null,
            Level = level,
            StoppedAt = null,
        };
}
