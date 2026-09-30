namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Which sources a channel already being recorded can go on being laid out by the same sequence on.
/// </summary>
/// <remarks>
/// The engine's open contract, probed here because opening a stream needs a device and this rule
/// does not. It is not the question of whether a screen may offer to re-open a source: a microphone
/// is re-opened under a running channel today and it works, because its stream numbers its own
/// frames and carries no sequence for this rule to refuse.
/// </remarks>
public class ReopenedSourceTests
{
    private static readonly AudioDevice Jabra = new("{0.0.1.0}.jabra", "Jabra Evolve 65", false);

    private static readonly AudioProcess Teams = new(8124, "teams", StartedBy: 1084);

    /// <summary>Any sequence at all. What is in it never enters the answer.</summary>
    private static FramePositions Carried() => new(48_000);

    [Theory]
    [MemberData(nameof(WhatCarriesTheSequenceOn))]
    public void Both_ways_of_obtaining_channel_zero_carry_the_sequence_on_and_a_microphone_does_not(CaptureTarget listening, bool carries)
    {
        ReopenedSource.CarriesTheSequenceOn(listening).ShouldBe(carries);
    }

    [Fact]
    public void A_microphone_refuses_a_sequence_because_it_numbers_its_own_frames()
    {
        var refused = Should.Throw<AudioCaptureException>(
            () => ReopenedSource.EnsureMayCarryOn(new CaptureTarget.Endpoint(Jabra), Carried()));

        refused.Message.ShouldContain("Jabra Evolve 65");
        refused.Message.ShouldContain("numbers its own frames");
    }

    /// <summary>
    /// A program's audio numbers no frames either, so a running channel 0 may be carried onto one.
    /// It used to be refused, on the argument that which program a recording follows is what the
    /// recording is.
    /// </summary>
    [Fact]
    public void A_program_takes_a_sequence_without_objecting()
    {
        Should.NotThrow(
            () => ReopenedSource.EnsureMayCarryOn(new CaptureTarget.Program(Teams), Carried()));
    }

    [Fact]
    public void The_whole_machines_audio_takes_a_sequence_without_objecting()
    {
        Should.NotThrow(
            () => ReopenedSource.EnsureMayCarryOn(new CaptureTarget.TheWholeMachine(), Carried()));
    }

    /// <summary>
    /// A channel starting a sequence is not a channel continuing one, so nothing is refused. This
    /// is the arm the microphone's re-open goes through — an endpoint's stream numbers its own
    /// frames, so <c>CaptureSource.ListenTo</c> hands this null every time it moves onto one.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryShape))]
    public void Nothing_is_refused_when_no_sequence_is_being_carried_on(CaptureTarget listening)
    {
        Should.NotThrow(() => ReopenedSource.EnsureMayCarryOn(listening, carryingOn: null));
    }

    public static TheoryData<CaptureTarget, bool> WhatCarriesTheSequenceOn() =>
        new()
        {
            { new CaptureTarget.Endpoint(Jabra), false },
            { new CaptureTarget.Program(Teams), true },
            { new CaptureTarget.TheWholeMachine(), true },
        };

    public static TheoryData<CaptureTarget> EveryShape() =>
        new()
        {
            new CaptureTarget.Endpoint(Jabra),
            new CaptureTarget.Program(Teams),
            new CaptureTarget.TheWholeMachine(),
        };
}
