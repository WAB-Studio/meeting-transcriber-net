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
    public void Only_the_whole_machines_audio_carries_the_sequence_on(CaptureTarget listening, bool carries)
    {
        ReopenedSource.CarriesTheSequenceOn(listening).ShouldBe(carries);
    }

    /// <summary>
    /// The defect PR #280 nearly shipped, as a test. Both of these feed channel 0 and they answer
    /// differently, so anything asking about a carried sequence has to ask about the source and
    /// never about the channel number.
    /// </summary>
    [Fact]
    public void Both_ways_of_obtaining_channel_zero_answer_differently()
    {
        var program = new CaptureTarget.Program(Teams);
        var machine = new CaptureTarget.TheWholeMachine();

        program.Channel.ShouldBe(machine.Channel);
        ReopenedSource.CarriesTheSequenceOn(program).ShouldBeFalse();
        ReopenedSource.CarriesTheSequenceOn(machine).ShouldBeTrue();
    }

    [Fact]
    public void A_microphone_refuses_a_sequence_because_it_numbers_its_own_frames()
    {
        var refused = Should.Throw<AudioCaptureException>(
            () => ReopenedSource.EnsureMayCarryOn(new CaptureTarget.Endpoint(Jabra), Carried()));

        refused.Message.ShouldContain("Jabra Evolve 65");
        refused.Message.ShouldContain("numbers its own frames");
    }

    [Fact]
    public void A_program_refuses_a_sequence_because_a_recording_is_not_moved_onto_one()
    {
        var refused = Should.Throw<AudioCaptureException>(
            () => ReopenedSource.EnsureMayCarryOn(new CaptureTarget.Program(Teams), Carried()));

        refused.Message.ShouldContain("teams (pid 8124)");
        refused.Message.ShouldContain("is not moved onto");
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
            { new CaptureTarget.Program(Teams), false },
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
