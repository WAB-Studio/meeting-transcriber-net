using System.Text.RegularExpressions;

using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Testing;

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
public sealed partial class SilentProgramTests
{
    private static readonly CaptureTarget Program =
        new CaptureTarget.Program(new AudioProcess(8124, "teams", StartedBy: 1084));

    private static readonly CaptureTarget WholeMachine = new CaptureTarget.TheWholeMachine();

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

    /// <summary>
    /// A channel moved onto a second program is judged on that program's own silence. Both what the
    /// last program delivered and when this one began are restarted at the handover, before the
    /// line the channel moves on: a silent program moved onto after a loud one is otherwise never
    /// reported, and one moved onto late is reported the instant it opens.
    /// </summary>
    /// <remarks>
    /// A source guard in <c>StoppingASourceTests</c>' shape, and for its reason: nothing here can
    /// construct a <c>CaptureSource</c>, so this reads the file and checks the shape. It catches
    /// the reset somebody deletes or moves below the handover, not the rewrite somebody argues for;
    /// the real-device proof is the walk of #102, which moves onto a second silent program and
    /// waits for the notice to come back.
    /// </remarks>
    [Fact]
    public void A_move_starts_the_silence_over()
    {
        var body = TheBodyOfListenTo().Match(SourceText.WithoutProse(TheSource()));

        body.Success.ShouldBeTrue(
            "CaptureSource.ListenTo is not where this reads for any more, and a guard that cannot "
            + "find the method passes over anything.");

        var handover = body.Value.IndexOf("Volatile.Write(ref stream, next)", StringComparison.Ordinal);
        var meter = body.Value.IndexOf("Volatile.Write(ref delivered, new SourceMeter())", StringComparison.Ordinal);
        var clock = body.Value.IndexOf("Volatile.Write(ref listeningSince,", StringComparison.Ordinal);

        handover.ShouldBeGreaterThan(-1, "the line the channel moves on has gone from ListenTo");
        meter.ShouldBeGreaterThan(-1, "a move no longer empties what the device delivered");
        clock.ShouldBeGreaterThan(-1, "a move no longer restarts the ten seconds");
        meter.ShouldBeLessThan(handover, "the delivered meter is emptied after the handover");
        clock.ShouldBeLessThan(handover, "the clock is restarted after the handover");
    }

    [GeneratedRegex(
        @"internal void ListenTo\([\s\S]*?(?=\n\s*(?:\[|(?:public|internal|private|protected)\s))")]
    private static partial Regex TheBodyOfListenTo();

    private static FileInfo TheSource() =>
        RepositoryTree.At("src/MeetingTranscriber.Audio/CaptureSource.cs");
}
