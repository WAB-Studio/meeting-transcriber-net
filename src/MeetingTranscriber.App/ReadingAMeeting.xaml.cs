using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

using Windows.System;

// WinUI has a Duration of its own — an animation's, in ticks — and both meanings are in scope
// here. Aliased rather than qualified at the use, for the reason `MainWindow` gives.
using Duration = MeetingTranscriber.Domain.Time.Duration;

namespace MeetingTranscriber.App;

/// <summary>
/// The screen one meeting is read from: what the AI left of it, who wrote that, what it would cost
/// to get the rest — and, along the bottom, the recording itself.
/// </summary>
/// <remarks>
/// <para>
/// There is no screen here for reading the transcript, and that is deliberate rather than missing:
/// nobody opens an application to read a hundred and forty-eight turns. What somebody comes back
/// for is what was decided, what is left to do and what was left unresolved — so those are the
/// screen, each thing carrying the minute it was said at, and the transcript is what one of those
/// opens, in place and without changing screen.
/// </para>
/// <para>
/// It decides nothing about the meeting. Every question it asks — whether the player is there,
/// what act is offered, whether the name may be typed, where the marks along the track go — is
/// <see cref="MeetingScreen"/>'s, in a project a build agent can run, and this control's whole job
/// is turning those answers into controls and the presses back into calls.
/// </para>
/// <para>
/// It opens no corpus it does not let go of, for the reason <see cref="MeetingsDrawer"/> gives: a
/// meeting whose transcription landed while somebody was looking at it is exactly the case a
/// remembered answer gets wrong. What it does hold open is the recording, because a player is a
/// file and an endpoint held for as long as somebody is listening — and that is why
/// <see cref="Close"/> exists and why every path off this screen goes through it.
/// </para>
/// <para>
/// A control and not a window. This is the same screen the meetings are on, with the meetings
/// out of the way, which is what makes going back a press rather than finding a window again —
/// and it is what lets the recorder above stay exactly where the drawer leaves it.
/// </para>
/// </remarks>
public sealed partial class ReadingAMeeting : UserControl
{
    /// <summary>
    /// How often the player's position is read while it is playing.
    /// </summary>
    /// <remarks>
    /// Often enough that the number does not visibly jump, and no more: it is four reads a second
    /// off a stream that is already being read by the endpoint, and a screen redrawing a slider
    /// sixty times a second would be spending a frame budget on a digit that changes once.
    /// </remarks>
    private static readonly TimeSpan HowOftenTheTrackIsRead = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How wide one citation's mark on the track is, in the units the canvas holding them lays out
    /// in. Here rather than in the style beside the mark's colour, because the run the marks are
    /// spread over is the track less one of them and two copies of that number would drift.
    /// </summary>
    private const double HowWideAMarkIs = 2;

    private readonly DispatcherTimer _watch = new() { Interval = HowOftenTheTrackIsRead };

    /// <summary>
    /// Where the meetings are, handed over once by the window that holds this. Not a constructor
    /// parameter because the XAML above declares this control, and what XAML constructs takes no
    /// arguments — so <see cref="Open"/> is the seam instead, and it is called exactly once.
    /// </summary>
    private CorpusFolder? _corpus;

    private UiLanguage _language;

    /// <summary>The meeting on screen, or none when this control is not showing one.</summary>
    private Guid? _meeting;

    /// <summary>
    /// What was read the last time this screen drew, kept only so the presses have something to
    /// answer about without reading the corpus again inside a handler.
    /// </summary>
    private MeetingAsRead? _read;

    /// <summary>
    /// The recording being played, held open for as long as this screen is showing the meeting it
    /// belongs to. Null when there is no audio, or when the machine would not play it.
    /// </summary>
    private Playback? _playing;

    /// <summary>
    /// True while this screen is writing the track's own value, so that the handler telling a drag
    /// from a redraw has something to tell them apart by. Without it, every tick would read as
    /// somebody having moved the slider and seek the player to where it already was.
    /// </summary>
    private bool _movingTheTrack;

