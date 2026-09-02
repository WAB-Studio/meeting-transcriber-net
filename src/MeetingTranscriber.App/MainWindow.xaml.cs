using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

// WinUI has a Duration of its own — an animation's, in ticks — and this file is one of the two
// places in the application where the domain's meets it. Aliased rather than qualified at the use,
// so that a use written later cannot quietly be the other one; `MeetingsDrawer` does the same.
using Duration = MeetingTranscriber.Domain.Time.Duration;

namespace MeetingTranscriber.App;

/// <summary>
/// The screen the application opens on. The next meeting above — what it will record, and record,
/// pause, carry on and stop — and the meetings already recorded below.
/// </summary>
/// <remarks>
/// <para>
/// One screen and not two, which is what this class is for. The meetings were a window behind a
/// button, and a list behind a button is a list nobody opens: what somebody comes back to this
/// application for is the meetings, and what they came to do once is record one. The half below is
/// <see cref="MeetingsDrawer"/>, which owns the list and which of its two positions it is in; what
/// this window owns about it is the half of the screen that goes when it takes the whole window.
/// </para>
/// <para>
/// It holds no rule of its own about what can be pressed, nor about which of the two arrangements
/// it is in. <see cref="RecorderScreen"/> answers all of it, in a project a build agent can run,
/// and every handler here asks it before doing anything — including the handlers of controls it
/// has just disabled, because a click already in flight arrives after that.
/// </para>
/// <para>
/// Two calls do not happen on this thread and it is not a preference in either case. Starting
/// opens two devices, each with a deadline for one that never answers; stopping pours the spools
/// onto a timeline, reads the file back and hashes it, which is minutes of work for a long
/// meeting. Both say so where they are defined.
/// </para>
/// <para>
/// The corpus is opened once, when the first meeting starts, and stays open until it is stopped.
/// That is <see cref="MeetingRecording"/>'s constraint rather than a choice made here: it holds
/// the context it was handed for as long as the meeting lasts, so one opened inside this handler
/// and let go of would be disposed by the time somebody pressed stop an hour later.
/// </para>
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The order the picker offers, and the order it reads a selection back in. One array, read
    /// twice, so the two cannot come apart.
    /// </summary>
    private static readonly UiLanguage[] Languages = Enum.GetValues<UiLanguage>();

    /// <summary>
    /// What a meeting can be said to be spoken in: the name a person picks, and the tag the corpus
    /// stores and the provider is asked for.
    /// </summary>
    /// <remarks>
    /// Two, because the application is written in two and nothing downstream could be asked for a
    /// third yet. The tags are spelled here rather than taken from what the application is being
    /// read in, which is the whole point of this picker existing beside the other one — the two
    /// answer different questions and a meeting spoken in English is not filed as Spanish for
    /// having been recorded from a Spanish menu.
    /// </remarks>
    private static readonly (UiText Name, string Tag)[] Spoken =
    [
        (UiTexts.SpanishName, "es"),
        (UiTexts.EnglishName, "en"),
    ];

    /// <summary>
    /// Where this application's corpus is, or what stopped it being found.
    /// </summary>
    /// <remarks>
    /// The refusal on it is settled for as long as this window is open and nothing here can lift
    /// one. Whether the folder holds a corpus is not settled: <see cref="ThereIsACorpus"/> asks
    /// that each time, because two of this screen's presses make one.
    /// </remarks>
    private readonly CorpusFolder _corpus;

    /// <summary>
    /// What has happened, as the lines it is made of rather than as the string it currently reads
    /// as, so that what is already on screen re-reads itself when the language changes.
    /// </summary>
    private readonly List<TextLine> _report = [];

    /// <summary>
    /// What asks the recording what it is hearing. Once a second, which is what the metering loop
    /// at a prompt does: it reads the meters, and it asks whether the program channel 0 is
    /// following has gone silent for long enough to be the wrong one.
    /// </summary>
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };

    private UiLanguage _language;
    private TextLine? _status;

    /// <summary>
    /// What the row about who is using the application knows that the field itself does not: what
    /// the last read of the corpus found, and whether a press is in flight. What is typed is read
    /// off the field at the moment it is asked, which is why that half is not kept here.
    /// </summary>
    private WhoIsUsingThisRow _whoIsUsingThis = WhoIsUsingThisRow.Unread;

    /// <summary>True while the pickers are being filled, so refilling them is not a choice.</summary>
    private bool _filling;

    /// <summary>
    /// Windows saying this machine's devices are not what they were, or nothing when it would not
    /// be asked. Held for the whole session and let go of when the window closes.
    /// </summary>
    private readonly DeviceChanges? _devices;

    /// <summary>
    /// The queue this window's work goes on, taken while there is still a thread that may read it
    /// off a window. A device change arrives on the audio service's thread, and reading a XAML
    /// object's property from there is the wrong apartment — so the queue is taken here, once,
    /// rather than asked for inside the handler that needs it.
    /// </summary>
    private readonly DispatcherQueue _drawnOn;

    /// <summary>
    /// What this machine has, as of the last time it said. Read when the window opens and again
    /// every time Windows says a device arrived, went or stopped being the default — which is the
    /// whole of ISC-158.6 on this screen: a microphone plugged in while somebody is looking at
    /// this list is in it, without the application being closed and opened again.
    /// </summary>
    private AudioDevice[] _microphones;

    /// <summary>
    /// One when a look is already queued and has not started. Windows fires several notifications
    /// for one headset going in — the endpoint arrives, its state changes, the default moves — and
    /// each of them means the same thing here: ask again. Collapsing them is not a timer wearing
    /// another hat; the ask still happens on what Windows said and never on a clock, and what it
    /// stops is three bounded questions where one answers all three.
    /// </summary>
    /// <remarks>
    /// Raised on the audio service's thread before the work is queued, and lowered by the work
    /// itself before it asks anything. Raised inside the queued work instead it would collapse
    /// nothing at all: the dispatcher runs what it is given one item after another, so every one of
    /// the three would find the flag down and put the same question to the machine again — three
    /// deadlines on the thread the window draws on, for one headset. A notification arriving while
    /// the ask is running does queue another look, which is right: the machine changed again.
    /// </remarks>
    private int _lookQueued;

    private RecorderSource[] _sources;

    private CorpusDbContext? _context;
    private MeetingRecording? _recording;

    /// <summary>Whichever press is still running, which is the only thing that owns it.</summary>
    private RecorderStep _step = RecorderStep.Nothing;

    /// <summary>
    /// Which step the save going on now is on, and nothing when no meeting is being saved.
    /// </summary>
    /// <remarks>
    /// Held rather than derived, because it is the one thing on this screen that no state of the
    /// recording can be read off: what a save is doing is known to the save and to nothing else.
    /// It arrives from the thread doing the work and is put down on the thread the screen is
    /// drawn on, which is what <c>OnStop</c> hands over an <see cref="IProgress{T}"/> for.
    /// </remarks>
    private SavingWork? _saving;

    /// <summary>
    /// Whether the window has been closed. Read by whichever press was in flight when it was, so
    /// that a recording that finished opening its devices into a window nobody is looking at is
    /// let go of by the handler that made it rather than published to a screen that is gone.
    /// </summary>
    private bool _closed;

    private bool _offered;
    private bool _taken;

    /// <summary>
    /// What each channel read the last time the devices were asked, which is once a second while a
    /// meeting runs. Kept rather than asked for again on every redraw: asking empties the meters,
    /// so a redraw that asked would show a channel somebody is talking into as hearing nothing.
    /// </summary>
    private IReadOnlyList<ChannelReading> _channels = [];

    /// <summary>
    /// The endpoint the machine is playing through, as of the last time it moved. Asked when a
    /// meeting starts and again when Windows says the default changed, which is the only thing
    /// that moves it — so a machine that will not answer leaves the line where it was rather than
    /// flickering, and a machine that answers is not asked sixty times a minute for one answer.
    /// </summary>
    private AudioDevice? _playback;

    private RecorderChoices _chosen = RecorderChoices.Nothing;

    public MainWindow(UiLanguage language, CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        // Before InitializeComponent: the bindings in the XAML are read while it runs.
        _language = language;
        _corpus = corpus;

        InitializeComponent();

        _watch.Tick += OnWatch;
        _drawnOn = DispatcherQueue;
        Closed += OnClosed;

        // Once, and not with the words. A strip is one control used twice, so both carry the same
        // x:Name and something has to tell them apart for a probe and for a tool; an id nobody
        // hears is not a text and has no business on the path a language change walks.
        TheOthers.Identity = "SourcePicker";
        Mine.Identity = "MicrophonePicker";

        Meetings.Open(corpus);
        Meetings.OpennessChanged += OnMeetingsMoved;
        Meetings.MeetingChosen += OnMeetingChosen;

        Reading.Open(corpus);
        Reading.Left += OnLeftTheMeeting;

        // Read off the drawer once here as well as on every move, so which position the screen
        // opens in is the drawer's answer rather than two defaults that happen to agree.
        OnMeetingsMoved(Meetings, EventArgs.Empty);

        var microphones = Ask(AudioDevices.Microphones, UiTexts.WindowsDidNotSayWhatMicrophonesThereAre);
        _microphones = [.. microphones ?? []];
        _sources = SourcesNow();

        // A report line and not the status one. The status says what the screen is doing and is
        // rewritten every time anything changes, so a machine with no microphone would announce it
        // once and lose it to the next refresh — and it is the one thing on this screen that
        // cannot be got round by pressing something else.
        //
        // Said only when the machine answered. An empty picker means one of two different things
        // and a machine that would not say is the other one: it has already said so in the report,
        // in this reader's language and in the machine's own words, and following that with this
        // would tell somebody whose audio service is stuck that their microphone does not exist.
        if (microphones is { Count: 0 })
        {
            Say(UiTexts.NoMicrophoneOnThisMachine);
        }

        // Before ReadIn, which is what draws the row: whether it is asking or showing is read off
        // the corpus here, and ReadIn only puts it on screen in this reader's language.
        ReadWhoIsUsingThis();

        ReadIn(language);

        // Last, and that is the whole of why it is here rather than beside the first list: what it
        // takes is a registration Windows holds until somebody gives it back, and a constructor
        // that threw after taking one would leave the audio service calling into a window that
        // never opened and whose Closed will never fire. Everything above may throw — reading the
        // meetings opens a database — and none of it can leave that behind.
        _devices = Ask(DeviceChanges.Listening, UiTexts.WindowsWillNotSayWhenTheDevicesChange);

        if (_devices is not null)
        {
            _devices.Changed += OnTheDevicesChanged;
        }
    }

    /// <summary>
    /// Somebody picked a language on this screen. What is done about it is not this window's.
    /// </summary>
    public event EventHandler<UiLanguage>? LanguageChosen;

    /// <summary>Somebody asked for the packaging checks, which are a window of their own.</summary>
    public event EventHandler? PackagingChecksAsked;

    /// <summary>
    /// Reads the whole window in this language: what the XAML bound, the title, the pickers, what
    /// has happened so far, the status line and the meetings under it. Nothing on screen is left
    /// in the one before.
    /// </summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();
        Title = UiTexts.RecordAMeeting.In(language);

        FillThePickers();
        ShowWhoIsUsingThis();
        Render();
        Refresh();

        // The drawer is half of this screen and not a window of its own, so it is read in the same
        // pass rather than told separately by whatever decides the language. Then the whole screen
        // again, because the words on the strip are the catalogue's and the pass above set them in
        // the language before.
        Meetings.ReadIn(language);
        Reading.ReadIn(language);
        Refresh();
    }

    /// <summary>
    /// What a text says in the language this window is being read in. The XAML binds to it, which
    /// is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>Says what the screen cannot say for itself — what happened around it.</summary>
    public void Report(UiText text) => Say(text);

    /// <summary>
    /// The screen as the facts that decide what can be pressed, built fresh every time rather than
    /// kept, so that it cannot come to disagree with the recording it describes.
    /// </summary>
    private RecorderScreen Screen() => new()
    {
        State = RecorderStates.Of(
            corpus: _corpus.Refusal is null,
            started: _recording is not null,
            paused: _recording?.IsPaused ?? false,
            step: _step),
        Chosen = _chosen,
        WholeMachineOffered = _offered,
        WholeMachineTaken = _taken,

        // The two ways the room below takes the window are one fact to everything above it: the
        // list raised into it, and a meeting being read in it. Read off the two controls rather
        // than kept, for the reason every other field here is — a copy of it updated by whichever
        // handler remembered to is how a screen comes to disagree with the arrangement it is in.
        TheRoomBelowHasTheWindow = Meetings.HasTheWholeWindow || Reading.IsShowingAMeeting,
    };

    /// <summary>
    /// Sets every control from the one answer. Nothing here decides anything: it is the reading of
    /// <see cref="RecorderScreen"/> onto a window, which is why it is one method and not a
    /// condition inside each of nine handlers.
    /// </summary>
    private void Refresh()
    {
        var screen = Screen();

        RecordButton.IsEnabled = screen.Allows(RecorderPress.Start);
        PauseButton.IsEnabled = screen.Allows(RecorderPress.Pause);
        ResumeButton.IsEnabled = screen.Allows(RecorderPress.Resume);
        StopButton.IsEnabled = screen.Allows(RecorderPress.Stop);

        // Visibility and not merely disabled: an offer that has not been made is not a button
        // waiting to become live, it is a thing there is no reason to have heard of yet.
        WholeMachineOffer.Visibility = screen.Allows(RecorderPress.RecordTheWholeMachine)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // The recorder half and the save are the same room, one at a time. Visibility and not
        // merely disabled, for the reason the offer above is: a picker nothing can be chosen in,
        // under a heading about recording a meeting, is a screen that reads as having lost the
        // meeting somebody just stopped rather than as one saving it.
        var saving = screen.State == RecorderState.Finishing;
        TheNextMeeting.Visibility = saving ? Visibility.Collapsed : Visibility.Visible;
        SavingCard.Visibility = saving ? Visibility.Visible : Visibility.Collapsed;
        ShowTheSteps();

        // What the next meeting records cannot be changed once one is running: the devices are
        // open, and the engine has no way to swap one out under a recording.
        var choosing = screen.State == RecorderState.Choosing;
        Mine.PickerIsLive = choosing;
        TheOthers.PickerIsLive = choosing;
        SpokenPicker.IsEnabled = choosing;
        RefreshTheMachineButton.IsEnabled = choosing;

        // Once, and read twice. The stopwatch on the card and the length on the strip are the same
        // meeting's clock said two ways, and #204's second decision is that nothing on this screen
        // asks a recording how long it has been going for itself.
        var clock = RecordingClock.Of(screen.State, _recording?.Card.StartedAt, Now());

        // What each half says before either of them moves, and that order is the whole of it. A
        // half is made visible at the start of its travel and its height is measured a frame later,
        // so one filled in afterwards arrives empty for the 300 ms of the move and animates to the
        // height of an empty card. The strip is the one this bites, because what it says is
        // cleared by the arrangement rather than by the meeting.
        ShowTheClock(clock);
        ShowTheStrip(screen, clock);
        ShowTheMeters(screen.State);

        // Then which of the two arrangements the window is in. Here rather than only where the
        // drawer moves, because the answer changes under a screen that is already up: a meeting
        // that starts or stops while the list has the window is the strip arriving or leaving with
        // nobody having pressed the header.
        ShowWhatTheRoomIsShowing(screen);

        Announce(screen.State);
    }

    /// <summary>
    /// Sets the clock: how long the meeting has been going, and nothing at all when none is.
    /// </summary>
    /// <remarks>
    /// Nothing here decides either half, and nothing is kept between draws — both instants come
    /// from outside and the answer is <see cref="RecordingClock"/>'s, in a project a build agent
    /// can run. Unlike a meter, asking costs nothing and takes nothing away, so there is no
    /// reading to cache and no moment at which a cached one would have to be refreshed.
    /// <para>
    /// The clock arrives rather than being read here, which is what makes it one clock. The strip
    /// says the same length while the meetings have the window, and the caller builds the answer
    /// once and hands it to both — a second <c>RecordingClock.Of</c> a line apart would be two
    /// readings of the machine's clock and two numbers on one screen.
    /// </para>
    /// </remarks>
    private void ShowTheClock(RecordingClock clock)
    {
        TheClock.Visibility = clock.Showing ? Visibility.Visible : Visibility.Collapsed;

        // Cleared and not left standing, for the reason a meter row is: the next meeting's first
        // frame is drawn from this control, and the last one's length under a new one is a screen
        // saying a meeting is an hour old the moment it starts.
        TheClock.Text = clock.Showing ? ScreenNumbers.Long(clock.Ran) : string.Empty;
    }

    /// <summary>
    /// Sets the strip: what the meeting is doing, how long it has been running, what channel 0 is
    /// following and which microphone is on it — and whether stop is live on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here decides anything either. Whether the strip is on screen at all is
    /// <see cref="RecorderScreen.TheStripIsOnScreen"/>'s and is acted on one step later; the
    /// length is the clock the caller already built; what channel 0 follows and which microphone
    /// is on it are the choices the recording was started from, which is what the pickers on the
    /// card say too. So the strip and the recorder half are two readings of one answer rather than
    /// two answers.
    /// </para>
    /// <para>
    /// Nothing is cleared on the way out, which is the opposite of what the clock and the meters
    /// do and is right for the opposite reason. Those are cleared because the next meeting's first
    /// frame is drawn from them; the strip's first frame is never the one left standing, because
    /// this runs before it travels and every arrangement change comes through
    /// <see cref="Refresh"/>. Clearing it here is what put a blank card on screen for the 300 ms of
    /// every raise, and it also measured the travel against a layout nobody was going to see.
    /// </para>
    /// <para>
    /// What was chosen and not what the channel is capturing now. A channel that moved is a line
    /// of its own in this row, in pico, naming what it moved from and to — so the strip going on
    /// saying what somebody picked is what that line is measured against, and a strip that
    /// followed the device silently would leave nothing on screen saying it had. Neither can be
    /// missing while this runs: both are answered before record can be pressed, and the only
    /// things that unsay them run in <see cref="RecorderState.Choosing"/>.
    /// </para>
    /// </remarks>
    private void ShowTheStrip(RecorderScreen screen, RecordingClock clock)
    {
        StripStopButton.IsEnabled = screen.Allows(RecorderPress.Stop);

        if (!screen.TheStripIsOnScreen)
        {
            return;
        }

        StripSays.Text = In(WhatTheMeetingIsDoing(screen.State));

        // The length only where there is one. Opening the devices and making the meeting have no
        // clock — RecordingClock says why — and a strip that showed the last reading through
        // either would be a screen saying a meeting is still being recorded.
        var line = clock.Showing
            ? ScreenNumbers.Beside(ScreenNumbers.Long(clock.Ran), Following(screen.Chosen))
            : Following(screen.Chosen);

        StripLine.Text = line;
    }

    /// <summary>
    /// What channel 0 is following and which microphone is on it, as the one line of data the
    /// strip carries.
    /// </summary>
    /// <remarks>
    /// The program's name and not what the picker shows, which is <see cref="NameOf"/> and puts
    /// the process id after it. That number is there to tell three programs called Teams apart
    /// while somebody is choosing between them; on a strip about the one already chosen it is a
    /// number answering a question nobody is asking, and `MainAbierto` draws the name alone. The
    /// microphone is the maker's name for the same reason — <c>DeviceLines</c> adds
    /// *(predeterminado)*, which says what Windows would have used had nobody said.
    /// <para>
    /// The whole machine goes through <see cref="Capturing"/>, which is the one place that turns
    /// "no name this machine gave" into the catalogue's sentence for it.
    /// </para>
    /// <para>
    /// Both are taken outright, the same way <see cref="OnRecord"/> takes them, and for the same
    /// reason: nothing starts a recording until every one of the three has been answered, and the
    /// only things that unsay one run in <see cref="RecorderState.Choosing"/> — the pickers, which
    /// are dead while a meeting is under way, and the end of a save. A screen that guarded them
    /// here would be drawing a strip with no source on it, which is a case this application does
    /// not have.
    /// </para>
    /// </remarks>
    private string Following(RecorderChoices chosen) =>
        ScreenNumbers.Beside(Capturing(chosen.Source!.Follow?.Name), chosen.Microphone!.Name);

    /// <summary>
    /// What the strip says a meeting under way is doing, in the word a glance takes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The screen is in a state this window has no word for.
    /// </exception>
    /// <remarks>
    /// The four <see cref="RecorderStates.IsInAMeeting"/> names, which is exactly the set the
    /// caller has already established — so the two states with no meeting in them are not arms
    /// here, and the last arm is unreachable for every state that exists today. It stops for the
    /// reason <see cref="Announce"/> stops, and it can never be the first to do so: a state added
    /// to the enum and given no line there stops the window from opening, on the refresh that
    /// opens it and long before anything can raise the list.
    /// </remarks>
    private static UiText WhatTheMeetingIsDoing(RecorderState state) => state switch
    {
        RecorderState.Starting => UiTexts.TheDevicesAreOpening,
        RecorderState.Recording => UiTexts.TheMeetingIsBeingRecorded,
        RecorderState.Paused => UiTexts.TheMeetingIsPaused,
        RecorderState.Finishing => UiTexts.TheMeetingIsBeingSaved,
        _ => throw new InvalidOperationException(
            $"The strip has no word for recorder state '{state}'."),
    };

    /// <summary>
    /// Asks the devices what they are hearing, which is the once-a-second half of the meters and
    /// the only thing that may do it.
    /// </summary>
    /// <remarks>
    /// Reading a level empties it — that is what makes a meter the stretch since somebody last
    /// looked — so this is called from the tick and from the moment a meeting starts, and never
    /// from a redraw. A press that read the meters again would find the stretch since a moment
    /// ago, which is nothing, and print the muted-channel answer over a channel somebody is
    /// talking into.
    /// </remarks>
    private void ReadTheDevices()
    {
        if (_recording is not { } recording)
        {
            return;
        }

        _channels = ChannelReading.ReadFrom(recording);
    }

    /// <summary>
    /// Asks which endpoint the machine is playing through, which is what says whether the room is
    /// hearing the other side of the meeting a second time.
    /// </summary>
    /// <remarks>
    /// Asked when a meeting starts and when Windows says the default moved, and at no other
    /// moment. It used to be asked once a second beside the meters, on the argument that the
    /// answer changes under a running meeting — which is true, and what changes it is exactly the
    /// event this window is now told about. Sixty questions a minute to notice one of them was the
    /// application not being told; being told is the same warning, sooner, for one question per
    /// headset.
    /// <para>
    /// A refusal leaves the answer where it was and writes nothing. What is being decided is a
    /// line beside a meter, and a machine that would not say is not worth a sentence in the report
    /// about a warning that did not appear.
    /// </para>
    /// </remarks>
    private void ReadWhatTheMachinePlaysThrough()
    {
        try
        {
            _playback = AudioDevices.Playback();
        }
        catch (AudioCaptureException)
        {
            // Left as it was, deliberately.
        }
    }

    /// <summary>
    /// Sets the meters from what was last read: what each channel is capturing, how loud it has
    /// been, whether its device is gone, and whether the room is hearing the other side twice.
    /// </summary>
    /// <remarks>
    /// Nothing here decides any of it, and nothing here asks a device anything. What says whether
    /// there is a meeting to meter at all is <see cref="RecordingMeters"/>, in a project a build
    /// agent can run; this is the setting of controls from that one answer, the same split as
    /// <see cref="Refresh"/> and for the same reason.
    /// </remarks>
    private void ShowTheMeters(RecorderState state)
    {
        var meters = RecordingMeters.Of(state, _playback, _channels);

        // The bars stand on the screen whether or not anything is arriving, and that is the redraw
        // rather than a regression. `docs/design.md` §The four layers: the hot zone is **visible
        // even when nothing is arriving**, so the colour is not something that appears out of
        // nowhere on the day it clips — and a meter that is not drawn at all until a recording
        // starts is a picker with an empty space under it, which is the arrangement §Where it goes
        // exists to stop. What is not drawn before a meeting is the level and the peak, which is
        // what a reading of nothing already says: `Show(null)` leaves the track and the hot zone
        // and paints neither.
        var others = meters.On(AudioChannel.Loopback);
        var mine = meters.On(AudioChannel.Microphone);

        Show(others, TheOthers);
        Show(mine, Mine);

        // Each of the three named where its words are, rather than reached through the row above.
        // A live region that nothing hands a sentence to renders blank and announces nothing, which
        // is the same failure as one bound in the XAML wearing different clothes — so the rule is
        // that every one of them appears in a call to Tell, and LiveRegionTests holds the screen to
        // it by name. Passing the control down through Show hid these two from that check, which is
        // how the check found them.
        Tell(HeardTwice, meters.TheOthersAreHeardTwice, UiTexts.TheOthersAreHeardTwice);
        Tell(OthersStopped, others?.Stopped ?? false, UiTexts.TheOthersChannelStoppedOnItsOwn);
        Tell(MineStopped, mine?.Stopped ?? false, UiTexts.TheMicrophoneChannelStoppedOnItsOwn);

        // What each channel moved from and to, as values rather than as words in the catalogue: a
        // device name is what this machine gave it and reads the same in every language. Where the
        // reading has no such name the value is the catalogue's own sentence in the language being
        // read, which is what Capturing is for and the one place it happens. A channel still on
        // what it opened with hands back nothing to move from, which is what says the line off.
        Tell(
            OthersMoved,
            others?.Moved ?? false,
            UiTexts.TheChannelMovedToAnotherDevice,
            Capturing(others?.WasCapturing),
            Capturing(others?.Capturing));
        Tell(
            MineMoved,
            mine?.Moved ?? false,
            UiTexts.TheChannelMovedToAnotherDevice,
            Capturing(mine?.WasCapturing),
            Capturing(mine?.Capturing));
    }

    /// <summary>What one channel's meter reads as: how loud it is, and the loudest it has been.</summary>
    /// <remarks>
    /// Taking the control rather than being written twice, because the two strips are one rule with
    /// nothing to tell them apart but which channel they are, and a second copy of it is a second
    /// chance to set the wrong one. What the two really do differ in — which sentence they say when
    /// the device is gone — is said where the line is, in <see cref="ShowTheMeters"/>.
    /// <para>
    /// The words are set here and the drawing is the meter's. A <see cref="ChannelStrip"/> is one
    /// control used twice, so it cannot hold words for either channel; and it would otherwise have
    /// to know which language it is in to say that nothing is arriving. So it draws and remembers,
    /// and every sentence on it comes off the catalogue through here or through
    /// <see cref="NameTheChannels"/>.
    /// </para>
    /// <para>
    /// Nothing here says what the channel is capturing any more, and that is the redraw: what it is
    /// capturing is what the picker above the bar says, and the moment the two stop agreeing the
    /// line beside it says so in pico with both names in it. A grey repeat of the same fact under
    /// every meter said it loudest where nothing had happened.
    /// </para>
    /// </remarks>
    private void Show(ChannelReading? reading, ChannelStrip strip)
    {
        // Cleared and not left standing. The reading belongs to a meeting that is over — but the
        // next meeting's first frame is drawn from these controls, and the last one's level is not
        // something to show for a second under a new one.
        if (reading is null)
        {
            strip.LoudnessSaid = string.Empty;
            strip.LoudestSoFarSaid = string.Empty;
            strip.Show(null);
            return;
        }

        // A level is a measurement and reads the same in every language, so the reading hands one
        // back as data. Having measured nothing is a sentence and the reading hands back none, so
        // the word for it comes from the catalogue — which is also what this screen exists to show:
        // an empty bar and a bar nothing has drawn yet look the same.
        strip.LoudnessSaid = reading.Loudness ?? In(UiTexts.NothingIsArriving);

        // The peak comes back from the call that draws the bar, because drawing is what moves it.
        //
        // The number itself is written the invariant way and not the reading language's, which is
        // the one place a value inside a catalogue sentence does not follow its language. It stands
        // beside the level directly above it, and that one comes off LevelReading already written
        // — so following the language here would put `pico -9,4` under `-16.2 dBFS`, two numbers
        // whose whole purpose is being compared to each other, punctuated two ways.
        strip.LoudestSoFarSaid = strip.Show(reading) is { } loudest
            ? UiTexts.TheLoudestSoFar.In(_language, loudest.ToString("0.0", CultureInfo.InvariantCulture))
            : string.Empty;
    }

    /// <summary>
    /// Says which channel each strip is: the mono chip, the role beside it, and what its picker
    /// chooses, for somebody who cannot see the two texts.
    /// </summary>
    /// <remarks>
    /// Every word of it is the catalogue's and is said again on every language change, which is why
    /// it is a call from <see cref="FillThePickers"/> rather than a binding inside
    /// <see cref="ChannelStrip"/>: one control is used for both channels, and a control that named
    /// itself would be a second place that has to agree with the contract about which channel is
    /// which. What a probe finds them by is not said here, because an automation id nobody hears is
    /// not a word and does not change with the language — that is set once, when the window opens.
    /// </remarks>
    private void NameTheChannels()
    {
        TheOthers.Describe(
            In(UiTexts.Channel0), In(UiTexts.TheOthersRole), In(UiTexts.WhatToRecordFromThisMachine));

        Mine.Describe(In(UiTexts.Channel1), In(UiTexts.MyRole), In(UiTexts.Microphone));
    }

    /// <summary>
    /// One of Olivo's named sizes, by the key it is settled under. A glyph built in code is still
    /// something on a screen, so it names a rank rather than carrying a number of its own.
    /// </summary>
    private static double Sized(string key) => (double)Application.Current.Resources[key];

    /// <summary>
    /// What a channel is capturing, in words a person reads: the name where the reading has one,
    /// and the catalogue's sentence where it hands back none — which is a channel on the whole
    /// machine's audio, the one thing nothing this machine named.
    /// </summary>
    private string Capturing(string? capturing) => capturing ?? In(UiTexts.EverythingThisMachinePlays);

    /// <summary>
    /// Shows or hides one of the lines somebody reading this screen through a narrator has to be
    /// told about the moment it appears, and says what it says while showing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every line of this is one step of a chain, and the chain breaking anywhere is a fault
    /// nobody sighted can see. A <c>Collapsed</c> element is not in the automation tree, and
    /// visibility only reaches that tree when layout runs — so the line is shown, then laid out,
    /// and only then given its words. A live region is announced on its text changing and not on
    /// its visibility, which is why the words are set here instead of bound in the XAML at all.
    /// And the event is raised rather than left to the framework, because "a text change raises
    /// <c>LiveRegionChanged</c>" is a belief about WinUI that nothing here can run, while raising
    /// it is something this code does. <c>FromElement</c> answers with nothing when no peer has
    /// been made, which is exactly when nobody is listening.
    /// </para>
    /// <para>
    /// Nothing happens at all when the line already says this. Asked here rather than left to the
    /// property system, because this runs once a second for as long as a dead microphone stays
    /// dead: a narrator re-reading the whole fault every second is a screen somebody switches off,
    /// and it would depend on WinUI short-circuiting an equal assignment — another belief nothing
    /// here can run. A language switch really is a change and is announced, which is right.
    /// </para>
    /// <para>
    /// What no probe here reaches is a narrator reading it out, which needs a packaged host and
    /// Narrator: it is run by hand and written down. What is held is the shape — every live region
    /// on this screen gets its words from a call to this, and <c>LiveRegionTests</c> goes red if
    /// one binds them in the XAML or is never told anything at all.
    /// </para>
    /// </remarks>
    /// <param name="values">
    /// What the entry leaves room for, where it leaves room for anything. An entry told nothing is
    /// read rather than formatted, which is not a shortcut: most lines on this screen leave room
    /// for nothing at all, and putting them all through a formatter would turn an entry somebody
    /// later writes a brace into from a stray character on screen into a screen that throws while a
    /// meeting is being recorded.
    /// </param>
    private void Tell(TextBlock line, bool showing, UiText says, params object?[] values)
    {
        ArgumentNullException.ThrowIfNull(says);

        var said = values.Length == 0 ? In(says) : says.In(_language, values);
        var words = showing ? said : string.Empty;

        if (line.Text == words)
        {
            return;
        }

        if (!showing)
        {
            line.Text = words;
            line.Visibility = Visibility.Collapsed;
            return;
        }

        line.Visibility = Visibility.Visible;
        line.UpdateLayout();
        line.Text = words;

        FrameworkElementAutomationPeer
            .FromElement(line)?
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>What the screen says about itself, which is one line and always the same one.</summary>
    /// <exception cref="InvalidOperationException">
    /// The screen is in a state this window has no line for.
    /// </exception>
    /// <remarks>
    /// The last arm stops rather than leaving the previous line standing, which is
    /// <see cref="RecorderStates.Reaches"/> on a state it does not have and
    /// <see cref="SayWhereTheCorpusIs"/> on a refusal it has no words for: the three tables in
    /// this window agree that an unknown key stops. Of the three this is the one whose silence
    /// would be hardest to see — a status line that keeps saying what it last said looks like a
    /// screen with nothing wrong in it, and somebody reads "recording" off a window that is doing
    /// something else. A state added to <see cref="RecorderState"/> and not given a line here is
    /// a fault of the code, which nothing a person does can reach.
    /// <para>
    /// It carries no test of the kind <c>CorpusTextTests</c> is for <see cref="SayWhereTheCorpusIs"/>,
    /// and the difference is where the two would first be met. A corpus refusal fires only for
    /// somebody whose folder is in that particular state, which a developer may never be in, so
    /// the throw there could reach a person before anybody saw it. This runs on every refresh in
    /// every state the screen passes through, and a state with no line here stops the window from
    /// opening on the first run after it was added.
    /// </para>
    /// </remarks>
    private void Announce(RecorderState state)
    {
        switch (state)
        {
            case RecorderState.WithoutACorpus:
                Status(UiTexts.ChoosingAnotherFolderIsNotHereYet);
                break;
            case RecorderState.Choosing:
                Status(UiTexts.ReadyToRecord);
                break;
            case RecorderState.Recording:
                Status(UiTexts.RecordingMeeting, TheMeetingBeingRecorded().MeetingId);
                break;
            case RecorderState.Paused:
                Status(UiTexts.PausedAndTheClockKeepsRunning);
                break;
            case RecorderState.Starting:
                Status(UiTexts.OpeningTheDevices);
                break;
            case RecorderState.Finishing:
                Status(UiTexts.MakingTheMeeting);
                break;
            default:
                throw new InvalidOperationException(
                    $"This screen has no line for recorder state '{state}'.");
        }
    }

    /// <summary>
    /// The save saying which step it is on now.
    /// </summary>
    /// <remarks>
    /// A report can outlive the save it belongs to: it crosses to the thread the screen is drawn
    /// on, and what is already queued there can include the end of the stop that raised it. So
    /// what decides whether it still says anything is the state the screen is in — a report that
    /// lands after the save is over, or after the window closed, is put down rather than left
    /// standing as a step nothing is doing.
    /// </remarks>
    private void ShowTheSave(SavingWork underway)
    {
        if (_closed || _step != RecorderStep.Finishing)
        {
            return;
        }

        _saving = underway;
        ShowTheSteps();
    }

    /// <summary>
    /// The steps of the save going on now, each with the mark saying where it stands — and nothing
    /// at all when no meeting is being saved.
    /// </summary>
    /// <remarks>
    /// Built here rather than templated in the XAML, and thrown away and made again on every
    /// refresh, which is what the meeting cards below do and for the same two reasons: which steps
    /// exist is an answer <see cref="SavingTheMeeting"/> gives rather than a layout, and a line
    /// made again out of the catalogue cannot be left standing in the language it was born in.
    /// The second one is the rule every line on this screen keeps and not a path anybody walks
    /// during a save: the recorder's own language picker sits inside <c>TheNextMeeting</c>, which
    /// this state collapses for the whole of it.
    /// </remarks>
    private void ShowTheSteps()
    {
        SavingSteps.Children.Clear();

        if (_saving is not { } underway)
        {
            return;
        }

        foreach (var step in SavingTheMeeting.Steps)
        {
            var standing = SavingTheMeeting.StandingOf(step, underway);
            var line = Step(step, standing);

            SavingSteps.Children.Add(line);

            // The one under way is announced as it is added, so a narrator hears the save move
            // instead of finding out by walking the window for it — which is what this state is
            // for when nobody is looking at the screen. Said from here and never bound in the
            // XAML, for the reason LiveRegionTests carries: words a screen writes down are set
            // once, while the panel is collapsed, and never change again.
            if (standing == StepStanding.Underway)
            {
                Saying(line, In(Words(step)));
            }
        }
    }

    /// <summary>One step of the save, as its mark and its words.</summary>
    private UIElement Step(SavingWork step, StepStanding standing)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        line.Children.Add(Mark(standing));
        line.Children.Add(new TextBlock
        {
            Text = In(Words(step)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,

            // The one under way is the sentence somebody is reading; the rest are where it has
            // been and where it is going.
            Opacity = standing == StepStanding.Underway ? 1 : 0.65,
        });

        return line;
    }

    /// <summary>
    /// Makes one line of the save a live region and announces it, which is the only way a step
    /// changing reaches somebody who is not looking at the screen.
    /// </summary>
    /// <remarks>
    /// The whole line and not the words inside it, so what is read out is the step and its mark
    /// together. It is set on an element this window just built rather than on one the XAML
    /// declares, which is why <c>LiveRegionTests</c> does not reach it: what that check exists
    /// against is a region whose words are bound in markup, and there is no markup here to bind
    /// them in. What no probe reaches either way is a narrator reading it, which needs a packaged
    /// host and Narrator.
    /// </remarks>
    private static void Saying(UIElement line, string words)
    {
        AutomationProperties.SetLiveSetting(line, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(line, words);

        // Made and not looked up, which is where this parts from `Tell`. `FromElement` answers
        // with a peer only where one was made already, and `Tell` may lean on that because the
        // lines it announces are declared in the XAML and have been walked by then — no peer there
        // really does mean nobody is listening. This line was built and added a moment ago in this
        // same callback, so no peer exists for it whether or not anybody is listening, and asking
        // would answer with nothing every single time.
        FrameworkElementAutomationPeer
            .CreatePeerForElement(line)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>
    /// What a step's mark is, and what a narrator is told it means: a tick behind, a ring on the
    /// one running, and room for one ahead.
    /// </summary>
    /// <remarks>
    /// One table and not two, because the mark and its words are one answer about one standing.
    /// Held apart they were two switches over the same enum told from each other by nothing but
    /// their parameter names, and a standing added to one and not the other would draw a tick
    /// while saying "under way". A step still to come carries no words at all: there is nothing
    /// yet to report about it, and a narrator told something would hear one more thing having
    /// happened. The room is still taken, so the words below stay under the words above.
    /// </remarks>
    private FrameworkElement Mark(StepStanding standing)
    {
        var (mark, said) = standing switch
        {
            // Segoe Fluent's tick. A glyph is a code point and not a word, so it says the same
            // thing in either language and names no entry of the catalogue.
            StepStanding.Done => ((FrameworkElement)new FontIcon { Glyph = "", FontSize = Sized("BodySize") },
                (UiText?)UiTexts.ThisStepIsDone),
            StepStanding.Underway => (new ProgressRing { IsActive = true }, UiTexts.ThisStepIsUnderWay),
            StepStanding.NotYet => (new Border(), null),
            _ => throw new InvalidOperationException(
                $"This screen has no mark for step standing '{standing}'."),
        };

        mark.Width = 16;
        mark.Height = 16;
        mark.VerticalAlignment = VerticalAlignment.Center;

        if (said is not null)
        {
            AutomationProperties.SetName(mark, In(said));
        }

        return mark;
    }

    /// <summary>What a step of the save says it is doing.</summary>
    /// <remarks>
    /// Every step a save runs is here, and the throw is for one that is not — which is a fault of
    /// the code rather than anything a person can reach, since the steps a save runs are
    /// <see cref="SavingWork"/> itself. A member added there and not here is caught by
    /// <c>SavingCardTests</c> before it can be shown to anybody, which is the arrangement the
    /// meeting cards below are already under.
    /// </remarks>
    private static UiText Words(SavingWork step) => step switch
    {
        SavingWork.LettingTheSourcesGo => UiTexts.LettingBothSourcesGo,
        SavingWork.WritingTheMeetingDown => UiTexts.SavingTheAudioOfBothChannels,
        _ => throw new InvalidOperationException(
            $"This screen has no words for saving step '{step}'."),
    };

    /// <summary>The meeting being recorded, which only a state that has one may ask for.</summary>
    /// <remarks>
    /// <see cref="RecorderStates.Of"/> reads <see cref="RecorderState.Recording"/> off this field
    /// being set, so the throw is unreachable for as long as the two agree — and saying that is
    /// the point of it. The alternative is a `!`, which asserts the same thing silently and comes
    /// back as a <see cref="NullReferenceException"/> naming nothing on the day the two come
    /// apart; the alternative before that was a guard on the arm, which sent the state to a
    /// status line that went on saying whatever it last said.
    /// </remarks>
    private MeetingRecording TheMeetingBeingRecorded() =>
        _recording ?? throw new InvalidOperationException(
            "The screen reads as recording with no meeting under it.");

    /// <summary>
    /// Where the meetings go, said before the first one rather than found out afterwards — and
    /// when there is nowhere, which folder and what was wrong with it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The corpus refused for a reason this screen has no words for.
    /// </exception>
    /// <remarks>
    /// The last arm stops rather than substituting, which is <see cref="RecorderStates.Reaches"/>
    /// on a state it does not have and is the same rule for the same reason: a refusal added to
    /// <see cref="CorpusRefusal"/> and not given a text here would otherwise be shown to somebody
    /// as one of the others, sending them to check a folder that is fine or a setting they never
    /// wrote. A screen that says the wrong reason confidently is worse than one that stops, and
    /// this is a table falling behind its enum — a fault of the code, which nothing a person does
    /// can reach. <c>CorpusTextTests</c> is what catches it before it can be thrown.
    /// </remarks>
    private void SayWhereTheCorpusIs()
    {
        var text = _corpus.Refusal switch
        {
            null => ThereIsACorpus()
                ? UiTexts.MeetingsAreKeptAt
                : UiTexts.TheFirstThingKeptMakesTheCorpusAt,
            CorpusRefusal.SettingSaysNothingUsable => UiTexts.TheSettingSaysNothingUsable,
            CorpusRefusal.FolderDoesNotAnswer => UiTexts.TheCorpusFolderDidNotAnswer,
            CorpusRefusal.NoCorpusInTheFolder => UiTexts.ThereIsNoCorpusInThatFolder,
            CorpusRefusal.GoesWhenThePackageDoes => UiTexts.TheCorpusFolderGoesWhenThePackageDoes,
            _ => throw new InvalidOperationException(
                $"This screen has no text for corpus refusal '{_corpus.Refusal}'."),
        };

        // One entry, read once. Every arm above takes the path and nothing else, so there is no
        // second case here and no punctuation for this window to choose between two of them.
        CorpusText.Text = text.In(_language, _corpus.Path);
    }

    /// <summary>
    /// Whether there is a corpus in that folder as of now, rather than as of when this window
    /// opened.
    /// </summary>
    /// <remarks>
    /// Asked and not kept, which is what <see cref="MeetingsDrawer"/> already does on every read
    /// and for the same reason: the corpus comes into existence under this screen — keeping who is
    /// using the application makes one, and so does the first recording — so an answer read once
    /// would go on saying there is none under the press that just made one. What is kept is the
    /// refusal beside it, because nothing on this screen can lift one.
    /// </remarks>
    private bool ThereIsACorpus() =>
        _corpus.Folder is { } folder && CorpusDatabase.HoldsACorpus(folder);

    /// <summary>
    /// Reads who is using this application out of the corpus and puts it in the field.
    /// </summary>
    /// <remarks>
    /// Not part of <see cref="ReadIn"/>, which runs whenever the language changes: what is in that
    /// field may be half typed, and re-reading it there would take a name out from under somebody
    /// mid-answer because they switched language to read the question. The words around it are the
    /// XAML's own binding and do follow the language.
    /// </remarks>
    private void ReadWhoIsUsingThis()
    {
        var name = string.Empty;

        // No corpus yet is not a failure and is not read as one: nobody has answered, which is
        // what an empty field with the question under it already says. Keeping an answer is what
        // makes the corpus, the same way the first recording does.
        if (_corpus.Folder is { } folder && ThereIsACorpus())
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                name = new HumanLayer(context, TimeProvider.System).Me()?.DisplayName ?? string.Empty;
            }
            catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))
            {
                // Said rather than left blank. A corpus that will not open reads exactly like one
                // nobody has answered in, and the difference is somebody's own name: shown the
                // empty field alone, they would answer again believing nobody had.
                //
                // The row stays live through it, which is the other half. The press opens the
                // corpus the way this read did not — bringing the schema up — so a corpus one
                // migration behind is repaired by being answered; and answering cannot make a
                // second person however this read failed, because the write renames whoever
                // carries the flag rather than adding to them.
                Say(UiTexts.WhoIsUsingThisCouldNotBeRead);
                Dump(wouldNotRead.Message);
            }
        }

        // The facts before the field, because setting the field is a change and the change is
        // handled: OnWhoIsUsingThisTyped draws the row before the next line would have run.
        _whoIsUsingThis = _whoIsUsingThis with
        {
            CorpusIsReachable = _corpus.Folder is not null,
            SomebodyHasSaid = name.Length > 0,
        };

        WhoIsUsingThisBox.Text = name;
    }

    /// <summary>
    /// The row as the facts that decide what it does, built fresh rather than kept, so what is on
    /// screen cannot come to disagree with what is in the field.
    /// </summary>
    private WhoIsUsingThisRow WhoIsUsingThis() =>
        _whoIsUsingThis with { Typed = WhoIsUsingThisBox.Text };

    /// <summary>
    /// Sets the row's three controls from the one answer, the way <see cref="Refresh"/> sets the
    /// recorder's. Nothing here decides anything.
    /// </summary>
    private void ShowWhoIsUsingThis()
    {
        var row = WhoIsUsingThis();

        // Visibility and not merely a greyer line: an explanation that stayed would keep asking a
        // question this install has an answer to.
        NobodyHasSaidYet.Visibility = row.IsAsking ? Visibility.Visible : Visibility.Collapsed;
        WhoIsUsingThisBox.IsEnabled = row.FieldIsLive;
        WhoIsUsingThisButton.IsEnabled = row.MayBeKept;
    }

    private void FillThePickers()
    {
        NameTheChannels();

        // A device's name is what its maker called it, so it is data and has no language. The row
        // is not only the name, though, and what this application adds around it is words: which
        // entry an endpoint gets is DeviceLines', and the entry carries the name inside it, so
        // neither the word nor the bracket is picked on this line. The source picker beside it
        // still is — AudioProcess.ToString says why, and it is not settled here.
        //
        // Each strip is told what it offers and what is chosen in one call, and the strip's own
        // flag is the whole of what keeps that from being read as somebody choosing — the window's
        // `_filling` covers the two pickers below and nothing here.
        Mine.Offer(
            [.. _microphones.Select(device => DeviceLines.Of(device.Name, device.IsDefault).In(_language))],
            _chosen.Microphone is null
                ? -1
                : Array.FindIndex(_microphones, device =>
                    device.Id.Equals(_chosen.Microphone.Id, StringComparison.OrdinalIgnoreCase)));

        TheOthers.Offer(
            [.. _sources.Select(NameOf)],
            _chosen.Source is null ? -1 : Array.IndexOf(_sources, _chosen.Source));

        _filling = true;
        try
        {
            SpokenPicker.ItemsSource = Spoken.Select(offered => In(offered.Name)).ToArray();
            SpokenPicker.SelectedIndex = _chosen.Spoken is null
                ? -1
                : Array.FindIndex(Spoken, offered => offered.Tag == _chosen.Spoken);

            LanguagePicker.ItemsSource = Languages.Select(offered => In(UiLanguages.Endonym(offered))).ToArray();
            LanguagePicker.SelectedIndex = Array.IndexOf(Languages, _language);
        }
        finally
        {
            _filling = false;
        }

        SayWhereTheCorpusIs();
    }

    /// <summary>
    /// What is running now, with the whole machine first. First because it is the answer that is
    /// always available and always right about what it records, where following a program is the
    /// one that can turn out to have been the wrong process.
    /// </summary>
    private RecorderSource[] SourcesNow() =>
    [
        RecorderSource.TheWholeMachine,
        .. (Ask(AudioProcesses.Running, UiTexts.WindowsDidNotSayWhatIsPlaying) ?? [])
            .OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(program => program.Id)
            .Select(RecorderSource.Following),
    ];

    private string NameOf(RecorderSource source) =>
        source.IsTheWholeMachine ? In(UiTexts.EverythingThisMachinePlays) : source.Follow!.ToString();

    /// <summary>
    /// Asks the machine what it has, and answers nothing rather than failing to open when it will
    /// not say. A window that would not start because the audio stack was busy is worse than one
    /// that opens with an empty picker and a line saying so.
    /// </summary>
    /// <param name="machine">The question, which is asked once.</param>
    /// <param name="unanswered">
    /// What to say when it will not answer, in this reader's language. Handed in rather than picked
    /// here, because this method funnels more than one question — the microphones, the programs
    /// that are playing, and being told when either changes — and a sentence about one over an
    /// answer about another is a report saying something that did not happen.
    /// </param>
    /// <returns>
    /// What the machine said, or nothing at all when it would not say — which is not the same
    /// answer as an empty list and is not shown as one. What happened is in the report either way,
    /// as a sentence from the catalogue with the machine's own words under it.
    /// </returns>
    private T? Ask<T>(Func<T> machine, UiText unanswered)
        where T : class
    {
        try
        {
            return machine();
        }
        catch (AudioCaptureException wouldNotSay)
        {
            // The sentence first and the words under it, which is Dump's own rule: what comes off
            // an exception here is English whoever wrote it — Windows', or this application's —
            // and alone on a line it reads as the application talking to somebody in a language
            // they did not choose.
            Say(unanswered);
            Dump(wouldNotSay.Message);
            return null;
        }
    }

    /// <remarks>
    /// The strip refuses its own refilling, so what arrives here is somebody choosing. What arrives
    /// with it is a position into the list this window handed over, which is a promise about two
    /// arrays staying aligned that no signature can carry — so it is checked at the seam, on both
    /// sides, where the answer is one line and the alternative is an exception on the UI thread.
    /// </remarks>
    private void OnMicrophoneChosen(object? sender, int chosen)
    {
        if (chosen < 0 || chosen >= _microphones.Length)
        {
            return;
        }

        _chosen = _chosen with { Microphone = _microphones[chosen] };
        Refresh();
    }

    /// <remarks>Guarded the way the microphone's is, for the reason written there.</remarks>
    private void OnSourceChosen(object? sender, int chosen)
    {
        if (chosen < 0 || chosen >= _sources.Length)
        {
            return;
        }

        _chosen = _chosen with { Source = _sources[chosen] };
        Refresh();
    }

    private void OnSpokenChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || SpokenPicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Spoken = Spoken[SpokenPicker.SelectedIndex].Tag };
        Refresh();
    }

    /// <summary>
    /// Everything this machine offers, asked again. A choice it no longer offers is dropped rather
    /// than carried into a recording that would open a device that has gone or follow a process id
    /// nothing owns.
    /// </summary>
    /// <remarks>
    /// Both pickers and not only the programs, which is what it used to be. The microphones keep up
    /// on their own now, and a press that refreshed one of the two would be a control that looks
    /// broken beside the one it does not touch — but the reason it asks about them is the session
    /// where Windows refused to say when devices change at all. There the machine answers perfectly
    /// well and nothing ever asks it again, and one press turns that from a dead session into a
    /// degraded one. Programs have no notification of their own: nothing tells an application that
    /// a meeting was just started in a browser tab.
    /// </remarks>
    private void OnRefreshTheMachine(object sender, RoutedEventArgs e)
    {
        LookAtTheMicrophonesAgain();

        _sources = SourcesNow();
        _chosen = _chosen.AsTheSourcesAreNow(_sources);

        FillThePickers();
        Refresh();
    }

    /// <summary>
    /// Windows says this machine's devices are not what they were, so the pickers are asked again.
    /// </summary>
    /// <remarks>
    /// It arrives on whatever thread the audio service uses, so the whole of the work is put on
    /// the dispatcher: what follows touches every control on this screen. On the queue taken in
    /// the constructor and not on <c>this.DispatcherQueue</c>, which is a property of a XAML object
    /// and so is itself a call across the apartment this handler is already on the wrong side of.
    /// <c>TryEnqueue</c> and not a throw, because it answers false exactly when the window is going
    /// away, which is a notification arriving during a close and not a fault.
    /// </remarks>
    private void OnTheDevicesChanged(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _lookQueued, 1) == 0)
        {
            _drawnOn.TryEnqueue(LookAtTheDevicesAgain);
        }
    }

    /// <summary>
    /// The machine asked again, on the thread the screen is drawn on. Everything Windows said since
    /// this was queued is one answer, however many ways it said it.
    /// </summary>
    /// <remarks>
    /// The microphones are asked for only while nothing is being recorded, and that is not an
    /// optimisation. <see cref="DeviceEnquiry"/> remembers a question given up on against the
    /// question and not against whoever asked it, and while a meeting runs the one asking this one
    /// is the capture session, every two seconds, following channel 1 onto whatever replaced a
    /// device that went. A screen asking the same thing on the same event — the unplug is what
    /// fires both — could wedge first and leave the recovery refused for the rest of the meeting,
    /// which is the coupling that file's scoping exists to prevent. Nothing is lost by waiting:
    /// what the next meeting records is chosen when this screen is choosing, and stopping asks
    /// again.
    /// <para>
    /// What the machine plays through is this screen's own question and nobody else's, so it is
    /// asked whenever there is a meeting for the answer to be a warning about.
    /// </para>
    /// </remarks>
    private void LookAtTheDevicesAgain()
    {
        Interlocked.Exchange(ref _lookQueued, 0);

        if (_closed)
        {
            return;
        }

        if (Screen().State == RecorderState.Choosing)
        {
            LookAtTheMicrophonesAgain();
            FillThePickers();
            Refresh();
            return;
        }

        if (_recording is not null)
        {
            ReadWhatTheMachinePlaysThrough();
            ShowTheMeters(Screen().State);
        }
    }

    /// <summary>
    /// What this machine offers to record from now, and what that leaves chosen. Called only where
    /// nothing else is asking that question — see <see cref="LookAtTheDevicesAgain"/>.
    /// </summary>
    /// <remarks>
    /// It writes the fields and leaves the controls alone, because every caller has other fields to
    /// write first and would otherwise draw the screen twice.
    /// </remarks>
    private void LookAtTheMicrophonesAgain()
    {
        var microphones = Ask(AudioDevices.Microphones, UiTexts.WindowsDidNotSayWhatMicrophonesThereAre);

        // A machine that would not say leaves the list where it was. An empty picker written from
        // a refusal is the failure ISC-163 is about wearing a different hat: it reads as a machine
        // with no microphone, and the recording it stops is one that would have run.
        if (microphones is null)
        {
            return;
        }

        _microphones = [.. microphones];

        var chosen = _chosen.AsTheMicrophonesAreNow(_microphones);
        var gone = _chosen.Microphone is not null && chosen.Microphone is null;
        _chosen = chosen;

        if (gone)
        {
            Say(UiTexts.TheMicrophoneChosenIsNoLongerThere);
        }
    }

    /// <summary>
    /// The meetings took the whole window, or gave it back. The recorder above goes with it and
    /// the strip comes the other way: the list is the same screen rather than another one, and a
    /// card left showing over a full-height list would be the two of them arguing about the room.
    /// </summary>
    /// <remarks>
    /// The whole screen and not the arrangement alone, which is the one thing this handler must
    /// not shortcut. What the strip says is written by the refresh, so a press that rearranged the
    /// window without one would raise the list over a meeting and travel in a strip with no state
    /// word, no clock and no microphone on it, until the next tick a second later — the failure
    /// ISC-172 names, at the moment of the gesture that is supposed to answer it.
    /// </remarks>
    private void OnMeetingsMoved(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Which of the two the room below is showing, and which of the recorder half and the strip is
    /// above it — the last step of a refresh and never a step on its own, so what a half says is
    /// always written before it moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A meeting being read takes the window on exactly the terms the raised list does, and both
    /// are one fact to <see cref="RecorderScreen"/>: the recorder half is on screen when neither
    /// has it. Nothing is refused either way, and what makes that safe is the strip — it carries
    /// the clock, what is being recorded and the press that stops it, so what a hidden recorder
    /// half would have taken off the automation tree is on screen instead of it rather than gone.
    /// </para>
    /// <para>
    /// The two travel as one gesture. The half leaves, the strip arrives, and the room below takes
    /// what is given up, which is ISC-173.2: the drawer rising is what says this is the same screen
    /// and not another one. `docs/design.md` §Movement gives the drawer 300 ms, decelerating in and
    /// accelerating out — and none of it on a machine that asked Windows for no animation, where
    /// each is simply already where it was going.
    /// </para>
    /// </remarks>
    private void ShowWhatTheRoomIsShowing(RecorderScreen screen)
    {
        var reading = Reading.IsShowingAMeeting;

        Reading.Visibility = reading ? Visibility.Visible : Visibility.Collapsed;
        Meetings.Visibility = reading ? Visibility.Collapsed : Visibility.Visible;

        // Where each is heading and not where it is: something on its way out is still visible for
        // the whole of the move, so reading its visibility here would drop the press that reversed
        // it.
        //
        // Written out twice rather than through a helper taking the element, and that is not an
        // oversight. What travels off this screen is what a live region must never be inside, and
        // LiveRegionTests holds that by reading these calls — so the name of each half has to be
        // at the call rather than inside a local function, where the only name the source carries
        // is the parameter's. That is the same trade `Tell` already makes for the same check.
        if (screen.TheRecorderIsOnScreen != ScreenMotion.IsShowing(RecordingCard))
        {
            ScreenMotion.ArriveOrLeave(RecordingCard, screen.TheRecorderIsOnScreen, Move.Travelling);
        }

        if (screen.TheStripIsOnScreen != ScreenMotion.IsShowing(TheStrip))
        {
            ScreenMotion.ArriveOrLeave(TheStrip, screen.TheStripIsOnScreen, Move.Travelling);
        }
    }

    /// <summary>
    /// Somebody opened one of the meetings on the list.
    /// </summary>
    /// <remarks>
    /// The meeting is shown before the room is rearranged, because showing one is what makes
    /// <see cref="ReadingAMeeting.IsShowingAMeeting"/> true and that is what <see cref="Screen"/>
    /// reads. The other way round leaves a window that hid the list to show an empty screen.
    /// <para>
    /// A refresh and not the arrangement alone, for the reason <see cref="OnMeetingsMoved"/> gives:
    /// a meeting opened while one is being recorded takes the recorder half away and puts the strip
    /// up, and what the strip says has to be written before it moves.
    /// </para>
    /// </remarks>
    private void OnMeetingChosen(object? sender, Guid meeting)
    {
        Reading.Show(meeting);
        Refresh();
    }

    /// <summary>
    /// Somebody came back from a meeting. The list is read again rather than shown as it was: what
    /// they did on that screen — named it, bought a transcription for it, ignored one — is exactly
    /// what its row on the list says.
    /// </summary>
    private void OnLeftTheMeeting(object? sender, EventArgs e)
    {
        Meetings.Read();
        Refresh();
    }

    private void OnWhoIsUsingThisTyped(object sender, TextChangedEventArgs e) => ShowWhoIsUsingThis();

    /// <summary>
    /// Keeps who is using this application, which is the same press whether it is the first answer
    /// or a correction of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off this thread, and for the reason starting a recording is: the corpus may have every
    /// migration to run before the first row, which on a machine that has never recorded is the
    /// whole schema. It is the same two lines the recorder uses — make the folder, open migrated —
    /// so an install where this is answered before anything is recorded lays the corpus out the
    /// same way the first recording would have.
    /// </para>
    /// <para>
    /// A blank answer is not one, and it is refused by the press being dead rather than by a
    /// sentence: there is nothing to say about it that the empty field does not already say.
    /// </para>
    /// <para>
    /// The row is dead for as long as the press is in flight, and that is not tidiness. Disabling
    /// the button alone left the field live, and a keystroke into it drew the row again and armed
    /// the button back — so a second press ran beside the first, both found that nobody had
    /// answered, and both wrote a person who had. The recorder's own presses are held the same
    /// way and by the same kind of state, which is why this one is in
    /// <see cref="WhoIsUsingThisRow"/> rather than beside the handler.
    /// </para>
    /// </remarks>
    private async void OnKeepWhoIsUsingThis(object sender, RoutedEventArgs e)
    {
        // Asked again inside the handler, because a click already in flight arrives after the row
        // was drawn dead.
        if (!WhoIsUsingThis().MayBeKept || _corpus.Folder is not { } folder)
        {
            return;
        }

        var name = WhoIsUsingThis().Name;
        _whoIsUsingThis = _whoIsUsingThis with { BeingKept = true };
        ShowWhoIsUsingThis();

        try
        {
            await Task.Run(() =>
            {
                folder.Create();
                using var context = CorpusDatabase.OpenMigrated(folder);
                new HumanLayer(context, TimeProvider.System).ThisIsMe(name);
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                Say(UiTexts.WhoIsUsingThisWasNotKept);
                Dump(refused.Message);
            }

            return;
        }
        finally
        {
            _whoIsUsingThis = _whoIsUsingThis with { BeingKept = false };

            if (!_closed)
            {
                ShowWhoIsUsingThis();
            }
        }

        if (_closed)
        {
            return;
        }

        // What the write said, rather than the corpus asked again. The write either threw or put
        // this name on the one row that carries the flag, so a read back could only disagree by
        // failing — and a "could not be read" printed under a "done" is a screen contradicting
        // itself about an act that worked.
        _whoIsUsingThis = _whoIsUsingThis with { SomebodyHasSaid = true };
        WhoIsUsingThisBox.Text = name;

        // The corpus may not have existed a moment ago, and the line under this row would still be
        // saying so.
        SayWhereTheCorpusIs();
        Say(UiTexts.WhoIsUsingThisIsKept, name);
    }

    private void OnLanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || LanguagePicker.SelectedIndex < 0)
        {
            return;
        }

        var chosen = Languages[LanguagePicker.SelectedIndex];

        // Selecting what is already selected is not somebody choosing. The picker is set from the
        // language the window opened in, and taking that for a choice would record one on every
        // launch — after which the application would never follow Windows again.
        if (chosen == _language)
        {
            return;
        }

        LanguageChosen?.Invoke(this, chosen);
    }

    private void OnOpenPackagingChecks(object sender, RoutedEventArgs e) =>
        PackagingChecksAsked?.Invoke(this, EventArgs.Empty);

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (!Screen().Allows(RecorderPress.Start) || _corpus.Folder is not { } folder)
        {
            return;
        }

        // A program is a process id, and a process id outlives the program that had it. Between
        // the list being read and record being pressed the chosen program can have exited and
        // Windows can have handed its number to something else, which would put another
        // application's audio on channel 0 with nothing on screen looking wrong. So the answer is
        // taken again here rather than trusted, through the same rule that drops a choice anywhere
        // else — a machine that would not say answers with the whole machine and nothing more,
        // which reads as the program having gone, and that is the answer to give when this is what
        // stands between somebody and a recording of the wrong process.
        if (_chosen.Source is { IsTheWholeMachine: false })
        {
            _sources = SourcesNow();

            if (_chosen.AsTheSourcesAreNow(_sources) is var still && still != _chosen)
            {
                _chosen = still;
                FillThePickers();
                Say(UiTexts.ThatProgramIsNoLongerRunning);
                Refresh();
                return;
            }
        }

        var chosen = _chosen;

        _report.Clear();
        _offered = false;
        _taken = false;

        // The last meeting's, and this one has not read anything yet. Left standing, they would be
        // the previous meeting's devices and levels under the new one for as long as the first tick
        // takes to arrive.
        _channels = [];

        // And the one thing on this screen that outlives a tick. A meter's retained peak does not
        // decay — that is what makes it a memory rather than a reading — so the mark left standing
        // is the loudest moment of the meeting before this one, sitting on a bar that has heard
        // nothing yet. Emptying the readings above does not reach it, because nothing about it
        // comes off a reading.
        TheOthers.ForgetTheLoudestMoment();
        Mine.ForgetTheLoudestMoment();
        _step = RecorderStep.Starting;
        Refresh();

        try
        {
            // Off this thread: two devices are opened, each with a deadline for one that never
            // answers, and the corpus may have a migration to run before the first row.
            var started = await Task.Run(() =>
            {
                folder.Create();
                var context = CorpusDatabase.OpenMigrated(folder);

                try
                {
                    return (Context: context, Recording: MeetingRecording.Start(
                        context,
                        chosen.Spoken!,
                        chosen.Microphone!,
                        chosen.Source!.Follow,
                        Now()));
                }
                catch
                {
                    context.Dispose();
                    throw;
                }
            });

            // The window can have been closed while the devices were opening. Publishing into it
            // would leave two devices recording with nothing left that could stop them, so what
            // this handler made this handler lets go of.
            if (_closed)
            {
                started.Recording.Dispose();
                started.Context.Dispose();
                return;
            }

            _context = started.Context;
            _recording = started.Recording;
            _watch.Start();

            // The other press that makes a corpus where there was none, said again for the same
            // reason keeping an answer says it: the line under the recorder would otherwise go on
            // offering to make one while a meeting records into the one it just made.
            SayWhereTheCorpusIs();

            // Once here, so the meters and the line about the room are up with the meeting rather
            // than a second into it. The meters are the tick's from here on; what the machine plays
            // through is nobody's until Windows says it moved.
            ReadTheDevices();
            ReadWhatTheMachinePlaysThrough();
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            Say(UiTexts.TheRecordingCouldNotStart);
            Dump(refused.Message);
        }
        finally
        {
            _step = RecorderStep.Nothing;

            if (!_closed)
            {
                Refresh();
            }
        }
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.Pause))
        {
            return;
        }

        recording.Pause();
        Refresh();
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.Resume))
        {
            return;
        }

        recording.Resume();
        Refresh();
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || _context is not { } context || !Screen().Allows(RecorderPress.Stop))
        {
            return;
        }

        _watch.Stop();
        _step = RecorderStep.Finishing;

        // The meeting is in the corpus from the moment record was pressed, so the list below can
        // show it while it is being saved rather than after. Told which one first, because the row
        // it reads says a meeting with no audio and this is the one thing that knows better.
        Meetings.BeingSavedNow(recording.MeetingId);
        Meetings.Read();

        Refresh();

        try
        {
            // Off this thread, and this is the one that matters: the spools are poured onto a
            // timeline, the file is read back and hashed, and for a long meeting that is minutes.
            // The devices are already let go of before any of it, so nothing is being recorded
            // while it runs.
            //
            // What the save is doing comes back through the report below, one step at a time, on
            // the thread the screen is drawn on — which is the same rule every other answer
            // arriving from off this thread follows. Not a `Progress<T>`: that would put the
            // report on whichever context happened to be current when it was made and then this
            // window would move it again, which is two queues for one answer and an order between
            // them nothing states.
            var told = new Watching(_drawnOn, ShowTheSave);

            var finished = await Task.Run(() =>
            {
                try
                {
                    return recording.Stop(Now(), told);
                }
                finally
                {
                    context.Dispose();
                }
            });

            Say(
                UiTexts.TheMeetingIsRecorded,
                finished.MeetingId,
                ScreenNumbers.Long(finished.Length),
                finished.Audio.RelativePath);

            // Said out loud, every time, because it is the promise and not an omission.
            Say(UiTexts.NothingWasQueued);
        }
        catch (Exception broke) when (ScreenFailures.Reportable(broke))
        {
            // The recording is over either way — what is on disk is a spool recovery already knows
            // how to offer — so this says so and does not throw a meeting away over it.
            //
            // Let go of here and not only inside stopping: stopping releases the devices itself,
            // but it can throw before it gets that far, and then the meeting has failed with two
            // devices still open. Letting go of one that did stop does nothing, so this is the
            // same guarantee the command line gets from holding the recording in a `using`.
            recording.Dispose();
            Say(UiTexts.TheMeetingCouldNotBeMade);
            Dump(broke.Message);
        }
        finally
        {
            _context = null;
            _recording = null;
            _step = RecorderStep.Nothing;

            // The save is over however it went, so nothing is being saved and no row below is.
            // Neither of these draws anything: what redraws is the refresh below, which happens
            // only while the window is open.
            _saving = null;
            Meetings.BeingSavedNow(null);

            // Every meeting is asked its own questions again. What channel 0 followed is a process
            // id that has just outlived its meeting, and what was spoken in the last one is not an
            // answer about the next one — `MeetingRecordings.Open` asks rather than guessing for
            // exactly that reason. The microphone is a device and stays chosen.
            _chosen = _chosen with { Source = null, Spoken = null };
            _sources = SourcesNow();

            if (!_closed)
            {
                // The one moment the microphones are asked about outside a device change: nothing
                // asked while the meeting ran, because the capture session was asking the same
                // question of the same machine, so anything that arrived or went in the last hour
                // is learned here.
                LookAtTheMicrophonesAgain();
                FillThePickers();
                Refresh();

                // The one thing that puts a row in the list below without anybody pressing
                // anything. Read here rather than left to the next press, because the meeting that
                // has just been made is the one somebody is about to decide about.
                Meetings.Read();
            }
        }
    }

    /// <summary>
    /// Takes the whole machine's audio. Nothing gets here without the offer having been on screen,
    /// because until then there is no button.
    /// </summary>
    /// <remarks>
    /// It is marked taken before the move rather than after, which is the opposite of what the
    /// prompt does and right for the same reason: at a prompt the question is whether a key was
    /// pressed after the words appeared, and here it is whether a second press can arrive while
    /// the first is still opening a device. A move that comes back refused puts it back.
    /// </remarks>
    private async void OnRecordTheWholeMachine(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.RecordTheWholeMachine))
        {
            return;
        }

        _taken = true;
        Refresh();

        try
        {
            // Off this thread like the other two: it opens one device, stops another and lets a
            // third go. Unlike the other two, the meeting is being recorded the whole time.
            await Task.Run(recording.RecordTheWholeMachine);
            Say(UiTexts.NowRecordingTheWholeMachine);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            // Reported and not thrown: the meeting is still being recorded either way, and the
            // channel is where it was, so the offer is there to take again. Wider than the audio
            // engine's own refusal because the move writes what it did into the recording's own
            // folder, and a folder refuses a write for reasons that have nothing to do with a
            // device.
            _taken = false;
            Say(UiTexts.TheWholeMachineCouldNotBeRecorded);
            Dump(refused.Message);
        }
        finally
        {
            // Guarded the way the other two handlers guard theirs. This one writes every control on
            // the screen, the meters included, and the window it is writing to can have been closed
            // while three devices were being dealt with.
            if (!_closed)
            {
                Refresh();
            }
        }
    }

    /// <summary>
    /// The second. It reads what each channel is hearing onto the screen, and asks the recording
    /// whether the program channel 0 is following has brought back nothing at all.
    /// </summary>
    /// <remarks>
    /// The offer is never asked for while the meeting is paused. A paused recording hears nothing
    /// from anything, so the rule it rests on would be true of a program that is playing perfectly
    /// well — and an offer, once made, stays made. The meters are read either way: what a paused
    /// meeting is recording is silence, and showing that is how somebody sees the pause took.
    /// </remarks>
    private void OnWatch(object? sender, object e)
    {
        if (_recording is not { } recording || _step != RecorderStep.Nothing)
        {
            return;
        }

        ReadTheDevices();

        // The whole screen only when the offer has just appeared, and the clock, the strip and the
        // meters otherwise — the three things a second changes, and the strip because the length
        // on it is the same clock. What the rest of it says does not change with a second passing
        // — the buttons, the pickers and the status line all answer to a press — so redrawing them
        // once a second would be a second's worth of work to say what it already said, and it
        // would take a selection out of the report every time it ran.
        if (!_offered && !recording.IsPaused && recording.HeardNothingFromTheProgram())
        {
            _offered = true;
            Refresh();
            return;
        }

        var screen = Screen();
        var clock = RecordingClock.Of(screen.State, _recording?.Card.StartedAt, Now());

        ShowTheClock(clock);
        ShowTheStrip(screen, clock);
        ShowTheMeters(screen.State);
    }

    /// <summary>
    /// Closing the window with a meeting running lets the devices go and finishes nothing. What
    /// stays on disk is a recording somebody decides about later, which is exactly what closing
    /// the application in the middle of a meeting has always left.
    /// </summary>
    /// <remarks>
    /// It lets go of nothing while a press is still running, and that is the whole of it: the
    /// handler that started one owns what it made until it comes back, and disposing a corpus
    /// context underneath a meeting being written into it, or a capture session underneath the
    /// code stopping it, is how a meeting somebody recorded becomes a half-written file. Closing
    /// during either leaves what that handler is holding to that handler — and if the process goes
    /// before it comes back, what is on disk is the spool recovery already offers, which is the
    /// same answer as the power being pulled out.
    /// </remarks>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _watch.Stop();

        // The list below has a press of its own that outlives the handler that started it —
        // keeping a recording is the same minutes of work stopping is — and it is told for the
        // same reason this window keeps `_closed`: what it must not do afterwards is draw.
        Meetings.Closing();

        // The meeting screen holds a file and an audio endpoint for as long as somebody is
        // listening, and a window that closed over one would leave both to whenever a finaliser
        // got round to them — with the recording still coming out of the machine until it did.
        Reading.Close();

        // Before the guard below, and it is the one thing here that is: what it stops is Windows
        // calling into a closed window about a device, which has nothing to do with whichever
        // press is still holding a recording.
        _devices?.Dispose();

        if (_step != RecorderStep.Nothing)
        {
            return;
        }

        _recording?.Dispose();
        _context?.Dispose();
        _recording = null;
        _context = null;
    }

    /// <summary>
    /// Now, off the machine's own clock. The command line reads it through a clock of its own for
    /// the same reason this does not take one: an instant somebody could hand in would let a paid
    /// artifact be dated to whenever they liked.
    /// </summary>
    private static UtcTimestamp Now() => UtcTimestamp.From(TimeProvider.System.GetUtcNow());

    private void Say(UiText text, params object?[] values)
    {
        _report.Add(TextLine.Says(text, values));
        Render();
    }

    /// <summary>
    /// A line that is not a sentence this application chose: a path, a device's own name, or what
    /// the machine said when something failed.
    /// </summary>
    /// <remarks>
    /// The first two really are data and read the same in every language. The third is not, and
    /// saying so is the point of this comment: a message off an exception is a
    /// <c>COMException</c>'s English, or the filesystem's, or SQLite's, and it is printed here
    /// anyway because it is the evidence — somebody quotes it, or searches for it, and a
    /// translation of it would match nothing. So it goes in the report, under a sentence from the
    /// catalogue that already said what happened, and never on a line of its own where it would
    /// read as the application talking. Beside a meter, while a meeting is still running, it is
    /// refused outright: <c>ChannelReading.Stopped</c> says why.
    /// </remarks>
    private void Dump(string line)
    {
        _report.Add(TextLine.Data(line));
        Render();
    }

    private void Status(UiText text, params object?[] values)
    {
        _status = TextLine.Says(text, values);
        Render();
    }

    private void Render()
    {
        OutputText.Text = string.Join(Environment.NewLine, _report.Select(line => line.In(_language)));
        StatusText.Text = _status?.In(_language) ?? string.Empty;
    }

    /// <summary>
    /// Somebody watching a save from the thread the screen is drawn on, whichever thread the save
    /// is running on.
    /// </summary>
    /// <remarks>
    /// Nine lines rather than a <see cref="Progress{T}"/>, and the difference is which queue the
    /// report lands in. <c>Progress</c> captures whatever synchronisation context happened to be
    /// current where it was made, which is a fact about the call site rather than about this
    /// window; this names the queue outright, so a report raised from a pool thread and one raised
    /// from the screen's own thread arrive the same way and in the order they were raised.
    /// </remarks>
    private sealed class Watching(DispatcherQueue drawnOn, Action<SavingWork> show)
        : IProgress<SavingWork>
    {
        public void Report(SavingWork value) => drawnOn.TryEnqueue(() => show(value));
    }
}
