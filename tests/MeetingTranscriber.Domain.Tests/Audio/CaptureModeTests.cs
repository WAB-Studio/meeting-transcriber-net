using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Tests.Audio;

/// <summary>
/// How channel 0 was obtained is stored twice — in the corpus and beside a recording's blocks —
/// and a folder is read with no database open, so the name it is written under is a rule of the
/// domain's rather than of the storage layer's.
/// </summary>
public class CaptureModeTests
{
    [Theory]
    [InlineData(CaptureMode.ProcessLoopback, "process_loopback")]
    [InlineData(CaptureMode.FullLoopback, "full_loopback")]
    public void Modes_round_trip_through_the_name_they_are_stored_under(CaptureMode mode, string wireName)
    {
        mode.ToWireName().ShouldBe(wireName);
        CaptureModes.FromWireName(wireName).ShouldBe(mode);
    }

    /// <summary>
    /// What it decides is whether a file holds one program or the whole machine, so a name this
    /// build does not know is refused rather than read as either of them.
    /// </summary>
    [Fact]
    public void An_unknown_stored_name_is_not_guessed_at()
    {
        Should.Throw<AudioContractException>(() => CaptureModes.FromWireName("FullLoopback"));
    }

    [Fact]
    public void A_mode_the_domain_does_not_have_has_no_stored_name()
    {
        const CaptureMode unknown = (CaptureMode)99;

        Should.Throw<AudioContractException>(() => unknown.ToWireName());
    }
}
