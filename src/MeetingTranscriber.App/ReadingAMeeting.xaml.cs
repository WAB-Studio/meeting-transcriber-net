using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Recording;

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
/// The screen one meeting is read from: what the AI left of it, the whole transcript under that,
/// who made what, what the meeting is doing now — and, along the bottom, the recording itself.
/// </summary>
/// <remarks>
/// <para>
/// What somebody comes back for first is what was decided, what is left to do and what was left
/// unresolved, so those are on top, each thing carrying the minute it was said at and unfolding the
/// turns around it in place. Under them stands the whole transcript, once there is one, because a
/// meeting that is transcribed has to be readable (<c>docs/design.md</c> §Reunion). It is a
/// repeater and draws only the lines in view, and every line is read through
/// <see cref="MeetingRenderer.AsRead"/>, so this screen and <c>transcript.md</c> say the same words.
/// Words are corrected from where they are read: selecting some offers <em>Corregir</em> beside
/// them, which asks the window to open the corrections screen with those words in its field
/// (<see cref="CorrectTheSelection"/>).
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
    /// How often a meeting with work under way is asked what it is doing. Often enough that a
    /// summary that lands is on screen within a moment of landing, and rare enough that a stretch
    /// of waiting is a read of one meeting's jobs every couple of seconds and nothing more.
    /// </summary>
    private static readonly TimeSpan HowOftenTheWorkIsAsked = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _workWatch = new() { Interval = HowOftenTheWorkIsAsked };

    /// <summary>True while a read of the meeting's work is out, so a slow one is not asked twice.</summary>
    private bool _askingTheWork;

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
    /// What this meeting is filed under, read with it. Its own read and not part of
    /// <see cref="MeetingAsRead"/>, because this screen wants two rows of pills and the screen that
    /// sets them wants the whole tree, everybody and their affiliations — and paying for those to
    /// draw a block in a corner is a read nobody asked for.
    /// </summary>
    private IReadOnlyList<(MeetingNodeRole Role, NodePath Path)>? _filing;

    /// <summary>Who spoke on this meeting, read with it, for the card that offers naming them.</summary>
    private WhoIsWho? _voices;

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

    // Where the volume stood before a press on its glyph muted it, and whether the pointer is over
    // the volume or holds the slider, which is what keeps the slider open under a drag that has
    // left it.
    private double _volumeBeforeMute = 100;
    private bool _pointerIsOverTheVolume;
    private bool _volumeIsBeingDragged;

    /// <summary>
    /// Every turn of the meeting as the rendered files say it, read with it, or nothing while there
    /// is no transcription. What the transcript's lines and the turns unfolded under a citation are
    /// both made of, so the two cannot say different words.
    /// </summary>
    private IReadOnlyList<Turn>? _turns;

    /// <summary>One line of the transcript: who said it, the minute, and the words.</summary>
    private sealed record TranscriptLine(string Who, Duration At, string Text);

    /// <summary>
    /// What a repeater builds each line with, handed the screen's own method: the line is built
    /// here, where the styles are.
    /// </summary>
    private sealed class LineFactory(Func<TranscriptLine, UIElement> build) : IElementFactory
    {
        public UIElement GetElement(ElementFactoryGetArgs args) => build((TranscriptLine)args.Data);

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            // Nothing is held on a line between uses: what it offered over a selection hides itself
            // when the repeater scrolls it away.
        }
    }

    /// <summary>True while the name is a field somebody is typing in and not a line of text.</summary>
    private bool _renaming;

    /// <summary>What the name field held when the meeting was drawn, so a leave that changed
    /// nothing writes nothing.</summary>
    private string _nameAsRead = string.Empty;

    private readonly ScreenStatus _status = new();

    public ReadingAMeeting()
    {
        InitializeComponent();
        _watch.Tick += OnWatch;
        _workWatch.Tick += OnWorkWatch;
        TheTranscriptLines.ItemTemplate = new LineFactory(ATurn);

        VolumeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnVolumePressed), true);
        VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumeLetGo), true);
        VolumeSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnVolumeLetGo), true);
    }

    /// <summary>Somebody asked to go back to the meetings.</summary>
    public event EventHandler? Left;

    /// <summary>Somebody asked to file this meeting under what it was about.</summary>
    public event EventHandler<Guid>? Classify;

    /// <summary>Somebody asked to name who spoke on this meeting.</summary>
    public event EventHandler<Guid>? NameTheVoices;

    /// <summary>
    /// Somebody asked to correct words on this meeting: with the words they selected, or with none
    /// when they asked for the screen itself.
    /// </summary>
    public event EventHandler<WordsToCorrect>? CorrectWords;

    /// <summary>Somebody asked to read the history of something this meeting is filed under.</summary>
    public event EventHandler<Guid>? NodeChosen;

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
    /// Reads this meeting again without touching the recording.
    /// </summary>
    /// <remarks>
    /// What the screen that files a meeting comes back through. Coming back is a redraw and not a
    /// reopen, so the player is exactly where <see cref="Pause"/> left it rather than rewound to
    /// the start of an hour somebody was half way through.
    /// </remarks>
    public void ReadAgain() => Draw(theRecordingToo: false);

    /// <summary>
    /// Stops the recording where it is, without letting go of it.
    /// </summary>
    /// <remarks>
    /// What <see cref="Close"/> says is why this exists: a player left running behind a screen
    /// nobody is looking at is sound coming out of an application that appears to be doing nothing
    /// else. The screen that files this meeting takes the window and leaves this one collapsed —
    /// out of the automation tree as well, so a reader would have audio coming from nothing they
    /// can find — and coming back is a redraw, so the recording is kept and paused rather than
    /// closed and rewound.
    /// </remarks>
    public void Pause()
    {
        CorrectTheSelection.Dismiss();
        _watch.Stop();
        _workWatch.Stop();

        if (_playing is { IsPlaying: true } playing)
        {
            playing.Pause();
            ShowWhereItIs();
        }
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
        _filing = null;
        _voices = null;
        _turns = null;
        _status.Nothing();

        if (_meeting is not { } meetingId)
        {
            Render(theRecordingToo);
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            _status.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
        }
        else
        {
            try
            {
                // Both reads inside the one context, and both here rather than in Render. That
                // method is called from the early return above, where no context was ever opened,
                // and again after this block has closed the one there is — which is why what was
                // read is a field at all. A read from in there would also put a SqliteException on
                // the UI thread outside both of these catches, where nothing is holding it.
                using var context = CorpusDatabase.Open(folder);
                _read = new MeetingReading(context, TimeProvider.System).Of(meetingId);
                _filing = new MeetingClassifying(context, TimeProvider.System).Filing(meetingId);
                _voices = new MeetingVoices(context, TimeProvider.System).Heard(meetingId);

                // The corrected words and not the stored ones: what the rendered files carry, read
                // now, so a correction made a moment ago is on this screen without waiting for the
                // files to be written again. Only once there is a transcription to read.
                if (_read.Screen.ThereIsATranscription)
                {
                    _turns = MeetingRenderer.AsRead(context, meetingId);
                }
            }
            catch (MeetingStageException gone)
            {
                _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            }
            catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
            {
                _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
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
        CorrectTheSelection.Dismiss();
        StopPlaying();
        _workWatch.Stop();
        _meeting = null;
        _read = null;
        _filing = null;
        _voices = null;
        _turns = null;
        _status.Nothing();
        _nameAsRead = string.Empty;
        NameBox.Text = string.Empty;
        TitleText.Text = string.Empty;
        SwapToTheTitle();
        TheTranscriptLines.ItemsSource = null;
        TheTranscriptCard.Visibility = Visibility.Collapsed;
        WhoMadeWhat.Visibility = Visibility.Collapsed;
        SummaryFailedText.Visibility = Visibility.Collapsed;
        TheSections.Children.Clear();
        Presses.Children.Clear();
        TheSummaries.Children.Clear();
        TheSummariesCard.Visibility = Visibility.Collapsed;
        TheFiling.Children.Clear();
        TheVoices.Children.Clear();
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
        TheSummaries.Children.Clear();
        TheSummariesCard.Visibility = Visibility.Collapsed;
        TheFiling.Children.Clear();
        TheVoices.Children.Clear();

        if (_read is not { } read)
        {
            _workWatch.Stop();
            NameBox.Text = string.Empty;
            NameBox.IsEnabled = false;
            RenameButton.IsEnabled = false;
            TitleText.Text = string.Empty;
            SwapToTheTitle();
            ShowTheDataLine();
            WhoMadeWhat.Visibility = Visibility.Collapsed;
            SummaryFailedText.Visibility = Visibility.Collapsed;
            TheTranscriptLines.ItemsSource = null;
            TheTranscriptCard.Visibility = Visibility.Collapsed;
            ClassifyButton.IsEnabled = false;
            WhoSpokeCard.Visibility = Visibility.Collapsed;
            WordsCard.Visibility = Visibility.Collapsed;

            if (theRecordingToo)
            {
                ShowThePlayer(playable: false);
            }

            return;
        }

        _nameAsRead = read.Meeting.Title ?? string.Empty;
        NameBox.IsEnabled = read.Screen.TheNameMayBeTyped;
        RenameButton.IsEnabled = read.Screen.TheNameMayBeTyped;

        // Somebody typing a name keeps the field they are typing in: a summary landing is a redraw
        // of everything else, and swapping the field for text under their cursor would throw away
        // what they had written.
        if (!_renaming)
        {
            NameBox.Text = _nameAsRead;
            SwapToTheTitle();
        }

        ShowTheDataLine();

        // The standing decides whether something is under way, and the status is read off the same
        // standing, so asking again while it holds and stopping when it does not cannot part.
        KeepAskingWhileWorkIsUnderWay(read.Screen.WorkIsUnderWay);

        WhoMadeWhatSection(read.Screen);
        SummariesSection(read.Screen);
        WhatWasLeft(read.Screen.Left);
        TheTranscriptSection();
        TheActOnOffer(read.Screen);
        WhatItWasAbout(read.Screen);
        WhoSpokeSection();
        WordsSection(read.Screen);

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
    /// The one data line under the title: when the meeting was, how long it ran, and what it is
    /// doing now in the one word the list says — or what a read or a press just refused, which a
    /// word saying the meeting is fine would otherwise hide.
    /// </summary>
    private void ShowTheDataLine()
    {
        var when = _read is { } read ? ScreenNumbers.When(read.Meeting) : string.Empty;

        var status = _status.IsSaying
            ? _status.In(_language)
            : _read is { } drawn ? In(MeetingWords.Status(drawn.Screen.Owed.Status)) : string.Empty;

        WhenText.Text = when.Length == 0 ? status
            : status.Length == 0 ? when
            : ScreenNumbers.Beside(when, status);
    }

    /// <summary>
    /// Shows the name as a line of text: the name, or <em>Sin nombre</em> in secondary ink when
    /// nobody has named the meeting, with <em>Renombrar</em> beside it.
    /// </summary>
    private void SwapToTheTitle()
    {
        _renaming = false;

        var named = _nameAsRead.Length > 0;
        TitleText.Text = named ? _nameAsRead : _read is null ? string.Empty : In(UiTexts.AMeetingNobodyHasNamed);
        TitleText.Style = Chrome(named ? "TheTitle" : "TheTitleNobodyNamed");

        NameBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        RenameButton.Visibility = Visibility.Visible;
    }

    /// <summary><em>Renombrar</em>: the line of text becomes the field holding the name, selected.</summary>
    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (_read is null)
        {
            return;
        }

        _renaming = true;
        NameBox.Text = _nameAsRead;
        TitleText.Visibility = Visibility.Collapsed;
        RenameButton.Visibility = Visibility.Collapsed;
        NameBox.Visibility = Visibility.Visible;
        NameBox.UpdateLayout();
        _ = NameBox.Focus(FocusState.Programmatic);
        NameBox.SelectAll();
    }

    /// <summary>
    /// Who made what and when, as a compact table: a row for the transcription once there is one
    /// and a row for the summary once there is one, and under it the one sentence a refused or
    /// failed summary comes to.
    /// </summary>
    /// <remarks>
    /// Two answers per row and not one. A meeting that arrived here already transcribed carries the
    /// response and no run, so the corpus has nothing to name — and a row that was left out for
    /// that would be the screen saying the meeting was never transcribed, under a status that says
    /// it was. The row is there and its cell says nothing is recorded. A stage that has not
    /// happened has no row at all: the status above already says where the meeting is, and a row
    /// reading <em>nobody yet</em> says it twice. Whether each stage exists is
    /// <see cref="MeetingScreen"/>'s, in a project a build agent runs.
    /// </remarks>
    private void WhoMadeWhatSection(MeetingScreen screen)
    {
        var wrote = screen.Left.Wrote;

        var transcribed = screen.ThereIsATranscription ? Visibility.Visible : Visibility.Collapsed;
        TranscribedLabel.Visibility = transcribed;
        TranscribedText.Visibility = transcribed;
        TranscribedText.Text = wrote is { Transcriber: { } who, TranscribedAt: { } when }
            ? ScreenNumbers.Beside(who, ScreenNumbers.At(when))
            : In(UiTexts.NotRecorded);

        var summarised = screen.ThereIsASummary ? Visibility.Visible : Visibility.Collapsed;
        SummarisedLabel.Visibility = summarised;
        SummarisedText.Visibility = summarised;
        SummarisedText.Text = wrote is { Summariser: { } model, SummarisedAt: { } then }
            ? ScreenNumbers.Beside(model, ScreenNumbers.At(then))
            : In(UiTexts.NotRecorded);

        WhoMadeWhat.Visibility = screen.ThereIsATranscription ? Visibility.Visible : Visibility.Collapsed;

        // The summary before stays on screen when the one after it failed, so the sentence is about
        // the attempt after it. A refusal says what it was refused for, in words that do not open by
        // denying the summary that is right there; every other failure keeps its own sentence.
        var failed = screen switch
        {
            { ThereIsASummary: false, WhyTheSummaryWasRefused: { } refusal } => RefusedText(refusal),
            { WhyTheLastSummaryFailed: JobFailure.ExtractionRefused, WhyTheSummaryWasRefused: { } refusedAgain } =>
                LastRefusedText(refusedAgain),
            { WhyTheLastSummaryFailed: { } failure } => In(MeetingWords.Failed(failure)),
            _ => string.Empty,
        };

        SummaryFailedText.Text = failed;
        SummaryFailedText.Visibility = failed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>What the screen says about a summary attempt that was refused, over no summary.</summary>
    private string RefusedText(ExtractionRefusal refusal)
    {
        var condition = RefusedBecause(refusal.Condition).In(_language);

        return refusal.Statement is { } statement
            ? UiTexts.SummaryNotAcceptedOn.In(_language, condition, statement)
            : UiTexts.SummaryNotAccepted.In(_language, condition);
    }

    /// <summary>What the screen says about a second summary that was refused, over one that exists.</summary>
    private string LastRefusedText(ExtractionRefusal refusal)
    {
        var condition = RefusedBecause(refusal.Condition).In(_language);

        return refusal.Statement is { } statement
            ? UiTexts.TheLastSummaryWasNotAcceptedOn.In(_language, condition, statement)
            : UiTexts.TheLastSummaryWasNotAccepted.In(_language, condition);
    }

    /// <summary>
    /// Every summary the meeting was given, as a radio row each, when there is more than one to
    /// choose between.
    /// </summary>
    /// <remarks>
    /// <c>Checked</c> and not <c>Click</c>: the arrow keys and UI Automation's Select — which is what
    /// Narrator calls — check a row without clicking it, so a write on <c>Click</c> let the check
    /// move while the summary stayed. A row being drawn never writes, and there is no flag saying
    /// so: <c>IsChecked</c> is set in the initialiser, before <c>Checked +=</c>, so drawing raises
    /// nothing a handler hears; and the one row drawn checked is the one shown, which
    /// <see cref="ShowSummary"/> refuses on <c>given.IsShown</c> anyway. The rows are the card's own,
    /// cleared and drawn again by every <see cref="Render"/>, the way <c>Presses</c> are.
    /// </remarks>
    private void SummariesSection(MeetingScreen screen)
    {
        TheSummaries.Children.Clear();
        TheSummariesCard.Visibility = screen.ASummaryMayBeChosen ? Visibility.Visible : Visibility.Collapsed;

        if (!screen.ASummaryMayBeChosen)
        {
            return;
        }

        foreach (var given in screen.EverySummary)
        {
            var row = new RadioButton
            {
                Content = ScreenNumbers.Beside(given.WrittenBy, ScreenNumbers.At(given.AcceptedAt)),
                Style = Chrome("ASummaryOfThisMeeting"),
                GroupName = nameof(TheSummaries),
                IsChecked = given.IsShown,
            };

            row.Checked += (_, _) => ShowSummary(given);
            TheSummaries.Children.Add(row);
        }
    }

    /// <summary>Somebody chose another of the meeting's summaries. The one call that puts it back.</summary>
    private void ShowSummary(GivenSummary given)
    {
        if (given.IsShown || _meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            new MeetingReading(context, TimeProvider.System).ShowSummary(meeting, given.RunId);
        }
        catch (MeetingStageException stale)
        {
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        AfterWriting();

        // The redraw threw the row somebody was on away with the rest of them, so the keyboard goes
        // back to the one that is checked now — and not to wherever the framework leaves it, which
        // was the press that files the meeting. Somebody walking the group with the arrow keys, or
        // Narrator reading it, would otherwise have to find the group again after every choice.
        TheSummaries.Children.OfType<RadioButton>()
            .FirstOrDefault(row => row.IsChecked is true)?
            .Focus(FocusState.Keyboard);
    }

    /// <summary>The sentence naming why an extraction was refused, one per condition.</summary>
    private static UiText RefusedBecause(ExtractionCondition condition) => condition switch
    {
        ExtractionCondition.NotTheSchema => UiTexts.RefusedNotTheSchema,
        ExtractionCondition.InputNotAsPrepared => UiTexts.RefusedInputNotAsPrepared,
        ExtractionCondition.AnotherMeeting => UiTexts.RefusedAnotherMeeting,
        ExtractionCondition.SpeakerNotInTheMeeting => UiTexts.RefusedSpeakerNotInTheMeeting,
        ExtractionCondition.NoEvidence => UiTexts.RefusedNoEvidence,
        ExtractionCondition.NoSuchTurn => UiTexts.RefusedNoSuchTurn,
        ExtractionCondition.NotTheTurnCited => UiTexts.RefusedNotTheTurnCited,
        ExtractionCondition.QuoteNotInTheTurn => UiTexts.RefusedQuoteNotInTheTurn,
        ExtractionCondition.CitedAgainElsewhere => UiTexts.RefusedCitedAgainElsewhere,
        _ => throw new InvalidOperationException($"No screen has text for extraction condition '{condition}'."),
    };

    /// <summary>
    /// What this meeting is filed under, and whether it is one to file at all.
    /// </summary>
    /// <remarks>
    /// One chip per place, carrying the whole path down the tree rather than the node the meeting
    /// hangs off: <em>ticket #4312</em> on its own names an incident belonging to nobody, and the
    /// company above it is what makes it a subject. Which of the three ways it relates to each is
    /// not said here — the chip is now the press that opens that node's own history, and which of
    /// the three ways it relates is a question for the screen <see cref="Classify"/> opens, not
    /// this one.
    /// </remarks>
    private void WhatItWasAbout(MeetingScreen screen)
    {
        ClassifyButton.IsEnabled = screen.ItMayBeFiled;

        if (_filing is not { Count: > 0 } filed)
        {
            // A real answer and not a gap: §5.3 says a casual chat is stored with no links at all,
            // and somebody chooses that rather than failing to answer.
            TheFiling.Children.Add(new TextBlock
            {
                Text = In(UiTexts.ItIsFiledUnderNothing),
                Style = Chrome("FiledUnderNothing"),
            });

            return;
        }

        // One chip per place and not per link. A meeting that is work of a company and has that
        // same company on the other side of the table is two links and one answer to the question
        // this block asks, and drawing the name twice reads as something gone wrong rather than as
        // a filing — grouped on the deepest node's id rather than on the drawn path, which is the
        // same fact the old text comparison protected.
        var places = filed
            .GroupBy(found => found.Path.Nodes[^1].Id)
            .Select(group => group.First().Path);

        foreach (var path in places)
        {
            var node = path.Nodes[^1].Id;

            var chip = new Button
            {
                Style = Chrome("FiledUnder"),
                Content = new TextBlock
                {
                    Text = ScreenNumbers.Inside([.. path.Nodes.Select(found => found.Name)]),
                    Style = Chrome("FiledUnderSays"),
                },
            };

            chip.Click += (_, _) => NodeChosen?.Invoke(this, node);
            TheFiling.Children.Add(chip);
        }
    }

    /// <summary>
    /// Who spoke, and the way to say who is who. Collapsed rather than drawn empty: a meeting
    /// nothing has transcribed yet has no voices to offer, and a card with nothing in it but a
    /// button reading <em>Decir quién es quién</em> is not a card this screen has anything to show.
    /// </summary>
    private void WhoSpokeSection()
    {
        var voices = _voices?.Voices ?? [];

        WhoSpokeCard.Visibility = voices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var voice in voices)
        {
            TheVoices.Children.Add(new TextBlock
            {
                Text = ScreenNumbers.Beside(
                    VoiceWords.ReadsAs(voice, _language), VoiceWords.TurnsSaid(voice.TurnsSaid, _language)),
                Style = Chrome("Data"),
            });
        }
    }

    /// <summary>
    /// The way to correct the words that came out wrong. Collapsed until the meeting has a
    /// transcription, for the reason <see cref="WhoSpokeSection"/> is: before that there are no
    /// words to be wrong.
    /// </summary>
    private void WordsSection(MeetingScreen screen) =>
        WordsCard.Visibility = screen.ThereIsATranscription ? Visibility.Visible : Visibility.Collapsed;

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
        RecordedAudio.NotWhereTheCorpusSaysItIs => UiTexts.TheRecordingFileIsMissing,
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
        // The abstract and, under it, the summary's longer account: what the AI wrote about the
        // meeting is one block on the decision tint, and a summary shown as its title alone is a
        // summary that was left out. Either alone is still drawn.
        if (left.Abstract is not null || left.Body is not null)
        {
            var about = new StackPanel { Spacing = 10 };

            foreach (var words in new[] { left.Abstract, left.Body }.OfType<string>())
            {
                about.Children.Add(new TextBlock { Text = words, Style = Chrome("Said") });
            }

            TheSections.Children.Add(new Border { Style = Chrome("TheAbstract"), Child = about });
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
    /// <remarks>
    /// Out of the turns this screen already read, corrected, and not out of the corpus again: the
    /// lines unfolded under a citation and the lines of the transcript are one set of words, so a
    /// correction cannot show in one and not the other.
    /// </remarks>
    private IReadOnlyList<UIElement> TheTranscriptAround(LeftThing thing)
    {
        // No branch for an empty answer, and that is a fact about the corpus rather than an
        // omission: a citation is a foreign key onto the turn it names, so a thing the AI left
        // cannot be here at all unless the turn it was said in is there to unfold — and every label
        // a turn of this meeting carries is one WhoIsWho.Of built a voice for, from the same turns.
        if (_turns is not { } turns || _voices is not { } voices)
        {
            return [];
        }

        var first = Math.Max(0, thing.TurnOrdinal - MeetingReading.TurnsEitherSide);
        var last = thing.TurnOrdinal + MeetingReading.TurnsEitherSide;

        return
        [
            .. turns
                .Where(turn => turn.Ordinal >= first && turn.Ordinal <= last)
                .Select(turn => Spoken(
                    VoiceWords.ReadsAs(voices.ForLabel(turn.SpeakerLabel)!, _language), turn.Start, turn.Text)),
        ];
    }

    /// <summary>
    /// One turn, as a line unfolded under a citation.
    /// </summary>
    /// <remarks>
    /// <paramref name="who"/> is <see cref="VoiceWords.ReadsAs"/>'s: their name once somebody has
    /// named their voice, and the same handle <c>SayingWhoIsWho</c> shows until then. Never the
    /// label <c>speaker_assignments</c> stores it under, which is <see cref="WhoIsWho"/>'s alone to
    /// read.
    /// </remarks>
    private UIElement Spoken(string who, Duration at, string text)
    {
        var line = new StackPanel { Spacing = 2 };

        line.Children.Add(new TextBlock
        {
            Text = ScreenNumbers.Beside(who, ScreenNumbers.Long(at)),
            Style = Chrome("Data"),
        });

        line.Children.Add(new TextBlock { Text = text, Style = Chrome("Quoted") });
        return line;
    }

    // ── The transcript ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole transcript, once there is one: a line per turn, under what the AI left.
    /// </summary>
    /// <remarks>
    /// Handed to the repeater as data and never built here a line at a time: the repeater asks for
    /// the lines in view, which is what keeps an hour of talk as cheap as five minutes of it. Who
    /// said each line is <see cref="VoiceWords.ReadsAs"/>'s, for the reason <see cref="Spoken"/>
    /// gives.
    /// </remarks>
    private void TheTranscriptSection()
    {
        if (_turns is not { Count: > 0 } turns || _voices is not { } voices)
        {
            TheTranscriptLines.ItemsSource = null;
            TheTranscriptCard.Visibility = Visibility.Collapsed;
            return;
        }

        TheTranscriptLines.ItemsSource = new List<TranscriptLine>(
            turns.Select(turn => new TranscriptLine(
                VoiceWords.ReadsAs(voices.ForLabel(turn.SpeakerLabel)!, _language), turn.Start, turn.Text)));

        TheTranscriptCard.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// One line of the transcript: who said it and the minute as a press that seeks the player, and
    /// under them the words, selectable and offering <em>Corregir</em> beside a selection.
    /// </summary>
    private UIElement ATurn(TranscriptLine line)
    {
        var who = new TextBlock
        {
            Text = line.Who,
            Style = Chrome("Data"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The minute is the press's whole words, for the reason the citations' presses are: what
        // pressing it does is the help text, which a screen reader reads and the eye does not.
        var minute = new Button
        {
            Content = ScreenNumbers.Long(line.At),
            Style = Chrome("WhenItWasSaid"),
        };

        AutomationProperties.SetHelpText(minute, In(UiTexts.WhereThisWasSaid));
        minute.Click += (_, _) => GoTo(line.At);

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(who);
        heading.Children.Add(minute);

        var said = new TextBlock { Text = line.Text, Style = Chrome("Spoken") };
        CorrectTheSelection.OfferOver(said, Chrome("TheSelectionsPress"), In(UiTexts.CorrectThisWord), AskToCorrect);

        var turn = new StackPanel { Spacing = 2 };
        turn.Children.Add(heading);
        turn.Children.Add(said);
        return turn;
    }

    // ── What is under way ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts asking what the meeting is doing while something is under way for it, and stops when
    /// nothing is.
    /// </summary>
    private void KeepAskingWhileWorkIsUnderWay(bool underWay)
    {
        if (underWay)
        {
            _workWatch.Start();
        }
        else
        {
            _workWatch.Stop();
        }
    }

    private void OnWorkWatch(object? sender, object e) => _ = AskTheWorkAsync();

    /// <summary>
    /// Asks what is owed on the meeting, off the UI thread, and draws it again only when its stage or
    /// its status changed.
    /// </summary>
    /// <remarks>
    /// Without touching the player: a summary that lands while somebody is listening is more of the
    /// screen, and the recording is exactly where it was. A status that did not change draws
    /// nothing, so a minute of waiting is not a minute of the screen blinking. The stage is compared
    /// with it: a transcription that lands while another is queued leaves the status at
    /// <em>queued</em>, and a screen that compared the status alone would not show the transcript
    /// until the summary started.
    /// </remarks>
    private async Task AskTheWorkAsync()
    {
        if (_askingTheWork || _meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        _askingTheWork = true;

        try
        {
            var owed = await Task.Run(() =>
            {
                using var context = CorpusDatabase.Open(folder);
                return new MeetingWork(context, TimeProvider.System).On(meeting);
            });

            // Another meeting was opened, or this one let go of, while the corpus was being asked.
            if (_workWatch.IsEnabled && _meeting == meeting && _read is { } read && (owed.Stage, owed.Status) != (read.Screen.Owed.Stage, read.Screen.Owed.Status) && CommitTheName())
            {
                Draw(theRecordingToo: false);
            }
        }
        catch (MeetingStageException gone)
        {
            _workWatch.Stop();
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            ShowTheDataLine();
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            _workWatch.Stop();
            _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            ShowTheDataLine();
        }
        finally
        {
            _askingTheWork = false;
        }
    }

    /// <summary>
    /// The stage's answers, each on screen only when it is one somebody may give — or, on a summarised
    /// meeting, the one way to ask for another summary.
    /// </summary>
    /// <remarks>
    /// The same pair the list carries, in the same two places and in the same order:
    /// <c>docs/design.md</c>'s grammar puts the neutral answer on the left and the act on the
    /// right, and a pair that read the other way round on one screen is where somebody presses the
    /// expensive one out of habit.
    /// </remarks>
    private void TheActOnOffer(MeetingScreen screen)
    {
        if (screen.WorkIsUnderWay)
        {
            // Where the press was, and not under it: somebody who pressed *Resumir* looks here
            // for what happened, and a press that goes quiet with nothing in its place reads as
            // one that did nothing. *Detener* and *Resumir de nuevo* are not this press and keep
            // their own conditions below.
            Presses.Children.Add(WorkUnderWay(screen));
        }
        else
        {
            if (screen.TheActMayBeLeft)
            {
                var leave = new Button { Content = In(UiTexts.Ignore), Style = Chrome("TheNeutralOne") };
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

        if (screen.TheSummaryMayBeStopped)
        {
            var stop = new Button
            {
                Content = In(UiTexts.Stop),
                Style = Chrome("TakeTheStage"),
            };

            stop.Click += (_, _) => StopTheSummary();
            Presses.Children.Add(stop);
        }

        if (screen.TheSummaryMayBeAskedForAgain)
        {
            var again = new Button
            {
                Content = In(UiTexts.SummariseAgain),
                Style = Chrome("TakeTheStage"),
            };

            again.Click += (_, _) => SummariseAgain();
            Presses.Children.Add(again);
        }
    }

    /// <summary>
    /// The word for what the meeting is doing, in olivo while it runs and in secondary ink while it
    /// waits, and a ring that turns for as long as either holds.
    /// </summary>
    private StackPanel WorkUnderWay(MeetingScreen screen)
    {
        var running = screen.Owed.Standing is StageStanding.Running;

        var says = new TextBlock
        {
            Text = In(MeetingWords.Status(screen.Owed.Status)),
            Style = Chrome(running ? "TheWorkRuns" : "TheWorkWaits"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var ring = new ProgressRing { IsActive = true, Style = Chrome("TheWorkRing"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(ring, "work-ring");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(ring);
        row.Children.Add(says);
        return row;
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
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        AfterWriting();
    }

    /// <summary>Somebody asked a summarised meeting for another summary. The one call that queues it.</summary>
    private void SummariseAgain()
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            new MeetingWork(context, TimeProvider.System).SummariseAgain(meeting);
        }
        catch (MeetingStageException stale)
        {
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        AfterWriting();
    }

    /// <summary>Somebody asked to stop a summary that is running. The one call that stops it.</summary>
    private void StopTheSummary()
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            new MeetingWork(context, TimeProvider.System).StopTheSummary(meeting);
        }
        catch (MeetingStageException stale)
        {
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        AfterWriting();
    }

    /// <summary>
    /// What every press on this screen that writes and then redraws shares: the message a refusal
    /// left is carried across the redraw, because <see cref="Draw"/> clears it on the way in.
    /// </summary>
    private void AfterWriting()
    {
        var said = _status.Line;
        Draw(theRecordingToo: false);
        _status.KeepsWhatWasSaid(said);

        if (_status.IsSaying)
        {
            ShowTheDataLine();
        }
    }

    // ── The name ──────────────────────────────────────────────────────────────────────────────

    private void OnNameLeft(object sender, RoutedEventArgs e)
    {
        // A field that was never shown is not being left; and one whose write was refused stays,
        // with the refusal on the data line, rather than losing what was typed.
        if (_renaming && CommitTheName())
        {
            SwapToTheTitle();
        }
    }

    /// <summary>Whether the name on screen is the name in the corpus.</summary>
    private bool TheNameIsWritten => _read is null
        || string.Equals(NameBox.Text ?? string.Empty, _nameAsRead, StringComparison.Ordinal);

    private void OnNameKey(object sender, KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;

            if (CommitTheName())
            {
                SwapToTheTitle();
            }
        }
        else if (e.Key is VirtualKey.Escape)
        {
            // Back to what was there, written nowhere: the field is put back first, so the leave
            // that swapping it out raises finds nothing changed.
            e.Handled = true;
            NameBox.Text = _nameAsRead;
            SwapToTheTitle();
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
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            ShowTheDataLine();
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
            ShowTheDataLine();
        }

        return false;
    }

    // ── The player ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the recording, or says why it will not play.
    /// </summary>
    /// <remarks>
    /// Whether there is a player at all is <see cref="MeetingScreen.MayBePlayedBack"/>'s and turns
    /// on the file alone — not the stage, and so no transcription, no job, no price. Why the stage
    /// is the wrong thing to read that off is argued where the property is, and is deliberately not
    /// restated here: this remark stated it backwards once, and re-deriving an argument another
    /// type owns is how it got the chance to. What can still go wrong after that is the file or the
    /// machine, and both of those are said where the player would have been rather than left as a
    /// play button that does nothing.
    /// </remarks>
    private void OpenTheRecording(MeetingAsRead read)
    {
        if (!read.Screen.MayBePlayedBack || read.Audio is not { } recording)
        {
            // The second half is a disagreement the corpus side cannot produce — it decides both
            // off the same two reads — so it falls to the sentence that would be true if it ever
            // did: the corpus says there is a recording here and this screen has no file.
            SayThePlayerWillNot(In(WhyItWillNotPlay(read.Screen.TheRecording)
                ?? UiTexts.TheRecordingFileIsMissing));

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

        // The slider outlives the recordings it is set for, so a meeting opened after one turned
        // down is not played at full volume while the slider says otherwise.
        _playing.Volume = (float)(VolumeSlider.Value / 100);
        ShowTheVolume();

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

        var says = In(playing.IsPlaying ? UiTexts.Pause : UiTexts.Play);
        PlayGlyph.Glyph = playing.IsPlaying ? "\uE769" : "\uE768";
        AutomationProperties.SetName(PlayButton, says);
        ToolTipService.SetToolTip(PlayButton, says);
        AtText.Text = ScreenNumbers.Long(playing.At);

        _movingTheTrack = true;
        Track.Value = Math.Clamp(playing.At.Milliseconds, Track.Minimum, Track.Maximum);
        _movingTheTrack = false;
    }

    private void OnVolumeMoved(object sender, RangeBaseValueChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.NewValue > 0)
        {
            _volumeBeforeMute = e.NewValue;
        }

        if (_playing is { } playing)
        {
            playing.Volume = (float)(e.NewValue / 100);
        }

        ShowTheVolume();
    }

    /// <summary>
    /// A press on the speaker mutes, or brings back the level it was muted from.
    /// </summary>
    private void OnMuteOrUnmute(object sender, RoutedEventArgs e) =>
        VolumeSlider.Value = VolumeSlider.Value > 0 ? 0 : _volumeBeforeMute;

    private void OnVolumeAreaEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerIsOverTheVolume = true;
        ShowTheVolume();
    }

    private void OnVolumeAreaExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerIsOverTheVolume = false;
        ShowTheVolume();
    }

    private void OnVolumeFocusChanged(object sender, RoutedEventArgs e) => ShowTheVolume();

    private void OnVolumePressed(object sender, PointerRoutedEventArgs e)
    {
        _volumeIsBeingDragged = true;
        ShowTheVolume();
    }

    private void OnVolumeLetGo(object sender, PointerRoutedEventArgs e)
    {
        _volumeIsBeingDragged = false;
        ShowTheVolume();
    }

    /// <summary>
    /// Draws the volume: the glyph for the level, its name and tooltip for what a press would do,
    /// and the slider only while the pointer is over the volume or either part has the keyboard.
    /// </summary>
    /// <remarks>
    /// Never hidden while the slider holds the pointer's capture: a drag that wanders off the
    /// glyph is still a drag, and closing the slider under the thumb would end it. A slider's own
    /// thumb handles its pointer events, so the press and the release are listened to for handled
    /// events too.
    /// </remarks>
    private void ShowTheVolume()
    {
        // The slider's starting value is set while the markup is still being built, before the glyph
        // beside it exists.
        if (VolumeSlider is null || VolumeGlyph is null || VolumeButton is null)
        {
            return;
        }

        var level = VolumeSlider.Value;

        VolumeGlyph.Glyph = level switch
        {
            <= 0 => "",
            <= 66 => "",
            <= 133 => "",
            _ => "",
        };

        var says = In(level > 0 ? UiTexts.Mute : UiTexts.Unmute);
        AutomationProperties.SetName(VolumeButton, says);
        ToolTipService.SetToolTip(VolumeButton, says);

        var open = _pointerIsOverTheVolume
            || _volumeIsBeingDragged
            || HasTheKeyboard(VolumeSlider)
            || HasTheKeyboard(VolumeButton);

        VolumeSlider.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    // A click leaves pointer focus on the glyph, which is not the keyboard being there and must not
    // keep the slider open once the pointer has left.
    private static bool HasTheKeyboard(Control control) =>
        control.FocusState is FocusState.Keyboard or FocusState.Programmatic;

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

    /// <summary>Leaves the screen, which is the window's app bar's to ask.</summary>
    public void GoBack()
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

    /// <summary>Somebody asked to file this meeting under what it was about.</summary>
    /// <remarks>
    /// The name first, for the reason <see cref="GoBack"/> gives, and then the recording is stopped
    /// where it is: the screen that files this meeting takes the window, and a player left running
    /// behind a collapsed one is sound coming out of an application that appears to be doing
    /// nothing else. It is paused and not closed, because coming back is a redraw.
    /// </remarks>
    private void OnClassify(object sender, RoutedEventArgs e)
    {
        if (!CommitTheName())
        {
            return;
        }

        Pause();

        if (_meeting is { } meeting)
        {
            Classify?.Invoke(this, meeting);
        }
    }

    /// <summary>Somebody asked to name who spoke on this meeting. <see cref="OnClassify"/>'s order.</summary>
    private void OnNameTheVoices(object sender, RoutedEventArgs e)
    {
        if (!CommitTheName())
        {
            return;
        }

        Pause();

        if (_meeting is { } meeting)
        {
            NameTheVoices?.Invoke(this, meeting);
        }
    }

    /// <summary>Somebody asked for the screen that corrects words. <see cref="OnNameTheVoices"/>'s order.</summary>
    private void OnCorrectWords(object sender, RoutedEventArgs e) => AskToCorrect(null);

    /// <summary>
    /// Asks the window to open the corrections screen over this meeting, with the words that were
    /// selected in its field or with none. <see cref="OnNameTheVoices"/>'s order: the name first,
    /// then the recording stopped where it is.
    /// </summary>
    private void AskToCorrect(string? asWritten)
    {
        if (!CommitTheName())
        {
            return;
        }

        Pause();

        if (_meeting is { } meeting)
        {
            CorrectWords?.Invoke(this, new WordsToCorrect(meeting, asWritten));
        }
    }
}
