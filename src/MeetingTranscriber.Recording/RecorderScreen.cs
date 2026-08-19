using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What channel 0 will follow: one program, or everything the machine plays.
/// </summary>
/// <remarks>
/// A type rather than the nullable process the engine takes, because a screen has three answers
/// where the engine has two. <c>null</c> means the whole machine to <c>MeetingRecording.Start</c>,
/// and it means nobody has said yet to a screen — and a screen that spelled both of them the same
/// way would start a recording of every notification on the machine for somebody who had not
/// answered the question at all.
/// </remarks>
public sealed record RecorderSource
{
    private RecorderSource(AudioProcess? follow) => Follow = follow;

    /// <summary>The program channel 0 follows, or nothing when it is the whole machine.</summary>
    public AudioProcess? Follow { get; }

    /// <summary>Everything this machine plays, notifications and other applications included.</summary>
    // Cast because a record's copy constructor takes a RecorderSource, and `new(null)` cannot
    // tell that from the process this actually means: nothing to follow.
    public static RecorderSource TheWholeMachine { get; } = new((AudioProcess?)null);

    /// <summary>Whether this is the whole machine rather than one program.</summary>
    public bool IsTheWholeMachine => Follow is null;

    /// <summary>One program, and whatever the processes it started play.</summary>
    public static RecorderSource Following(AudioProcess program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new RecorderSource(program);
    }
}

/// <summary>
/// What has been said about the meeting that has not started yet. Every one of these is a
/// question only a person can answer, and each is <c>null</c> until they have.
/// </summary>
/// <remarks>
/// <see cref="Spoken"/> is the one that looks like it has an obvious default and does not.
/// <c>MeetingRecordings.Open</c> states why in its own words: a default guessed from the
/// application's own language would file an English meeting as Spanish for having a Spanish menu.
/// So it is asked for, on a screen that already has a language picker on it for something else
/// entirely, and the recording does not start until it has been answered.
/// </remarks>
public sealed record RecorderChoices
{
    /// <summary>Nobody has said anything yet, which is where every screen opens.</summary>
    public static RecorderChoices Nothing { get; } = new();

    /// <summary>The device channel 1 will record.</summary>
    public AudioDevice? Microphone { get; init; }

    /// <summary>What channel 0 will follow.</summary>
    public RecorderSource? Source { get; init; }

    /// <summary>
    /// What the meeting is expected to be spoken in, as the tag it is stored under. Never the
    /// language the application is being read in — see the remarks on this type.
    /// </summary>
    public string? Spoken { get; init; }

    /// <summary>
    /// Whether every question a recording cannot be started without has been answered.
    /// </summary>
    public bool Settled =>
        Microphone is not null && Source is not null && !string.IsNullOrWhiteSpace(Spoken);
}

/// <summary>
/// The recording screen as the facts that decide what can be pressed on it, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It holds no meeting, opens no device and starts nothing. That is what makes it the half of the
/// screen a build agent can run: everything here is a rule about what is offered, and everything
/// that needs a microphone is on the other side of it. A window builds one of these from what it
/// can see and reads the controls off it, so the same question is not answered once per button in
/// four handlers that drift apart.
/// </para>
/// <para>
/// Nothing is remembered here that can be read instead. The state comes off the meeting through
/// <see cref="RecorderStates.Of"/>, so a screen cannot come to believe a recording is running
/// after it has stopped.
/// </para>
/// </remarks>
public sealed record RecorderScreen
{
    /// <summary>What the screen is doing.</summary>
    public required RecorderState State { get; init; }

    /// <summary>What has been said about the meeting.</summary>
    public required RecorderChoices Chosen { get; init; }

    /// <summary>
    /// Whether the recording has offered the whole machine's audio — which it does when channel 0
    /// has heard nothing at all from the program it is following, for long enough that the program
    /// is the wrong one.
    /// </summary>
    /// <remarks>
    /// The offer is the recording's to make and this only carries it. Nothing on a screen decides
    /// that a program has been silent long enough, because that is a measurement and not a layout.
    /// </remarks>
    public bool WholeMachineOffered { get; init; }

    /// <summary>Whether it has already been taken, which happens at most once in a meeting.</summary>
    public bool WholeMachineTaken { get; init; }

    /// <summary>
    /// What can be pressed now: what the state reaches, less what has not been answered yet.
    /// </summary>
    public IReadOnlySet<RecorderPress> Available =>
        State.Reaches().Where(Allowed).ToHashSet();

    /// <summary>
    /// Whether this press is one that can be made now. Read off <see cref="Available"/> rather
    /// than worked out a second time, so the set a test asserts about whole and the answer a
    /// button is enabled from cannot come apart.
    /// </summary>
    public bool Allows(RecorderPress press) => Available.Contains(press);

    /// <summary>
    /// What has to be true beyond the state, per press. Two conditions and both are refusals a
    /// person would otherwise meet as an exception thrown out of a button.
    /// </summary>
    private bool Allowed(RecorderPress press) => press switch
    {
        // A recording that cannot say which microphone, what channel 0 follows and what will be
        // spoken is not a recording anybody can start: the first two open the wrong devices and
        // the third is a question nothing on this machine can answer.
        RecorderPress.Start => Chosen.Settled,

        // The offer has to have been made, and there has to be something to move. A recording
        // already following the whole machine has nowhere to move to, and one that has moved has
        // moved. Offered first and by itself, because it is the whole of the consent: what is not
        // in Available is not on screen, so there is nothing to press before the offer exists.
        RecorderPress.RecordTheWholeMachine =>
            WholeMachineOffered
            && !WholeMachineTaken
            && Chosen.Source?.IsTheWholeMachine == false,

        _ => true,
    };
}