    /// <summary>What the name field held when the meeting was drawn, so a leave that changed
    /// nothing writes nothing.</summary>
    private string _nameAsRead = string.Empty;

    private TextLine? _status;

    public ReadingAMeeting()
    {
        InitializeComponent();
        _watch.Tick += OnWatch;
    }

    /// <summary>Somebody asked to go back to the meetings.</summary>
    public event EventHandler? Left;

    /// <summary>Whether this screen is showing a meeting.</summary>
    public bool IsShowingAMeeting => _meeting is not null;

    /// <summary>
    /// Hands over the corpus the meetings are in. Reads nothing: nothing is shown until a meeting
    /// is chosen.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    public void Open(CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The meeting screen already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>Which language this screen is being read in.</summary>
    /// <remarks>
    /// It reads again and it does not touch the player. What language a screen is read in says
    /// nothing about the recording under it, and somebody who changed it half way through a
    /// meeting would otherwise have the audio stop and go back to the start.
    /// </remarks>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        if (_meeting is not null)
        {
            Draw(theRecordingToo: false);
        }
    }

    /// <summary>
    /// Opens one meeting: reads it, and opens the recording under it.
    /// </summary>
    /// <remarks>
    /// The one entry that touches the player, and every other path through this screen reads
    /// again without it. Coming back to a meeting after buying a transcription shows the
    /// transcription because the corpus is read on every draw; the recording is opened here
    /// because this is the only moment it can have become a different file.
    /// </remarks>
    public void Show(Guid meetingId)
    {
        StopPlaying();

        _meeting = meetingId;

        // With the name, and that is not tidiness. It is what the field held for the meeting that
        // was on screen a moment ago, and a read that then refuses would leave it standing over
        // this meeting — where the next press that commits would write the old meeting's title
        // onto this one, or wipe it.
        _nameAsRead = string.Empty;

        Draw(theRecordingToo: true);
    }

    /// <summary>
    /// Reads the meeting on screen again and puts it back on the controls.
    /// </summary>
    /// <param name="theRecordingToo">
    /// Whether the recording is opened again with it. Only when the meeting itself changed: every
    /// other reason to draw — a language, a stage bought — leaves the audio exactly what it was,
    /// and reopening it would stop the playback and rewind it under somebody listening.
    /// </param>
    private void Draw(bool theRecordingToo)
    {
        _read = null;
        _status = null;

        if (_meeting is not { } meetingId)
        {
            Render(theRecordingToo);
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
        }
        else
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                _read = new MeetingReading(context, TimeProvider.System).Of(meetingId);
            }
            catch (MeetingStageException gone)
            {
                _status = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            }
            catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
            {
                _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            }
        }

        Render(theRecordingToo);
    }

    /// <summary>
    /// Lets go of the meeting and of whatever is playing it.
    /// </summary>
    /// <remarks>
    /// Every way off this screen comes through here — the press, the window closing, and being
    /// asked to show a different meeting. A player left running behind a screen nobody is looking
    /// at is sound coming out of an application that appears to be doing nothing else, and the
    /// file and the endpoint it holds are not the window's to leak.
    /// </remarks>
    public void Close()
    {
        StopPlaying();
        _meeting = null;
        _read = null;
        _status = null;
        _nameAsRead = string.Empty;
        NameBox.Text = string.Empty;
        TheSections.Children.Clear();
        Presses.Children.Clear();
    }

    /// <summary>
    /// What this screen says, in the language it is being read in. Every word on it comes through
    /// here, which is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>What the title of one of the three fixed sections says.</summary>
    /// <remarks>
    /// The sections are fixed and are the tables the corpus already has, so this table is closed
    /// and the last arm stops rather than substituting: a kind added to <see cref="LeftKind"/> and
    /// not given a title here would otherwise be drawn under another section's heading, which puts
    /// an open question in the list of things that were settled.
    /// </remarks>
    private static UiText Section(LeftKind kind) => kind switch
    {
        LeftKind.Decision => UiTexts.WhatWasDecided,
        LeftKind.Action => UiTexts.WhatIsLeftToDo,
        LeftKind.Question => UiTexts.WhatWasLeftUnresolved,
        _ => throw new InvalidOperationException($"This screen has no title for the section '{kind}'."),
    };

    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException(
            "The meeting screen was never given a corpus, so it has no meetings to read.");

    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>Puts everything that was read onto the controls.</summary>
    private void Render(bool theRecordingToo)
    {
        TheSections.Children.Clear();
        Presses.Children.Clear();

        if (_read is not { } read)
        {
            NameBox.Text = string.Empty;
            NameBox.IsEnabled = false;
            WhenText.Text = string.Empty;
            StageText.Text = _status?.In(_language) ?? string.Empty;
            TranscribedText.Text = string.Empty;
            SummarisedText.Text = string.Empty;

            if (theRecordingToo)
            {
                ShowThePlayer(playable: false);
            }

            return;
        }

        _nameAsRead = read.Meeting.Title ?? string.Empty;
        NameBox.Text = _nameAsRead;
        NameBox.IsEnabled = read.Screen.TheNameMayBeTyped;

        WhenText.Text = ScreenNumbers.When(read.Meeting);
        StageText.Text = In(MeetingWords.Reached(read.Screen.Stage));

        WhoWroteIt(read.Screen);
        WhatWasLeft(read.Screen.Left);
        TheActOnOffer(read.Screen);

        if (theRecordingToo)
        {
            OpenTheRecording(read);
        }
        else
        {
            // The marks and not the player: a summary that arrived while somebody was listening is
            // more marks along a track that is still running.
            DrawTheMarks();
        }
    }

    /// <summary>
    /// Who transcribed this meeting and who summarised it, and when each of them did.
    /// </summary>
    /// <remarks>
    /// Three answers each and not two. A meeting that arrived here already transcribed carries the
    /// response and no run, so the corpus has nothing to name — and reading that as nobody having
    /// transcribed it, under a line that says the meeting is transcribed, is the screen saying two
    /// opposite things about the same meeting. Whether each half exists at all is
    /// <see cref="MeetingScreen"/>'s, in a project a build agent runs.
    /// </remarks>
    private void WhoWroteIt(MeetingScreen screen)
    {
        var wrote = screen.Left.Wrote;

        TranscribedText.Text = wrote is { Transcriber: { } who, TranscribedAt: { } when }
            ? UiTexts.TranscribedBy.In(_language, who, ScreenNumbers.At(when))
            : In(screen.ThereIsATranscription
                ? UiTexts.TheCorpusDoesNotSayWhoTranscribedIt
                : UiTexts.NobodyHasTranscribedThisYet);

        SummarisedText.Text = wrote is { Summariser: { } model, SummarisedAt: { } then }
            ? UiTexts.SummarisedBy.In(_language, model, ScreenNumbers.At(then))
            : In(screen.ThereIsASummary
                ? UiTexts.TheCorpusDoesNotSayWhoSummarisedIt
                : UiTexts.NobodyHasSummarisedThisYet);
    }

    /// <summary>
    /// What a screen with no player says instead, or nothing when there is one.
    /// </summary>
    /// <remarks>
    /// The last arm stops rather than substituting, for the reason <see cref="MeetingWords"/>
    /// gives about its own tables: a state added to <see cref="RecordedAudio"/> and not given a
    /// sentence here would be shown to somebody as one of the others, and the two that are not
    /// <see cref="RecordedAudio.Playable"/> are a meeting with nothing recorded yet and a meeting
    /// whose recording is gone — which are not the same news.
    /// </remarks>
    private static UiText? WhyItWillNotPlay(RecordedAudio recording) => recording switch
    {
        RecordedAudio.NoneYet => UiTexts.ThereIsNoRecordingUnderThisMeetingYet,
        RecordedAudio.NotWhereTheCorpusSaysItIs => UiTexts.TheRecordingIsNotWhereTheCorpusSaysItIs,
        RecordedAudio.Playable => null,
        _ => throw new InvalidOperationException($"This screen has no text for a recording that is '{recording}'."),
    };

    /// <summary>
    /// The three sections, each one only where it has something in it.
    /// </summary>
    /// <remarks>
    /// A section with nothing in it is not drawn, and there is no line saying so. What the AI has
    /// not left yet is simply not there — which is honest about a meeting nobody has bought
    /// anything for, and is why this screen is the same screen at all three stages rather than a
    /// blueprint per stage.
    /// </remarks>
    private void WhatWasLeft(WhatTheAiLeft left)
    {
        if (left.Abstract is { } about)
        {
            TheSections.Children.Add(new Border
            {
                Style = Chrome("TheAbstract"),
                Child = new TextBlock { Text = about, Style = Chrome("Said") },
            });
        }

        foreach (var kind in Enum.GetValues<LeftKind>())
        {
            if (left.Of(kind) is { Count: > 0 } things)
            {
                TheSections.Children.Add(SectionCard(kind, things));
            }
        }
    }

    private UIElement SectionCard(LeftKind kind, IReadOnlyList<LeftThing> things)
    {
        var inside = new StackPanel { Spacing = 12 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        heading.Children.Add(new TextBlock
        {
            Text = In(Section(kind)),
            Style = Chrome("SectionTitle"),
        });

        // How many there are: a count, so it reads the same in either language.
        heading.Children.Add(new TextBlock
        {
            Text = things.Count.ToString(UiLanguages.Culture(_language)),
            Style = Chrome("SectionCount"),
            VerticalAlignment = VerticalAlignment.Bottom,
        });

        inside.Children.Add(heading);

        foreach (var thing in things)
        {
            inside.Children.Add(OneThing(thing));
        }

        return new Border { Style = Chrome("ReadingCard"), Child = inside };
    }

    /// <summary>
    /// One thing the AI left: what it says, and the press that goes to where it was said.
    /// </summary>
    /// <remarks>
    /// The press is always there, and that is the claim this screen is built around: every thing
    /// the AI left carries where it was said. It cannot be otherwise — a
    /// <see cref="LeftThing"/> has nowhere for a missing offset to live, so there is no state in
    /// which one of these rows could be drawn without its minute.
    /// </remarks>
    private UIElement OneThing(LeftThing thing)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var said = new TextBlock { Text = thing.Says, Style = Chrome("Said") };
        Grid.SetColumn(said, 0);
        row.Children.Add(said);

        // The minute is the button's whole words: a play glyph beside a number, repeated twelve
        // times down a column, is twelve controls saying the same thing. What pressing it does is
        // the help text, which a screen reader reads and the eye does not have to.
        var pill = new Button
        {
            Content = ScreenNumbers.Long(thing.At),
            Style = Chrome("WhenItWasSaid"),
            VerticalAlignment = VerticalAlignment.Top,
        };

        AutomationProperties.SetHelpText(pill, In(UiTexts.WhereThisWasSaid));
        Grid.SetColumn(pill, 1);
        row.Children.Add(pill);

        var unfolded = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        pill.Click += (_, _) => OpenWhereItWasSaid(thing, unfolded);

        var whole = new StackPanel { Spacing = 0 };
        whole.Children.Add(row);
        whole.Children.Add(unfolded);
        return whole;
    }

    /// <summary>
    /// A citation pressed: the player goes to where it was said, and the transcript there unfolds
    /// under the thing that cited it.
    /// </summary>
    /// <remarks>
    /// Both, and in that order. Going there is what the press is for and costs nothing, so it
    /// happens even when there are no turns to unfold — a meeting that was transcribed and whose
    /// files were never produced still plays from the right second.
    /// <para>
    /// Unfolded in place and never on another screen. What somebody is doing is checking that the
    /// sentence above really follows from what was said, and a screen that took them somewhere
    /// else to check would lose the list they were reading down.
    /// </para>
    /// </remarks>
    private void OpenWhereItWasSaid(LeftThing thing, StackPanel unfolded)
    {
        GoTo(thing.At);

        if (unfolded.Visibility is Visibility.Visible)
        {
            unfolded.Visibility = Visibility.Collapsed;
            return;
        }

        unfolded.Visibility = Visibility.Visible;

        if (unfolded.Children.Count > 0)
        {
            return;
        }

        foreach (var line in TheTranscriptAround(thing))
        {
            unfolded.Children.Add(line);
        }
    }

    /// <summary>The turns around a cited one, as the lines they are read as.</summary>
    private IReadOnlyList<UIElement> TheTranscriptAround(LeftThing thing)
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return [];
        }

        IReadOnlyList<Turn> turns;

        try
        {
            using var context = CorpusDatabase.Open(folder);
            turns = new MeetingReading(context, TimeProvider.System).Around(meeting, thing.TurnOrdinal);
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            return [new TextBlock { Text = unreadable.Message, Style = Chrome("Quoted") }];
        }

        // No branch for an empty answer, and that is a fact about the corpus rather than an
        // omission: a citation is a foreign key onto the turn it names, so a thing the AI left
        // cannot be here at all unless the turn it was said in is there to unfold.
        return [.. turns.Select(turn => Spoken(turn.SpeakerLabel, turn.Start, turn.Text))];
    }

    /// <summary>
    /// One turn, as a line of the transcript.
    /// </summary>
    /// <remarks>
    /// The speaker goes as the label the corpus stores and never as a person's name, because
    /// nothing on this screen says who a label is: putting a name here would be this screen
    /// deciding something <c>speaker_assignments</c> is the only thing allowed to answer.
    /// </remarks>
    private UIElement Spoken(string speakerLabel, Duration at, string text)
    {
        var line = new StackPanel { Spacing = 2 };

        line.Children.Add(new TextBlock
        {
            Text = ScreenNumbers.Beside(speakerLabel, ScreenNumbers.Long(at)),
            Style = Chrome("Data"),
        });

        line.Children.Add(new TextBlock { Text = text, Style = Chrome("Quoted") });
        return line;
    }

    /// <summary>
    /// The stage's two answers, each on screen only when it is one somebody may give.
    /// </summary>
    /// <remarks>
    /// The same pair the list carries, in the same two places and in the same order:
    /// <c>docs/design.md</c>'s grammar puts the neutral answer on the left and the act on the
    /// right, and a pair that read the other way round on one screen is where somebody presses the
    /// expensive one out of habit.
    /// </remarks>
    private void TheActOnOffer(MeetingScreen screen)
    {
        if (screen.TheActMayBeLeft)
        {
            var leave = new Button { Content = In(UiTexts.Ignore) };
            leave.Click += (_, _) => Answer(decline: true);
            Presses.Children.Add(leave);
        }

        if (screen.TheActOffered is { } next)
        {
            var take = new Button
            {
                Content = In(MeetingWords.Action(next)),
                Style = Chrome("TakeTheStage"),
            };

            take.Click += (_, _) => Answer(decline: false);
            Presses.Children.Add(take);
        }
    }

    /// <summary>
    /// One of the two presses, and the reason neither checks anything first: what is allowed is
    /// re-read against the corpus inside the call, so a screen drawn before somebody pressed the
    /// same button on the list cannot spend on what it still shows.
    /// </summary>
    private void Answer(bool decline)
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            var work = new MeetingWork(context, TimeProvider.System);

            if (decline)
            {
                work.Decline(meeting);
            }
            else
            {
                work.Take(meeting);
            }
        }
        catch (MeetingStageException stale)
        {
            _status = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        var said = _status;
        Draw(theRecordingToo: false);
        _status ??= said;
        StageText.Text = _status?.In(_language) ?? StageText.Text;
    }

    // ── The name ──────────────────────────────────────────────────────────────────────────────

    private void OnNameLeft(object sender, RoutedEventArgs e) => CommitTheName();

    /// <summary>Whether the name on screen is the name in the corpus.</summary>
    private bool TheNameIsWritten => _read is null
        || string.Equals(NameBox.Text ?? string.Empty, _nameAsRead, StringComparison.Ordinal);

    private void OnNameKey(object sender, KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            CommitTheName();
        }
    }

    /// <summary>
    /// Writes what somebody typed, when they typed something.
    /// </summary>
    /// <remarks>
    /// The comparison is against what the field held when the meeting was drawn, so leaving a
    /// field nobody touched writes nothing at all — a title is a row touched and a recovery card
    /// rewritten, and doing that every time somebody looks away from a meeting would put a write
    /// on the corpus for reading it.
    /// </remarks>
    private bool CommitTheName()
    {
        // Only over a meeting this screen really read. A read that refused leaves the field
        // cleared and disabled — and disabling a field somebody is standing in raises a leave —
        // so without this the empty box would be committed onto whichever meeting was asked for,
        // taking off a title nobody touched.
        if (_read is null || TheNameIsWritten)
        {
            return true;
        }

        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return true;
        }

        var typed = NameBox.Text ?? string.Empty;

        try
        {
            using var context = CorpusDatabase.Open(folder);
            new MeetingReading(context, TimeProvider.System).Name(meeting, typed);
            _nameAsRead = typed;
            return true;
        }
        catch (MeetingStageException gone)
        {
            StageText.Text = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message).In(_language);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            StageText.Text = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message).In(_language);
        }

        return false;
    }

    // ── The player ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the recording, or says why it will not play.
    /// </summary>
    /// <remarks>
    /// Whether there is a player at all is <see cref="MeetingScreen.MayBePlayedBack"/>'s and turns
    /// on the stage alone — no transcription, no job, no price. What can still go wrong after that
    /// is the file or the machine, and both of those are said where the player would have been
    /// rather than left as a play button that does nothing.
    /// </remarks>
    private void OpenTheRecording(MeetingAsRead read)
    {
        if (!read.Screen.MayBePlayedBack || read.Audio is not { } recording)
        {
            // The second half is a disagreement the corpus side cannot produce — it decides both
            // off the same two reads — so it falls to the sentence that would be true if it ever
            // did: the corpus says there is a recording here and this screen has no file.
            SayThePlayerWillNot(In(WhyItWillNotPlay(read.Screen.TheRecording)
                ?? UiTexts.TheRecordingIsNotWhereTheCorpusSaysItIs));

            return;
        }

        try
        {
            _playing = Playback.Of(recording);
        }
        catch (Exception wont) when (ScreenFailures.Reportable(wont))
        {
            // Everything a read of a file can be and not only the audio's own refusal: the file is
            // opened here, so a recording a backup has locked or an ACL refuses arrives as an
            // IOException, and this is the one read on this screen that would otherwise take the
            // window down with it.
            SayThePlayerWillNot(wont.Message);
            return;
        }

        ShowThePlayer(playable: true);

        _movingTheTrack = true;
        Track.Maximum = Math.Max(1, _playing.Length.Milliseconds);
        Track.Value = 0;
        _movingTheTrack = false;

        LengthText.Text = ScreenNumbers.Long(_playing.Length);
        ShowWhereItIs();
        DrawTheMarks();
    }

    private void ShowThePlayer(bool playable)
    {
        Player.Visibility = playable ? Visibility.Visible : Visibility.Collapsed;
        PlayerStatusText.Visibility = Visibility.Collapsed;
        PlayerStatusText.Text = string.Empty;
    }

    private void SayThePlayerWillNot(string why)
    {
        Player.Visibility = Visibility.Collapsed;
        PlayerStatusText.Visibility = Visibility.Visible;
        PlayerStatusText.Text = UiTexts.ThisMeetingWillNotPlay.In(_language, why);
    }

    private void OnPlayOrPause(object sender, RoutedEventArgs e)
    {
        if (_playing is not { } playing)
        {
            return;
        }

        if (playing.IsPlaying)
        {
            playing.Pause();
            _watch.Stop();
        }
        else
        {
            playing.Play();
            _watch.Start();
        }

        ShowWhereItIs();
    }

    private void OnWatch(object? sender, object e)
    {
        if (_playing is not { } playing)
        {
            _watch.Stop();
            return;
        }

        if (playing.WhatStoppedIt is { } broke)
        {
            // The endpoint pushes the audio on a thread of its own, so a device pulled out mid
            // meeting fails over there and nowhere this screen is standing. Without this the
            // player would simply say Play again, and the reader would be told a recording that
            // cannot play is one they have paused.
            StopPlaying();
            SayThePlayerWillNot(broke.Message);
            return;
        }

        if (!playing.IsPlaying)
        {
            // It ran out, or somebody stopped it elsewhere. The watch is the only thing that finds
            // out — an endpoint reaching the end of a stream announces nothing this screen hears.
            _watch.Stop();
        }

        ShowWhereItIs();
    }

    /// <summary>Moves the track and the clock to where the recording actually is.</summary>
    private void ShowWhereItIs()
    {
        if (_playing is not { } playing)
        {
            return;
        }

        PlayButton.Content = In(playing.IsPlaying ? UiTexts.Pause : UiTexts.Play);
        AtText.Text = ScreenNumbers.Long(playing.At);

        _movingTheTrack = true;
        Track.Value = Math.Clamp(playing.At.Milliseconds, Track.Minimum, Track.Maximum);
        _movingTheTrack = false;
    }

    private void OnTrackMoved(object sender, RangeBaseValueChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (_movingTheTrack || _playing is null)
        {
            return;
        }

        GoTo(Duration.FromMilliseconds((long)e.NewValue));
    }

    /// <summary>
    /// Puts the recording at a point in itself, whether or not it is playing.
    /// </summary>
    /// <remarks>
    /// It does not start playing. Pressing a citation is somebody asking where a thing was said,
    /// and an application that started making noise at them for asking is one they stop pressing.
    /// </remarks>
    private void GoTo(Duration at)
    {
        if (_playing is not { } playing)
        {
            return;
        }

        playing.Seek(at);
        ShowWhereItIs();
    }

    /// <summary>
    /// Draws one mark on the track for each thing the AI left.
    /// </summary>
    /// <remarks>
    /// Where each of them falls across the hour, so a summary is not only a list but a shape: four
    /// decisions in the first ten minutes and nothing after is a meeting somebody can see the
    /// shape of before reading a word of it. The marks are placed at a fraction of the track's
    /// laid-out width, which nothing but the laid-out control knows — so they are drawn again
    /// whenever it changes size.
    /// </remarks>
    private void DrawTheMarks()
    {
        Marks.Children.Clear();

        if (_read is not { } read || _playing is not { } playing)
        {
            return;
        }

        var length = playing.Length.Milliseconds;

        if (length <= 0 || Marks.ActualWidth <= 0)
        {
            return;
        }

        // Its own width taken off the run, so the one at the very end of a meeting is drawn on the
        // track rather than one mark past the right-hand edge of it.
        var run = Math.Max(0, Marks.ActualWidth - HowWideAMarkIs);

        foreach (var at in read.Screen.MarkedAlongTheMeeting)
        {
            var mark = new Rectangle { Style = Chrome("ACitationOnTheTrack"), Width = HowWideAMarkIs };
            Canvas.SetLeft(mark, Math.Clamp(at.Milliseconds / (double)length, 0, 1) * run);
            Marks.Children.Add(mark);
        }
    }

    private void OnTrackResized(object sender, SizeChangedEventArgs e) => DrawTheMarks();

    private void StopPlaying()
    {
        _watch.Stop();
        _playing?.Dispose();
        _playing = null;
        Marks.Children.Clear();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        // The name first, and this screen stays where it is when the corpus would not take it.
        // Pressing back with a title typed is somebody who meant to keep it — and leaving over a
        // refusal would put the message on a control the window is about to hide, which is the
        // one thing on this screen that would silently lose what a person wrote.
        if (!CommitTheName())
        {
            return;
        }

        Close();
        Left?.Invoke(this, EventArgs.Empty);
    }
}
