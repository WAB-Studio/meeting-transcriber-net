using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What channel 0 will follow: one program, or everything the machine plays.
/// </summary>
/// <remarks>
/// A type rather than the nullable process the engine takes, because a screen has three answers
/// where the engine has two. <c>null</c> means the whole machine to <c>MeetingRecording.Start</c>,
/// and it means there is no source at all to a screen, which is a screen with no microphone and so
/// no default to start from. The whole machine is what a screen starts on, on purpose
/// (<see cref="RecorderChoices.WithTheDefaults"/>); what stays apart is the engine's <c>null</c> and
/// the screen's <c>Nothing</c>.
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
/// What has been said about the meeting that has not started yet. What will be spoken waits on a
/// person; the microphone and what channel 0 follows start as they were last chosen, or as
/// Windows' default microphone and the whole machine, and a person changes either.
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

    /// <summary>
    /// What the person last said a meeting would be spoken in, when the picker still offers it: the
    /// language of the most recently started meeting that has a recording, and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The person's last answer and never the application's language, which is
    /// <c>MeetingRecordings.Open</c>'s reason for asking at all: a meeting file from a Spanish menu would be
    /// filed as Spanish. What is read is a meeting somebody recorded, so it is a row with an audio
    /// artifact — the fact <c>MeetingReading.Audio</c> reads — and not merely a row. A meeting
    /// that arrived as a paid response has none and never sets it, and neither does one that has
    /// only just been started and has no audio filed yet.
    /// </para>
    /// <para>
    /// Here and not on <c>MeetingRecordings</c>, which is the composition of what recording a
    /// meeting files: the window reads this, and the window is held to leaving every step of that
    /// composition alone (<c>SavingCardTests</c>). Reading what was said last time files nothing.
    /// </para>
    /// <para>
    /// Only the latest is asked. When that one's language is not on offer the answer is nothing,
    /// not the one before it: the picker then waits on a person, which is what it does on a corpus
    /// with no meeting in it.
    /// </para>
    /// </remarks>
    /// <param name="corpus">The corpus the next meeting is recorded into.</param>
    /// <param name="offered">The language tags the picker offers.</param>
    public static string? SpokenLastTime(CorpusDbContext corpus, IReadOnlySet<string> offered)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(offered);

        var last = corpus.Meetings
            .AsNoTracking()
            .Where(meeting => corpus.Artifacts.Any(
                artifact => artifact.MeetingId == meeting.Id && artifact.Kind == ArtifactKind.Audio))
            .OrderByDescending(meeting => meeting.StartedAt)
            .Select(meeting => meeting.Language)
            .FirstOrDefault();

        return last is not null && offered.Contains(last) ? last : null;
    }

    /// <summary>
    /// What was chosen last time, as far as this machine still offers it (ISC-220.3).
    /// </summary>
    /// <remarks>
    /// The microphone by id, compared as <see cref="AsTheMicrophonesAreNow"/> compares it, so a
    /// launch and a later re-reading agree about one endpoint. A program by its name, case-blind,
    /// and only when exactly one offered entry carries that name: two entries of one name cannot be
    /// told apart from what was kept, and guessing would put another window's audio on channel 0.
    /// Whatever is not found stays null, and <see cref="WithTheDefaults"/> answers it.
    /// </remarks>
    /// <param name="last">What was kept, or nothing.</param>
    /// <param name="microphones">What this machine offers now.</param>
    /// <param name="offered">The programs on offer now.</param>
    public static RecorderChoices AsLastChosen(
        LastSources? last,
        IReadOnlyList<AudioDevice> microphones,
        IReadOnlyList<OfferedProgram> offered)
    {
        ArgumentNullException.ThrowIfNull(microphones);
        ArgumentNullException.ThrowIfNull(offered);

        if (last is null)
        {
            return Nothing;
        }

        var microphone = last.MicrophoneId is null
            ? null
            : microphones.FirstOrDefault(
                device => device.Id.Equals(last.MicrophoneId, StringComparison.OrdinalIgnoreCase));

        RecorderSource? source = null;

        if (last.TheWholeMachine)
        {
            source = RecorderSource.TheWholeMachine;
        }
        else if (last.ProgramName is { } name)
        {
            var named = offered
                .Where(program => program.Process.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            source = named.Length == 1 ? RecorderSource.Following(named[0].Process) : null;
        }

        return new RecorderChoices { Microphone = microphone, Source = source };
    }

    /// <summary>
    /// These choices with what nobody has answered filled from the defaults (ISC-220.4): channel 0
    /// follows the whole machine, and the microphone is Windows' default one.
    /// </summary>
    /// <remarks>
    /// Never what will be spoken, which <see cref="Spoken"/> explains. A machine with no default
    /// microphone leaves that question waiting, drawn as wanting an answer.
    /// </remarks>
    /// <param name="microphones">What this machine offers now.</param>
    public RecorderChoices WithTheDefaults(IReadOnlyList<AudioDevice> microphones)
    {
        ArgumentNullException.ThrowIfNull(microphones);

        return this with
        {
            Source = Source ?? RecorderSource.TheWholeMachine,
            Microphone = Microphone ?? microphones.FirstOrDefault(device => device.IsDefault),
        };
    }

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
    /// going anywhere — the default moves the moment a headset is plugged in, and the name beside
    /// it moves with it — so the device that is still there is taken again as the machine now
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

/// <summary>The three questions a recording cannot start without an answer to.</summary>
public enum RecorderQuestion
{
    /// <summary>What channel 0 will follow.</summary>
    Source = 1,

    /// <summary>Which microphone channel 1 will record.</summary>
    Microphone = 2,

    /// <summary>What the meeting will be spoken in.</summary>
    Spoken = 3,
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
    /// Whether the recording has reported that nothing came from the program channel 0 follows —
    /// which it does when channel 0 has heard nothing at all from it, for long enough that the
    /// program is the wrong one.
    /// </summary>
    /// <remarks>
    /// The report is the recording's to make and this only carries it. Nothing on a screen decides
    /// that a program has been silent long enough, because that is a measurement and not a layout.
    /// It is one of the two reports that gate the notice and its two ways out — the whole machine,
    /// and another program — and <see cref="TheProgramWentAway"/> is the other.
    /// </remarks>
    public bool NothingCameFromTheProgram { get; init; }

    /// <summary>
    /// Whether the recording has reported that the program channel 0 follows has ended since
    /// channel 0 began following it.
    /// </summary>
    /// <remarks>
    /// Carried and never worked out, for the reason <see cref="NothingCameFromTheProgram"/> is: the
    /// report is the recording's to make. Where both stand, this is the one said, because it is
    /// the cause.
    /// </remarks>
    public bool TheProgramWentAway { get; init; }

    /// <summary>
    /// Whether the notice's own press for the whole machine has been made and has not come back,
    /// which is what takes the offer off screen while the move is under way.
    /// </summary>
    /// <remarks>
    /// Once the move has held, what ends the offer is channel 0 being on the whole machine, which
    /// <see cref="RecorderChoices.Source"/> says — channel 0 can be moved back onto a program from
    /// its picker now, and the notice must be able to be made about that one in its own right.
    /// </remarks>
    public bool WholeMachineTaken { get; init; }

    /// <summary>
    /// Whether a move of channel 0 is in flight, onto a program or onto the whole machine. The
    /// screen takes one move at a time because the session records each move under a gate and a
    /// second press would only queue behind it, reporting a move onto a source the first had
    /// already left.
    /// </summary>
    /// <remarks>
    /// The same fact <c>TheSourceIsBeingChanged</c> would be, so there is one name for it: this
    /// one is older, and what the notice asks about it is the same question.
    /// </remarks>
    public bool AnotherProgramIsBeingOpened { get; init; }

    /// <summary>
    /// Whether a move of channel 1 onto another microphone is in flight. One move at a time, for
    /// the reason <see cref="AnotherProgramIsBeingOpened"/> gives for channel 0.
    /// </summary>
    public bool TheMicrophoneIsBeingChanged { get; init; }

    /// <summary>
    /// Whether the notice is on screen, and channel 0's meter reads <em>sin señal</em>: the
    /// recording reported that nothing came or that the program went away, the whole machine has
    /// not been taken, no move is in flight, and a meeting is under way. The one answer both the
    /// notice and the meter read.
    /// </summary>
    /// <remarks>
    /// Not while a move is in flight: the recording already names the new program by then, and the
    /// notice would say something about a program that has not yet been judged. It returns, naming
    /// the program channel 0 is on, if the move is refused. A paused meeting counts as under way,
    /// so a program that goes away during a pause is said at once; the presses it offers come off
    /// the state table, which reaches them only while recording.
    /// </remarks>
    public bool TheNoticeIsOnScreen =>
        EitherReportStands
        && !WholeMachineTaken
        && !AnotherProgramIsBeingOpened
        && State.IsRecording();

    /// <summary>
    /// Whether either report stands. The one place that says so: the notice and both ways out read
    /// it, so they cannot come to disagree about which reports they answer to.
    /// </summary>
    private bool EitherReportStands => NothingCameFromTheProgram || TheProgramWentAway;

    /// <summary>Whether the notice says that nothing came, which is the other sentence's absence.</summary>
    public bool NothingCameIsOnScreen => TheNoticeIsOnScreen && !TheProgramWentAway;

    /// <summary>Whether the notice says that the program went away.</summary>
    public bool TheProgramWentAwayIsOnScreen => TheNoticeIsOnScreen && TheProgramWentAway;

    /// <summary>
    /// The applications on offer to channel 0, less the one it follows now, in the order the source
    /// picker lists them: by name, then window title, then process id.
    /// </summary>
    /// <remarks>
    /// The follower is left out by process id and never by what is on its title, which changes
    /// with a browser's tab: a list compared on it would offer the program being followed. One
    /// place decides the order, so the picker and the move list cannot disagree.
    /// </remarks>
    /// <param name="offered">What <c>AudioPrograms.Offered</c> says, one per application.</param>
    /// <param name="followingNow">The program channel 0 follows, or nothing.</param>
    public static IReadOnlyList<OfferedProgram> ProgramsOnOffer(
        IReadOnlyList<OfferedProgram> offered,
        AudioProcess? followingNow)
    {
        ArgumentNullException.ThrowIfNull(offered);

        return
        [
            .. offered
                .Where(program => program.Process.Id != followingNow?.Id)
                .OrderBy(program => program.Process.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(program => program.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(program => program.Process.Id),
        ];
    }

    /// <summary>
    /// Whether the microphone's device stopped responding, as the meters last read it.
    /// </summary>
    /// <remarks>
    /// Carried and not worked out here, exactly like <see cref="NothingCameFromTheProgram"/>: what says a
    /// source died is its stream having ended, which is a device's answer and not a layout. False
    /// is the ordinary case and the one this record opens in, so a screen nobody has metered offers
    /// nothing rather than everything.
    /// </remarks>
    public bool TheMicrophoneDied { get; init; }

    /// <summary>
    /// Whether opening it again has been asked for and has not come back, so the offer is not
    /// there to take a second time.
    /// </summary>
    /// <remarks>
    /// The same shape as <see cref="WholeMachineTaken"/> and for the same reason: what it guards is
    /// a press arriving while the first is still opening a device. What says the channel is dead is
    /// a reading up to a second old, so without this a second press lands on a channel the first
    /// one already brought back — and the meeting is told the microphone could not be opened
    /// immediately after being told it is recording again.
    /// </remarks>
    public bool TheMicrophoneIsBeingOpenedAgain { get; init; }

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
    /// What a choosing screen still waits on, so each question that has no answer can say so while
    /// it waits. Empty outside <see cref="RecorderState.Choosing"/>: a meeting under way has been
    /// through all three.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="RecorderChoices.Settled"/>'s own three conditions, so a picker drawn as
    /// wanting an answer and the press that is refused for the same missing answer cannot
    /// disagree.
    /// </remarks>
    public IReadOnlySet<RecorderQuestion> Unanswered
    {
        get
        {
            if (State != RecorderState.Choosing)
            {
                return new HashSet<RecorderQuestion>();
            }

            var waiting = new HashSet<RecorderQuestion>();

            if (Chosen.Source is null)
            {
                waiting.Add(RecorderQuestion.Source);
            }

            if (Chosen.Microphone is null)
            {
                waiting.Add(RecorderQuestion.Microphone);
            }

            if (string.IsNullOrWhiteSpace(Chosen.Spoken))
            {
                waiting.Add(RecorderQuestion.Spoken);
            }

            return waiting;
        }
    }

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
    /// What has to be true beyond the state, per press. Three conditions and each is a refusal a
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
            EitherReportStands
            && !WholeMachineTaken
            && !AnotherProgramIsBeingOpened
            && Chosen.Source?.IsTheWholeMachine == false,

        // The same two reports, and the same consent: pointing channel 0 somewhere else is a way
        // out of the same silence. It opens channel 0's picker and moves nothing, so pressing it
        // twice is opening a list twice. Only while the recorder half is on screen, because a
        // picker that is not there cannot be opened and choosing without the meter in view
        // defeats the point. And not while a move is under way.
        RecorderPress.ChooseAnotherProgram =>
            EitherReportStands
            && !WholeMachineTaken
            && !AnotherProgramIsBeingOpened
            && TheRecorderIsOnScreen
            && Chosen.Source?.IsTheWholeMachine == false,

        // A pick in either picker while the meeting records or is paused, one move of a channel at
        // a time. The state table says where; this says that the picker is on screen to have been
        // picked in and that the channel is not already on its way somewhere.
        // The notice's own press moves channel 0 too, and is in flight while WholeMachineTaken says
        // so; a pick made then would queue a second move behind it.
        RecorderPress.ChangeTheSource =>
            !AnotherProgramIsBeingOpened && !WholeMachineTaken && TheRecorderIsOnScreen,

        RecorderPress.ChangeTheMicrophone =>
            !TheMicrophoneIsBeingChanged && !TheMicrophoneIsBeingOpenedAgain && TheRecorderIsOnScreen,

        // The microphone has to have died, and nothing must already be opening it. The second half
        // is what the whole machine's `Taken` is: an offer stays on screen while the press it
        // belongs to is opening a device, and one taken twice reaches a channel the first press
        // brought back.
        RecorderPress.TryTheMicrophoneAgain =>
            TheMicrophoneDied && !TheMicrophoneIsBeingOpenedAgain && !TheMicrophoneIsBeingChanged,

        _ => true,
    };
}
