using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The one rule that reads a wrong program off a level, which is the only thing that ever says so:
/// Windows follows any process id it is given and hands back silence for the ones that play
/// nothing, so nothing throws and nothing ends.
/// </summary>
/// <remarks>
/// No device. What is being decided here is arithmetic over a level and a length of time, and the
/// whole point of it living in a type of its own is that the decision is provable without a
/// meeting, a program or a machine that has either.
/// </remarks>
public sealed class SilentProgramTests
{
    private static readonly CaptureTarget Program =
        new CaptureTarget.Program(new AudioProcess(8124, "teams", StartedBy: 1084));

    private static readonly CaptureTarget WholeMachine =
        new CaptureTarget.Endpoint(new AudioDevice("{0.0.0.0}.speakers", "Speakers (Realtek)", IsDefault: true));

    private static readonly LevelReading Nothing = new(0f);

    /// <summary>
    /// ISC-77. A program that has played nothing at all for long enough is the wrong program, and
    /// this is the only thing on the machine that can tell.
    /// </summary>
    [Fact]
    public void A_program_that_has_played_nothing_at_all_for_long_enough_says_so()
    {
        SilentProgram.HeardNothing(Program, Nothing, SilentProgram.Waits).ShouldBeTrue();
        SilentProgram.HeardNothing(Program, Nothing, SilentProgram.Waits + Duration.FromSeconds(600))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Somebody presses record before anybody speaks, every time. The wait is what keeps that from
    /// being read as the wrong program.
    /// </summary>
    [Fact]
    public void A_program_that_has_only_just_opened_says_nothing_yet()
    {
        SilentProgram.HeardNothing(Program, Nothing, Duration.Zero).ShouldBeFalse();
        SilentProgram.HeardNothing(Program, Nothing, SilentProgram.Waits - Duration.FromMilliseconds(1))
            .ShouldBeFalse();
    }

    /// <summary>
    /// The loudest since it opened and not the last second: a program that said one word an hour
    /// ago and has been quiet since is being followed correctly, and a meter emptied every time
    /// somebody looked at it would call that meeting a wrong program.
    /// </summary>
    [Fact]
    public void A_program_that_has_ever_made_a_sound_is_the_right_program()
    {
        SilentProgram.HeardNothing(Program, new LevelReading(0.0001f), Duration.FromSeconds(3600))
            .ShouldBeFalse();
    }

    /// <summary>
    /// ISC-139, read from its own side: a channel already recording the whole machine has nowhere
    /// to be moved to, so silence on it is a meeting nobody played anything into and not a choice
    /// anybody has to be offered.
    /// </summary>
    [Fact]
    public void A_channel_recording_the_whole_machine_is_never_the_wrong_program()
    {
        SilentProgram.HeardNothing(WholeMachine, Nothing, Duration.FromSeconds(3600)).ShouldBeFalse();
    }
}
