using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

// WinUI has a Duration of its own — an animation's, in ticks — and this file is the one place in
// the application where the domain's meets it. Aliased rather than qualified at the use, so that
// the day a second use appears it cannot quietly be the other one.
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
/// It holds no rule of its own about what can be pressed — including whether the list may take the
/// window. <see cref="RecorderScreen"/> answers all of it, in a project a build agent can run, and
/// every handler here asks it before doing anything — including the handlers of controls it has
/// just disabled, because a click already in flight arrives after that.
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

    /// <summary>Where this application's corpus is, or what stopped it being found.</summary>
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

        Meetings.Open(corpus);
        Meetings.OpennessChanged += OnMeetingsMoved;

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
        Render();
        Refresh();

        // The drawer is half of this screen and not a window of its own, so it is read in the same
        // pass rather than told separately by whatever decides the language.
        Meetings.ReadIn(language);
        ShowWhereTheDrawerIs();
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

        // What the next meeting records cannot be changed once one is running: the devices are
        // open, and the engine has no way to swap one out under a recording.
        var choosing = screen.State == RecorderState.Choosing;
        MicrophonePicker.IsEnabled = choosing;
        SourcePicker.IsEnabled = choosing;
        SpokenPicker.IsEnabled = choosing;
        RefreshTheMachineButton.IsEnabled = choosing;

        // The list below may hide this half of the screen only while there is nothing here that
        // has to be seen. It is RecorderScreen's answer and not this window's, for the same reason
        // every button above is.
        Meetings.OfferTheWholeWindow(screen.TheMeetingsMayTakeTheWholeWindow);

        ShowTheMeters(screen.State);
        Announce(screen.State);
    }

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

        // The panel before the lines inside it, because nothing inside a Collapsed element is in
        // the automation tree — the rest of that chain, and why each step of it matters, is Tell's.
        Meters.Visibility = meters.Showing ? Visibility.Visible : Visibility.Collapsed;

        var others = meters.On(AudioChannel.Loopback);
        var mine = meters.On(AudioChannel.Microphone);

        Show(others, OthersCapturing, OthersMeter, OthersLevel);
        Show(mine, MineCapturing, MineMeter, MineLevel);

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

    /// <summary>What one channel's meter reads as: what it is capturing, and how loud.</summary>
    /// <remarks>
    /// Taking the controls rather than being written twice, because the two rows are now one rule
    /// with nothing to tell them apart, and a second copy of it is a second chance to set the wrong
    /// one. What the two rows really do differ in — which sentence they say when the device is
    /// gone — is said where the line is, in <see cref="ShowTheMeters"/>.
    /// </remarks>
    private void Show(ChannelReading? reading, TextBlock capturing, ProgressBar meter, TextBlock level)
    {
        // Cleared and not left standing. The row belongs to a meeting that is over, and the panel
        // around it is hidden — but the next meeting's first frame is drawn from these controls,
        // and the last one's microphone is not something to show for a second under a new one.
        if (reading is null)
        {
            capturing.Text = string.Empty;
            meter.Value = 0;
            level.Text = string.Empty;
            return;
        }

        // The catalogue where the reading hands back no name, which is a channel capturing the
        // whole machine — the same shape as the level below it, and for the same reason.
        capturing.Text = Capturing(reading.Capturing);
        meter.Value = reading.Meter;

        // A level is a measurement and reads the same in every language, so the reading hands one
        // back as data. Having measured nothing is a sentence and the reading hands back none, so
        // the word for it comes from the catalogue — which is also what this screen exists to show:
        // an empty bar and a bar nothing has drawn yet look the same.
        level.Text = reading.Loudness ?? In(UiTexts.NothingIsArriving);
    }

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
            null => _corpus.HoldsACorpus
                ? UiTexts.MeetingsAreKeptAt
                : UiTexts.TheFirstRecordingMakesTheCorpusAt,
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

    private void FillThePickers()
    {
        _filling = true;
        try
        {
            // A device's name is what its maker called it, so it is data and has no language.
            MicrophonePicker.ItemsSource = _microphones.Select(device => device.ToString()).ToArray();
            MicrophonePicker.SelectedIndex = _chosen.Microphone is null
                ? -1
                : Array.FindIndex(_microphones, device =>
                    device.Id.Equals(_chosen.Microphone.Id, StringComparison.OrdinalIgnoreCase));

            SourcePicker.ItemsSource = _sources.Select(NameOf).ToArray();
            SourcePicker.SelectedIndex = _chosen.Source is null ? -1 : Array.IndexOf(_sources, _chosen.Source);

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

    private void OnMicrophoneChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || MicrophonePicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Microphone = _microphones[MicrophonePicker.SelectedIndex] };
        Refresh();
    }

    private void OnSourceChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || SourcePicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Source = _sources[SourcePicker.SelectedIndex] };
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
    /// The meetings took the whole window, or gave it back. The recorder above goes with it: the
    /// list is the same screen rather than another one, and a card left showing over a full-height
    /// list would be the two of them arguing about the room.
    /// </summary>
    private void OnMeetingsMoved(object? sender, EventArgs e) => ShowWhereTheDrawerIs();

    /// <summary>
    /// The recorder half, shown or not according to where the drawer is. One writer and one
    /// reading, called from the constructor, from a language change and from the drawer saying it
    /// moved — so which position the screen is in is the drawer's answer everywhere rather than two
    /// defaults in two files that happen to agree.
    /// </summary>
    private void ShowWhereTheDrawerIs() =>
        RecordingCard.Visibility = Meetings.HasTheWholeWindow ? Visibility.Collapsed : Visibility.Visible;

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

            // Once here, so the meters and the line about the room are up with the meeting rather
            // than a second into it. The meters are the tick's from here on; what the machine plays
            // through is nobody's until Windows says it moved.
            ReadTheDevices();
            ReadWhatTheMachinePlaysThrough();
        }
        catch (Exception refused) when (Reportable(refused))
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

    /// <summary>
    /// Whether this is something to say on screen rather than something to end the application
    /// over. Every one of them is a fault in what the machine handed back: a device, a folder, or
    /// the corpus file itself.
    /// </summary>
    /// <remarks>
    /// The list is closed on purpose, and these handlers are <c>async void</c>, which is what makes
    /// it worth being exact about: anything not named here reaches the dispatcher and takes the
    /// application down in the middle of a meeting. What is named is what the layers underneath
    /// actually throw — the audio engine's refusal, the recording's own, the filesystem's two, and
    /// SQLite's, which arrives from a corpus that is locked, unwritable or not a database and which
    /// the command line already answers with a refusal rather than with a stack trace.
    /// </remarks>
    private static bool Reportable(Exception thrown) => thrown
        is AudioCaptureException
        or RecordingException
        or IOException
        or UnauthorizedAccessException
        or SqliteException
        or DbUpdateException;

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
        Refresh();

        try
        {
            // Off this thread, and this is the one that matters: the spools are poured onto a
            // timeline, the file is read back and hashed, and for a long meeting that is minutes.
            // The devices are already let go of before any of it, so nothing is being recorded
            // while it runs.
            var finished = await Task.Run(() =>
            {
                try
                {
                    return recording.Stop(Now());
                }
                finally
                {
                    context.Dispose();
                }
            });

            Say(
                UiTexts.TheMeetingIsRecorded,
                finished.MeetingId,
                Length(finished.Length),
                finished.Audio.RelativePath);

            // Said out loud, every time, because it is the promise and not an omission.
            Say(UiTexts.NothingWasQueued);
        }
        catch (Exception broke) when (Reportable(broke))
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
        catch (Exception refused) when (Reportable(refused))
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

        // The whole screen only when the offer has just appeared, and the meters otherwise. What
        // the rest of it says does not change with a second passing — the buttons, the pickers and
        // the status line all answer to a press — so redrawing them once a second would be a
        // second's worth of work to say what it already said, and it would take a selection out of
        // the report every time it ran.
        if (!_offered && !recording.IsPaused && recording.HeardNothingFromTheProgram())
        {
            _offered = true;
            Refresh();
            return;
        }

        ShowTheMeters(Screen().State);
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

    /// <summary>How long the meeting was, as somebody reads a length off a screen.</summary>
    private static string Length(Duration length) =>
        length.ToTimeSpan().ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

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
}
