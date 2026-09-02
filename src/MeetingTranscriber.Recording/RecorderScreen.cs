using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What channel 0 will follow: one program, or everything the machine plays.
/// </summary>
/// <remarks>
/// A type rather than the nullable process the engine takes, because a screen has three answers
/// where the engine has two. <c>null</c> means the whole machine to <c>MeetingRecording.Start</c>,
/// and it means nobody has said yet to a screen â€” and a screen that spelled both of them the same
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
    /// language the application is being read in â€” see the remarks on this type.
    /// </summary>
    public string? Spoken { get; init; }

    /// <summary>
    /// Whether every question a recording cannot be started without has been answered.
    /// </summary>
    public bool Settled =>
        Microphone is not null && Source is not null && !string.IsNullOrWhiteSpace(Spoken);

    /// <summary>
    /// These choices against the microphones this machine has now: the chosen one as the machine
    /// describes it today, or nothing chosen at all when the machine no longer offers it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this and <see cref="AsTheSourcesAreNow"/> have in common is the outcome and not the
    /// rule: a choice the machine stopped offering is dropped rather than carried into a recording.
    /// Unplugged, the device opens as a refusal thrown out of a button; still selected, it is a
    /// screen saying a recording can start when it cannot. What counts as the same offer differs
    /// between them, and each says its own.
    /// </para>
    /// <para>
    /// Here it is the id and nothing else, and the answer is the new description rather than what
    /// was chosen. What Windows says about an endpoint changes under a screen without the endpoint
    /// going anywhere â€” the default moves the moment a headset is plugged in, and the name beside
    /// it moves with it â€” so the device that is still there is taken again as the machine now
    /// describes it. Keeping the old one would leave a picker offering a list where one entry says
    /// something the machine has stopped saying.
    /// </para>
    /// </remarks>
    /// <param name="microphones">What this machine offers now.</param>
    public RecorderChoices AsTheMicrophonesAreNow(IReadOnlyList<AudioDevice> microphones)
    {
        ArgumentNullException.ThrowIfNull(microphones);

        if (Microphone is not { } chosen)
        {
            return this;
        }

        var still = microphones.FirstOrDefault(
            offered => offered.Id.Equals(chosen.Id, StringComparison.OrdinalIgnoreCase));

        return still == chosen ? this : this with { Microphone = still };
    }

    /// <summary>
    /// These choices against what this machine is playing now: what was chosen when it is still
    /// one of them, and nothing chosen at all when it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whole equality and not a process id, which is the other half of the pair
    /// <see cref="AsTheMicrophonesAreNow"/> describes. Windows hands a number that was one
    /// program's to whatever starts next, so a match on the id alone would put another
    /// application's audio on channel 0 with nothing on screen looking wrong: what says this is
    /// still the program somebody picked is everything the machine said about it. The whole machine
    /// is in every such list, so choosing it survives every re-reading.
    /// </para>
    /// <para>
    /// The reason it is here and not inside a handler is that both moments it matters are the same
    /// question: the list being read again while somebody is choosing, and the list being read once
    /// more at the moment record is pressed. A screen that answered it twice would be two rules for
    /// one refusal, and the one that costs a meeting is the one at the press.
    /// </para>
    /// </remarks>
    /// <param name="sources">What channel 0 could follow now.</param>
    public RecorderChoices AsTheSourcesAreNow(IReadOnlyList<RecorderSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        return Source is { } chosen && !sources.Contains(chosen)
            ? this with { Source = null }
            : this;
    }
}

/// <summary>
/// The recording screen as the facts that decide what can be pressed on it and which of its two
/// arrangements it is in, and nothing else.
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
    /// Whether the recording has offered the whole machine's audio â€” which it does when channel 0
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
    /// Whether the room under the recorder has the whole window — the meetings raised into it, or
    /// a meeting being read in it — so the recorder half is not on screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Told and never worked out here, because it is the one fact this record needs that is not
    /// about the meeting: which of its two arrangements the window is in belongs to the control
    /// that changes it. It is handed over so that everything following from it — what is on
    /// screen, and what says what a meeting is doing — is one answer rather than one per control.
    /// It opens as no, which is the arrangement a window nobody has rearranged is in.
    /// </para>
    /// <para>
    /// Not one of <see cref="RecorderPress"/>, though raising the list is a press on the same
    /// screen. That set is what a recording offers, and <c>Available</c> being empty is how a
    /// screen with nothing said on it is asserted; folding a control that is live before anything
    /// has been chosen into it would make that assertion say something else.
    /// </para>
    /// </remarks>
    public bool TheRoomBelowHasTheWindow { get; init; }

    /// <summary>
    /// Whether the recorder half is on screen: the pickers, the meters, the stopwatch and the
    /// presses that run a meeting.
    /// </summary>
    /// <remarks>
    /// There is no answer here about whether the room below <em>may</em> take the window, and its
    /// absence is what this card changed. The list used to be refused the window from the moment a
    /// meeting started, because what went with the recorder half was stop, the clock and every
    /// line a narrator is told about the moment a device dies — and a collapsed element is not in
    /// the automation tree at all. That left the front door's most-used control dead for the whole
    /// of every meeting. What answers it instead is <see cref="TheStripIsOnScreen"/>: the strip
    /// carries what the recorder half was carrying, so the raise costs nothing and is never
    /// refused.
    /// </remarks>
    public bool TheRecorderIsOnScreen => !TheRoomBelowHasTheWindow;

    /// <summary>
    /// Whether the strip above the room below is what says what a meeting under way is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exactly when the recorder half is not on screen and there is a meeting to say anything
    /// about, which is what makes it the recorder half's stand-in rather than a second opinion
    /// beside it: the two are never both up, and neither reads the recording for itself — both are
    /// set from this one record and from the one clock, so how long the meeting has been running
    /// cannot be two numbers. Written against <see cref="TheRecorderIsOnScreen"/> rather than
    /// against the field both come from, so the two cannot come apart into an arrangement where
    /// neither half is anywhere.
    /// </para>
    /// <para>
    /// <see cref="RecorderStates.IsInAMeeting"/> and not
    /// <see cref="RecorderStates.IsRecording"/>, which is the difference between the raise costing
    /// nothing and costing the two states nobody would think to check. Stop is what creates
    /// finishing, and stop is on the strip: a strip that went away on its own press would answer
    /// somebody with an empty window for the minutes a long meeting takes to be made. Starting is
    /// the same shape from the other end — two devices are opening, each with its own deadline.
    /// Neither has a clock or a press, so what the strip says in them is the state and nothing
    /// else, which is exactly what there is to say.
    /// </para>
    /// </remarks>
    public bool TheStripIsOnScreen => !TheRecorderIsOnScreen && State.IsInAMeeting();

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
