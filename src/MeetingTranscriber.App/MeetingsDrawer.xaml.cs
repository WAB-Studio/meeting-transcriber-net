using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

// WinUI has a Duration of its own — an animation's, in ticks — and both meanings are in scope
// here. Aliased rather than qualified at the use, for the reason `MainWindow` gives.
using Duration = MeetingTranscriber.Domain.Time.Duration;

namespace MeetingTranscriber.App;

/// <summary>
/// The bottom half of the screen the application opens on: one card per meeting and per recording
/// nobody got to stop, what each has got to, and the buttons naming what may be done about it —
/// under a header that raises the list to the whole window and lowers it again.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about a meeting or a recording. Every card is a <see cref="OwedWork"/> or a
/// <see cref="WaitingRow"/> read out — which stage, which standing, which presses — and this
/// control's whole job is turning that into controls and the presses back into calls. Which means
/// the half of this screen with rules in it is the half a build agent runs, and the half that
/// needs a window is the half a person presses. Two rules are still this file's, and both are
/// about the list rather than about a row: the recordings go above the meetings, and a recording
/// is drawn instead of the meeting card for the meeting it is of.
/// </para>
/// <para>
/// It opens no corpus it does not let go of. A corpus is opened for each read and each write and
/// closed again, so what is on screen is what is on disk rather than what a long-lived context
/// remembers loading — which is the same reason nothing here caches a stage. What it does keep is
/// what a read through a recording's blocks cost: that is a pass over every byte, and it is the
/// one answer here nothing else can hand back.
/// </para>
/// <para>
/// One press outlives the handler that made it, and it is the same minutes of work stopping a
/// meeting is: keeping a recording. What is not here is the recorder's own protection for that —
/// the window refuses to let go of anything while its own press is in flight, and it has no such
/// state for this one. A process that goes during a keep leaves whatever of the finish committed,
/// which is the audio filed and possibly a capture run never marked as recovered.
/// </para>
/// <para>
/// The one thing it owns beyond the list is which of its two positions it is in. That is one fact
/// and it lives here, where the control that changes it is; what the rest of the window does about
/// it — the recording card sliding out of the way — is the window's, told through
/// <see cref="OpennessChanged"/>.
/// </para>
/// </remarks>
public sealed partial class MeetingsDrawer : UserControl
{
    private readonly List<MeetingAndWork> _meetings = [];

    /// <summary>
    /// What each waiting recording turned out to be worth, by the folder holding it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept rather than asked for while a card is being built, because asking costs a pass over
    /// every byte of the recording — a few hundred megabytes a source for two hours of meeting.
    /// It is safe to keep for the same reason: once nothing is writing them, the blocks are what
    /// they are, so an answer read an hour ago is the answer now.
    /// </para>
    /// <para>
    /// A folder is in here exactly when its read came back with something. One whose read refused
    /// is in <see cref="_wouldNotRead"/> instead, and <see cref="ReadThrough"/> asks both: what a
    /// row's answers wait on is the read being over, never on its having worked.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, WhatSurvived> _survived =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which folders have already been handed to a read, so none is read twice.</summary>
    private readonly HashSet<string> _asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Which folders were read and would not come back, for as long as the application is open.
    /// </summary>
    /// <remarks>
    /// Held here and handed to <see cref="WaitingRows.Of"/> on every build, because
    /// <see cref="Read"/> builds the rows out of the corpus again each time anything happens to
    /// the list — and the corpus cannot know this. Forgotten between builds, the row would go back
    /// to offering both answers with no length on it, which is the decide-blind the read-first
    /// rule exists to prevent, and nothing would find out again because <see cref="_asked"/>
    /// already holds the folder.
    /// </remarks>
    private readonly HashSet<string> _wouldNotRead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where the meetings are, handed over once by the window that holds this. Not a constructor
    /// parameter because the XAML above declares this control, and what XAML constructs takes no
    /// arguments — so <see cref="Open"/> is the seam instead, and it is called exactly once.
    /// </summary>
    private CorpusFolder? _corpus;

    private UiLanguage _language;
    private TextLine? _status;

    /// <summary>
    /// Whether the recorder above is in a state that lets this take the whole window. Held rather
    /// than asked, because the answer is the recorder's and this control knows nothing about a
    /// meeting; it opens as no, which is what a drawer that has not been told yet must be.
    /// </summary>
    private bool _mayTakeTheWholeWindow;

    /// <summary>
    /// The meeting the recorder above is saving right now, when it is saving one.
    /// </summary>
    /// <remarks>
    /// Held rather than read out of the corpus, and that is the whole of why the recorder has to
    /// say. A meeting being saved is a row with no audio filed yet, which is exactly what a
    /// recording that never finished is — so the corpus cannot tell the two apart, and a list
    /// reading it alone would put "no audio yet, it is being recorded or its recording never
    /// finished" on the meeting somebody stopped four seconds ago.
    /// </remarks>
    private Guid? _beingSaved;

    /// <summary>
    /// The recordings this corpus is holding that nobody got to stop, as the rows they are drawn
    /// as. Empty until the first read, which is what a drawer that has not read yet must show.
    /// </summary>
    private IReadOnlyList<WaitingRow> _waiting = [];

    /// <summary>
    /// The folder of the recording being kept right now, when one is being kept.
    /// </summary>
    /// <remarks>
    /// The folder and not a flag, because what a keep in flight rules out is an answer about
    /// <em>that</em> recording — it is already being decided, and a second press over the top of
    /// it would be somebody deciding twice. Every other row on the list is about a different
    /// folder and stays answerable: keeping one recording takes minutes, and a list that went
    /// blank for all of them would read as broken over a recording somebody is not looking at.
    /// <para>
    /// The keep on the other rows goes with it, and only the keep. Two keeps at once are two
    /// long corpus writes racing for one status line, so the list takes one at a time — shown as
    /// the press not being there rather than as a button that does nothing, which is the same
    /// thing <see cref="Answers"/> does with a row that has not been read through.
    /// </para>
    /// </remarks>
    private string? _keeping;

    /// <summary>
    /// Whether the window holding this has closed. Read after every await, because keeping a
    /// recording is minutes of work and the window it started in may be gone by the time it comes
    /// back — and drawing into a closed one is the one thing that must not happen then.
    /// </summary>
    private bool _closed;

    public MeetingsDrawer()
    {
        InitializeComponent();
    }

    /// <summary>The drawer moved between its two positions.</summary>
    public event EventHandler? OpennessChanged;

    /// <summary>
    /// Somebody asked to open one of the meetings on this list.
    /// </summary>
    /// <remarks>
    /// Said and not done. Which room a meeting is read in, and what happens to the recorder above
    /// while it is, is the window's — this list knows only that a meeting was chosen, which is the
    /// same shape <see cref="OpennessChanged"/> takes for the same reason.
    /// </remarks>
    public event EventHandler<Guid>? MeetingChosen;

    /// <summary>Whether the list has the whole window rather than the half under the recorder.</summary>
    public bool HasTheWholeWindow { get; private set; }

    /// <summary>
    /// Hands over the corpus these meetings are in. Reads nothing: the window says which language
    /// it is being read in immediately afterwards, and that is what fills the list.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    /// <remarks>
    /// Once, and loudly if not. A second corpus handed to a drawer already showing one is two
    /// answers to the question the application cannot be wrong about, which <c>App</c> says is
    /// answered before any window opens.
    /// </remarks>
    public void Open(CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The meetings drawer already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>
    /// Reads the whole drawer in this language: what the XAML bound, the header, every card and the
    /// status line. The cards are built again rather than translated, so nothing on screen is left
    /// in the language before.
    /// </summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();
        ShowWhichPositionItIsIn();

        Read();
    }

    /// <summary>
    /// Whether the list may take the whole window, which is not its own to decide: what would go
    /// with the recorder above is stop and every line a narrator is told about, so the recorder
    /// answers it and this acts on the answer.
    /// </summary>
    /// <remarks>
    /// An offer withdrawn from a drawer that already has the window puts it back down, and that is
    /// the half that matters. Leaving it up and merely refusing the next press would hold the rule
    /// at the door only: the recorder above says no from the moment a meeting starts, and a list
    /// still covering the window at that moment is the failure the rule is about rather than a
    /// press away from it. Today nothing can reach that — record is inside the half a raised
    /// drawer collapses, so a meeting cannot begin from up here — and a rule that holds because of
    /// where a button happens to sit is one screen change from not holding.
    /// </remarks>
    public void OfferTheWholeWindow(bool offered)
    {
        _mayTakeTheWholeWindow = offered;

        if (!offered && HasTheWholeWindow)
        {
            HasTheWholeWindow = false;
            ShowWhichPositionItIsIn();
            OpennessChanged?.Invoke(this, EventArgs.Empty);
        }

        OpennessButton.IsEnabled = offered || HasTheWholeWindow;
    }

    /// <summary>
    /// Which meeting the recorder above is saving, or nothing when it is saving none. Told and
    /// never worked out here, for the reason <see cref="_beingSaved"/> gives.
    /// </summary>
    /// <remarks>
    /// It draws nothing, which is not an omission. Both ends of a save are moments the list has to
    /// be read again anyway — a meeting arriving in it, and one finished in it — so the window
    /// says this and then reads, and a draw here would be a second rebuild of every card for one
    /// press. It also keeps this off the path a window closed mid save takes, where drawing is the
    /// one thing that must not happen.
    /// </remarks>
    public void BeingSavedNow(Guid? meeting) => _beingSaved = meeting;

    /// <summary>
    /// The window holding this has closed, so nothing here draws again.
    /// </summary>
    /// <remarks>
    /// Told rather than found out, for the reason <see cref="BeingSavedNow"/> is: what a control
    /// can observe about a window closing is <c>Unloaded</c>, which is about being taken out of a
    /// tree and would say the same thing about a re-parenting that is not a close at all. Keeping
    /// a recording is the one thing here that outlives the press that started it, so the one
    /// answer this needs is the window's own.
    /// </remarks>
    public void Closing() => _closed = true;

    /// <summary>
    /// What a text says in the language this screen is being read in. The XAML binds to it, which
    /// is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>
    /// What a row says about a recording nobody got to stop, and which of the three surfaces it
    /// says it on.
    /// </summary>
    /// <remarks>
    /// One table and not two, because the two answers are one statement: the tint is what says a
    /// row is waiting on the person, and a sentence that said so over a surface that did not would
    /// be the screen contradicting itself. It stays here rather than moving to
    /// <see cref="MeetingWords"/> with the stage tables, because the surface it names is one of
    /// this screen's own styles and no other screen declares them. The last arm stops rather than
    /// substituting, for the reason that class gives; <c>MeetingCardTextTests</c> is what catches
    /// a standing with no answer here before it can be thrown.
    /// </remarks>
    private static (UiText Says, string Surface) Reads(WaitingStanding waiting) => waiting switch
    {
        WaitingStanding.StillBeingRecorded => (UiTexts.ItIsBeingRecordedRightNow, "MeetingCard"),
        WaitingStanding.BeingSavedNow => (UiTexts.ThisOneIsBeingSaved, "MeetingCard"),
        WaitingStanding.Waiting => (UiTexts.TheApplicationClosedInTheMiddleOfThisOne, "WaitingOnADecision"),
        WaitingStanding.CannotBecomeAMeeting => (UiTexts.ThisCannotBecomeAMeeting, "SomethingIsLost"),
        WaitingStanding.CouldNotBeReadThrough => (UiTexts.TheBlocksOfThisOneWouldNotRead, "SomethingIsLost"),
        _ => throw new InvalidOperationException(
            $"This screen has no text for a waiting recording that is '{waiting}'."),
    };

    /// <summary>
    /// What a reason a recording is not a meeting reads as. The values its sentence leaves room for
    /// travel with the reason and are not fetched here, for the reason <see cref="NotAMeeting"/>
    /// gives.
    /// </summary>
    /// <remarks>
    /// A table over an enum and nothing else, which is <see cref="Reads"/>'s shape and is here for
    /// the same reason: what a screen says is `UiTexts`', and `UiTexts` is only in reach of the
    /// application. The last arm stops rather than substituting, and <c>MeetingCardTextTests</c> is
    /// what catches a reason with no words here before it can be thrown.
    /// <para>
    /// What no probe reaches is that each entry asks for as many values as its reason carries. A
    /// text wanting more than it is given throws where the card is drawn, which is a red nothing
    /// here can produce: this file has no <c>ProjectReference</c> to it and the switch is read as
    /// source. The five pairs are three-with-none, one-with-one and one-with-two, and they are
    /// checked by reading them.
    /// </para>
    /// </remarks>
    private static UiText Words(WhyNotAMeeting why) => why switch
    {
        WhyNotAMeeting.NothingSaysWhichMeetingItIs => UiTexts.NothingHereSaysWhichMeetingItIs,
        WhyNotAMeeting.WhatItSaysAboutItselfCannotBeRead => UiTexts.WhatItSaysAboutItselfCannotBeRead,
        WhyNotAMeeting.ItIsInAnotherMeetingsFolder => UiTexts.ItIsInAnotherMeetingsFolder,
        WhyNotAMeeting.ThisCorpusHasNoSuchMeeting => UiTexts.ThisCorpusHasNoSuchMeeting,
        WhyNotAMeeting.NotAllOfItsSourcesAreHere => UiTexts.NotAllOfItsSourcesAreHere,
        _ => throw new InvalidOperationException(
            $"This screen has no words for a recording that is not a meeting because '{why}'."),
    };

    /// <summary>
    /// Why this recording is not the meeting it was of, said in the language being read in, or
    /// nothing when it still can be.
    /// </summary>
    private string? Because(WaitingRecording recording) =>
        recording.Unrecoverable is { } reason ? Words(reason.Why).In(_language, [.. reason.Says]) : null;

    /// <summary>
    /// Reads every meeting and what is owed on it, from a corpus opened for this read and let go
    /// of again.
    /// </summary>
    /// <remarks>
    /// A folder with no corpus in it is not an error and is not migrated into one from here. The
    /// first recording makes the corpus, and a screen that made an empty one to list nothing out
    /// of would be making a corpus somewhere a person never asked for one.
    /// </remarks>
    public void Read()
    {
        _meetings.Clear();
        _waiting = [];
        _status = null;

        if (Corpus().Folder is not { } folder)
        {
            // The one case an empty list would be a lie about. A corpus folder is there exactly
            // when nothing refused it, so falling through here would tell somebody whose corpus is
            // unreachable that they have no meetings.
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
        }
        else if (CorpusDatabase.HoldsACorpus(folder))
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                _meetings.AddRange(new MeetingWork(context, TimeProvider.System).Listed());

                // On the same read and out of the same context, because the two are one list. A
                // recording nobody got to stop reads no block here — the folders, their cards and
                // what each occupies, which is what a start can run before anything is on screen.
                _waiting = WaitingRows.Of(
                    WaitingRecordings.In(context), _beingSaved, _wouldNotRead);
            }
            catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
            {
                _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            }
        }

        Render();
        ReadWhatSurvived();
    }

    /// <summary>
    /// Reads through the blocks of every waiting recording somebody has to answer for, off this
    /// thread, and draws again when the answers arrive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How long a recording is, is what somebody decides on, and the only way to know it is to
    /// read every packet of every source — a few hundred megabytes each for two hours of meeting.
    /// So the row is drawn without it and gains it, rather than a start standing still with a
    /// blank window until the disk has been walked.
    /// </para>
    /// <para>
    /// Each folder is handed to a read once ever, whether or not that read came back with a
    /// length. The blocks of a recording nothing is writing do not change, so a second pass could
    /// only produce the same answer at the same cost — and a list that is read again on every
    /// press would otherwise start one on each of them. A folder whose blocks would not read is
    /// still a folder somebody may throw away, and that is what being read once buys it: the row
    /// says the blocks refused and offers the one answer that does not need them, rather than
    /// waiting on a pass that will fail again.
    /// </para>
    /// </remarks>
    private void ReadWhatSurvived()
    {
        var unread = new List<WaitingRow>();
        foreach (var row in _waiting)
        {
            if (row.MayBeReadThrough && _asked.Add(row.Recording.Folder.FullName))
            {
                unread.Add(row);
            }
        }

        if (unread.Count == 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            foreach (var row in unread)
            {
                WhatSurvived? survived = null;

                try
                {
                    survived = row.Recording.Read();
                }
                catch (Exception unreadable)
                {
                    // Every exception, and this is the one place in this file that does not sort
                    // them. Everything inside the try is the engine reading a file that a machine
                    // died in the middle of writing, so there is no defect of this screen's to
                    // stay loud about, and what comes back names a file and an offset rather than
                    // anything a person acts on — the row saying the blocks refused is what they
                    // need, and the standing below is what says it.
                    //
                    // Not `Absorbable(thrown) => thrown is not OutOfMemoryException`, which is the
                    // rule at the two per-item sweeps in `Processing`, and the difference is what
                    // an escape costs rather than what the exception means. There an escape ends
                    // one sweep, reaches a caller and is retried on the next launch. Here `_asked`
                    // claims every folder before this loop starts and the task is nobody's to
                    // observe, so an escape strands every row behind it, silently, for the rest of
                    // the session — with no length and no answers, which is the state this whole
                    // pass exists to get a row out of.
                    _ = unreadable;
                }

                var folder = row.Recording.Folder.FullName;

                // One at a time and not one for the batch: what a row is waiting on is its own
                // blocks, and posting at the end would leave a ten-minute recording unanswerable
                // behind the three-hour one being read after it.
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_closed)
                    {
                        return;
                    }

                    if (survived is null)
                    {
                        // Built again rather than patched, out of the recordings already listed
                        // and never a second corpus read: every standing is settled in one place
                        // and the order comes off it, so a refusal arriving after the list cannot
                        // rewrite one row behind the sort's back.
                        _wouldNotRead.Add(folder);
                        _waiting = WaitingRows.Of(
                            _waiting.Select(other => other.Recording), _beingSaved, _wouldNotRead);
                    }
                    else
                    {
                        _survived[folder] = survived;
                    }

                    Render();
                });
            }
        });
    }

    /// <summary>
    /// Where the meetings are, once the window has said. Every read goes through this rather than
    /// the field, so a drawer asked for its meetings before it was opened stops here and names
    /// that, instead of throwing a <see cref="NullReferenceException"/> naming nothing.
    /// </summary>
    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException("The meetings drawer was read before it was opened.");

    /// <summary>
    /// The header's one press: the list takes the whole window, or gives it back. The same
    /// control in the same place either way, which is what makes it read as one screen moving
    /// rather than as two screens.
    /// </summary>
    /// <remarks>
    /// Asked again here even though the control it comes from was disabled, for the reason every
    /// handler on the recorder above asks again: a click already in flight arrives after that.
    /// Giving the window back is never refused — a drawer that could be raised and not lowered is
    /// the same fault the refusal exists to prevent.
    /// <para>
    /// Raising is also the moment to read the list. It is the one gesture that says somebody is
    /// about to act on what is in it, and everything that changes a meeting's stage — the runner,
    /// the command line, this list's own two buttons — happens where nothing tells this control.
    /// </para>
    /// </remarks>
    private void OnToggleOpenness(object sender, RoutedEventArgs e)
    {
        if (!HasTheWholeWindow && !_mayTakeTheWholeWindow)
        {
            return;
        }

        HasTheWholeWindow = !HasTheWholeWindow;
        ShowWhichPositionItIsIn();

        if (HasTheWholeWindow)
        {
            Read();
        }

        OpennessChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the header's press offers next, which is the other position and never the one the
    /// drawer is in. Set here rather than bound in the XAML because it is the one thing on this
    /// screen whose words change without the language changing.
    /// </summary>
    private void ShowWhichPositionItIsIn() =>
        OpennessButton.Content = In(HasTheWholeWindow
            ? UiTexts.BringTheMeetingsBackDown
            : UiTexts.OpenTheMeetingsWhole);

    /// <summary>
    /// Everything on screen, built from the meetings last read. Nothing here decides anything: it
    /// is the reading of an answer already given.
    /// </summary>
    private void Render()
    {
        // Nothing about how many there are when the corpus would not open or would not be read:
        // an empty list is not the same fact as no meetings, and "there is none here yet" over a
        // corpus nobody reached is the lie Read refuses to tell one line further up.
        CountText.Text = _status is not null
            ? string.Empty
            : _meetings.Count == 0 && _waiting.Count == 0
                ? In(UiTexts.NoMeetingsHereYet)
                : UiTexts.SomeAreWaitingToBeTold.In(
                    _language,
                    _meetings.Count(entry => entry.Owed.IsOwed)
                        + _waiting.Count(row => row.WaitsOnSomebody));

        MeetingsStatusText.Text = _status?.In(_language) ?? string.Empty;

        Cards.Children.Clear();

        // The recordings first, which is the whole of where they go: a recording the application
        // never finished is at the top of this list and nothing says so in words.
        foreach (var row in _waiting)
        {
            Cards.Children.Add(WaitingCard(row));
        }

        // And each of them instead of the meeting it is of, never above it. A recording nobody
        // stopped has had its row in the corpus since before its first sample, so the meeting is
        // already in the list read above — drawing both would put one meeting on the screen twice,
        // once saying it has no audio and once offering to make some of it.
        var drawn = _waiting.Select(row => row.Recording.MeetingId).OfType<Guid>().ToHashSet();

        foreach (var entry in _meetings)
        {
            if (!drawn.Contains(entry.Meeting.Id))
            {
                Cards.Children.Add(Card(entry));
            }
        }
    }

    /// <summary>One recording nobody got to stop, as the row it is offered in.</summary>
    /// <remarks>
    /// It is a meeting's row and reads as one — the same name, the same data line, the same shape —
    /// because that is what it is. What tells it from the meetings under it is the surface it sits
    /// on and the two answers on it, and neither of those is decided here: <see cref="WaitingRow"/>
    /// answers both, in a project a build agent can run.
    /// </remarks>
    private UIElement WaitingCard(WaitingRow row)
    {
        var lines = new StackPanel { Spacing = 4 };
        var (says, surface) = Reads(row.Standing);

        // ISC-165.1 on a row that may not have a meeting at all. A folder the corpus knows nothing
        // about has no name to show and none is invented from the folder it is sitting in.
        var named = row.Recording.Meeting?.Title is { } title && !string.IsNullOrWhiteSpace(title);

        lines.Children.Add(new TextBlock
        {
            Text = named ? row.Recording.Meeting!.Title! : In(UiTexts.AMeetingNobodyHasNamed),
            Style = Chrome(named ? "MeetingName" : "MeetingUnnamed"),
        });

        lines.Children.Add(new TextBlock
        {
            Text = WhatIsThere(row),
            Style = Chrome("MeetingWhen"),
        });

        // The reason rides on the line for the one standing whose sentence leaves room for it, and
        // is ignored by the other three — a recording still being written can have a reason too,
        // and their sentences take no value, so it goes nowhere. It is read in this language here
        // rather than kept: a card is built again from scratch when somebody switches, so nothing
        // holds these words past the switch.
        lines.Children.Add(new TextBlock
        {
            Text = TextLine.Says(says, Because(row.Recording)).In(_language),
            Style = Chrome("MeetingLine"),
        });

        if (Answers(row) is { } answers)
        {
            lines.Children.Add(answers);
        }

        return new Border { Style = Chrome(surface), Child = lines };
    }

    /// <summary>
    /// What is there, as one line of data: when it started, how long it turned out to be, and what
    /// it occupies.
    /// </summary>
    /// <remarks>
    /// The length is missing until the blocks have been read through, and missing rather than
    /// guessed at or shown as a nought — the size beside it is what says there is audio there
    /// while that is happening. A recording that says nothing about when it started shows no date
    /// rather than today's: the corpus is asked first and the folder's own card second, and a
    /// folder neither of them knows is a folder with no date.
    /// </remarks>
    private string WhatIsThere(WaitingRow row)
    {
        var line = new List<string>();

        if ((row.Recording.Meeting?.StartedAt ?? row.Recording.Spooled.Card?.StartedAt) is { } began)
        {
            line.Add(ScreenNumbers.At(began));
        }

        if (_survived.GetValueOrDefault(row.Recording.Folder.FullName) is { } survived)
        {
            line.Add(ScreenNumbers.Long(survived.Length));
        }

        line.Add(string.Create(
            CultureInfo.InvariantCulture, $"{row.Recording.Bytes / 1024d / 1024d:0.0} MB"));

        return ScreenNumbers.Beside([.. line]);
    }

    /// <summary>
    /// The two answers, each on screen only when this row offers it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is <c>docs/design.md</c>'s grammar, the same one the meetings under this follow:
    /// the act on the right and the neutral answer on the left. Throwing a recording away is
    /// neither — it is the press that loses something — so it goes past a gap at the far left,
    /// where it cannot be reached by the reflex that presses the left-hand button.
    /// </para>
    /// <para>
    /// Nothing at all on the row being kept, and that is not the same as the buttons being
    /// disabled: keeping writes a meeting into the corpus over minutes, and what a person should
    /// see in that stretch is a row that is not taking answers, not a row of dead controls. It is
    /// that row and not the list — a recording somebody is not deciding about is not made
    /// undecidable by one they are, and a list that went blank for the minutes a keep runs would
    /// read as broken.
    /// </para>
    /// <para>
    /// What the other rows do lose is the keep, because two of those at once are two long corpus
    /// writes racing for one status line. Losing the press rather than having it refuse is the
    /// same rule as the paragraph below: this screen does not draw an answer it will not take.
    /// </para>
    /// <para>
    /// Nothing either until this recording's blocks have been read through, and that is the one
    /// rule here with a second reason. The first is the person's: how long it turned out to be is
    /// what somebody decides on, and a row offering the answer before it can say that is asking
    /// them to decide blind. The second is the files': a read holds each spool open as it goes,
    /// and Windows will not unlink a file somebody holds — so a throw-away pressed into the middle
    /// of one deletes the source already read and fails on the source still being read, which is
    /// the half-removed folder the engine's own check exists to make impossible. Waiting is what
    /// keeps that check whole rather than working around it from outside.
    /// </para>
    /// </remarks>
    private UIElement? Answers(WaitingRow row)
    {
        var offered = ReadThrough(row) && !BeingKept(row);
        var thrown = offered && row.Allows(WaitingAnswer.Discard);
        var kept = offered && _keeping is null && row.Allows(WaitingAnswer.Keep);

        if (!thrown && !kept)
        {
            return null;
        }

        var answers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };

        if (thrown)
        {
            var discard = new Button
            {
                Content = In(UiTexts.Discard),
                Style = Chrome("ItLosesSomething"),
            };

            discard.Click += (_, _) => Decide(row, WaitingAnswer.Discard);
            answers.Children.Add(discard);
        }

        if (kept)
        {
            var keep = new Button { Content = In(UiTexts.Keep) };
            keep.Click += (_, _) => Decide(row, WaitingAnswer.Keep);
            answers.Children.Add(keep);
        }

        return answers;
    }

    /// <summary>
    /// Whether this recording's own read is over, which is what its answers wait on. Said once,
    /// because the press asks it again after the button carrying it was drawn.
    /// </summary>
    /// <remarks>
    /// Over, and not over with a length: a folder whose blocks refused is one somebody may still
    /// be rid of, and it is nothing holding its files that says so — the read that would have has
    /// already ended. Asking only for a length would leave that row with no press on it for the
    /// rest of the session, which is the one thing worse than no length.
    /// </remarks>
    private bool ReadThrough(WaitingRow row) => !row.MayBeReadThrough
        || _survived.ContainsKey(row.Recording.Folder.FullName)
        || _wouldNotRead.Contains(row.Recording.Folder.FullName);

    /// <summary>Whether this is the row whose keep is running right now.</summary>
    private bool BeingKept(WaitingRow row) => _keeping is { } folder
        && string.Equals(folder, row.Recording.Folder.FullName, StringComparison.OrdinalIgnoreCase);

    /// <summary>One meeting, as the card the task asks for.</summary>
    private UIElement Card(MeetingAndWork entry)
    {
        var lines = new StackPanel { Spacing = 4 };

        // ISC-165.1 on a row. A meeting nobody has named reads as one nobody has named: the
        // catalogue's own words, in the reader's language and greyed the way a caption is, and
        // never a name worked out from the date, the folder or the first thing said in it. There
        // is nothing here to invent one from and that is deliberate — a title is somebody's, and
        // the only thing allowed to fill it in without being asked is the summary when it arrives.
        var named = !string.IsNullOrWhiteSpace(entry.Meeting.Title);

        // The name, and the press that opens the meeting. It is the same control because the row
        // is what you press: `docs/design.md` puts two answers on a row and nothing else, so an
        // open button would have to take the place of one of the two that cost money. What it says
        // is the meeting's own name — or, for one nobody has named, the catalogue's two words,
        // which is ISC-165.1 unchanged: this screen has nothing to invent a name from and is not
        // pretending to.
        var open = new Button
        {
            Content = new TextBlock
            {
                Text = named ? entry.Meeting.Title : In(UiTexts.AMeetingNobodyHasNamed),
                Style = Chrome(named ? "MeetingName" : "MeetingUnnamed"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Style = Chrome("MeetingOpen"),
        };

        // Named rather than left to whatever a button derives from a TextBlock in its content.
        // What is on it is the meeting's own name, and that is what a narrator reads out — which
        // is not something a control template promises.
        AutomationProperties.SetName(
            open,
            named ? entry.Meeting.Title : In(UiTexts.AMeetingNobodyHasNamed));

        // And an id beside it, which is a different question with a different answer. The name is
        // read aloud and is a person's, so it is in their language and half this list has none —
        // every meeting nobody has named reads the same two words, and nothing on the screen can
        // then be told from anything else. The id says which meeting, in every language and for
        // as long as the meeting exists, which is what a tool driving this window needs to press
        // one of twelve rows.
        AutomationProperties.SetAutomationId(open, entry.Meeting.Id.ToString());

        open.Click += (_, _) => MeetingChosen?.Invoke(this, entry.Meeting.Id);
        lines.Children.Add(open);

        // Data and not a sentence, so it reads the same in either language. Written to the minute
        // and never to the second: what tells two meetings apart on a list is which one it was,
        // not how far into a minute it started. The length comes after it where there is one — a
        // meeting still being recorded, and one whose recording never finished, have none.
        lines.Children.Add(new TextBlock
        {
            Text = ScreenNumbers.When(entry.Meeting),
            Style = Chrome("MeetingWhen"),
        });

        // The one line on this list not read out of the corpus. A meeting being saved has no audio
        // filed yet, which is exactly what a recording that never finished looks like from here —
        // so the corpus cannot tell the two apart, and left to it the list would say "no audio
        // yet: it is being recorded, or its recording never finished" about the meeting somebody
        // stopped four seconds ago. Only the line changes: a meeting at that stage has no action
        // and no standing either way, so there is nothing else on the card for this to decide.
        //
        // Almost always this meeting is drawn as its own waiting recording instead — its blocks
        // are in the spool while the save reads them, so it is on the list above and reads the
        // same sentence from there. What reaches this line is the one case that leaves: a spool
        // root that would not be listed, where the meetings were read and the recordings were not.
        lines.Children.Add(new TextBlock
        {
            Text = In(entry.Meeting.Id == _beingSaved
                ? UiTexts.ThisOneIsBeingSaved
                : MeetingWords.Reached(entry.Owed.Stage)),
            Style = Chrome("MeetingLine"),
        });

        if (MeetingWords.Standing(entry.Owed.Standing) is { } standing)
        {
            lines.Children.Add(new TextBlock
            {
                Text = In(standing),
                Style = Chrome(entry.Owed.WaitsOnSomebody ? "MeetingStoppedOnAPerson" : "MeetingLine"),
            });
        }

        if (entry.Owed.Next is { } next && (entry.Owed.MayBeTaken || entry.Owed.MayBeLeft))
        {
            lines.Children.Add(Presses(entry.Meeting.Id, next, entry.Owed));
        }

        return new Border { Style = Chrome("MeetingCard"), Child = lines };
    }

    /// <summary>
    /// One of the styles this screen declares. What a card looks like stays in the XAML, where a
    /// theme brush resolves against the theme, and this file chooses between them by name.
    /// </summary>
    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>
    /// The stage's two answers, each on screen only when it is one somebody may give.
    /// </summary>
    /// <remarks>
    /// They come and go independently, and the one case where they differ is the one worth having
    /// them separate for: work already asked for cannot be asked for twice, and can still be taken
    /// back — so a stage in the queue shows ignore alone. A control that is on screen and dead is
    /// one somebody presses and learns nothing from, so neither of these is ever that.
    /// <para>
    /// The order is <c>docs/design.md</c>'s grammar and not the order they were written in: the
    /// neutral answer is on the left and the act is on the right. Ignoring is the neutral one — the
    /// meeting stays where it is and the same button comes back — and taking the stage is what
    /// opens the charge, so it is the one on the right in every row of this list and on every other
    /// screen. A pair that read the other way round on one screen is where somebody presses the
    /// expensive one out of habit.
    /// </para>
    /// </remarks>
    private UIElement Presses(Guid meeting, JobKind next, OwedWork owed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        if (owed.MayBeLeft)
        {
            var leave = new Button { Content = In(UiTexts.Ignore) };
            leave.Click += (_, _) => Answer(meeting, decline: true);
            row.Children.Add(leave);
        }

        if (owed.MayBeTaken)
        {
            var take = new Button { Content = In(MeetingWords.Action(next)), Style = Chrome("TakeTheStage") };
            take.Click += (_, _) => Answer(meeting, decline: false);
            row.Children.Add(take);
        }

        return row;
    }

    /// <summary>
    /// One of the two presses, and the reason neither checks anything first: what is allowed is
    /// re-read against the corpus inside the call, so a screen drawn before somebody pressed the
    /// same button in another window cannot spend on what it still shows.
    /// </summary>
    private void Answer(Guid meeting, bool decline)
    {
        if (Corpus().Folder is not { } folder)
        {
            // Said, not swallowed. A button that visibly does nothing is worse than one that says
            // the corpus is not reachable, which is what the list already says.
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            Render();
            return;
        }

        TextLine said;

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

            said = TextLine.Says(decline ? UiTexts.ItIsIgnoredForNow : UiTexts.ItIsInTheQueueNow);
        }
        catch (MeetingStageException)
        {
            // The ordinary outcome of a stale screen rather than a failure: the message says so in
            // one language and names a GUID, so what is shown is this window's own sentence and the
            // list underneath it is read again.
            said = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            said = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        // After the re-read, which clears whatever the last one said. Saying it first would leave
        // the answer to this press wiped by the list it caused — and never over what the re-read
        // itself had to say, because a corpus that would not open on the way back is a list that is
        // not what is on disk, and "it is in the queue now" over the top of that reads as a screen
        // with nothing wrong in it.
        Read();
        _status ??= said;
        Render();
    }

    /// <summary>
    /// One of the two answers about a recording nobody got to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked again here even though the button that carries it only exists when the row allows it,
    /// for the reason every handler on the recorder above asks again: a click already in flight
    /// arrives after the screen was redrawn without it.
    /// </para>
    /// <para>
    /// Throwing one away is this thread's and keeping one is not, and the difference is what each
    /// costs. Discarding unlinks a folder. Keeping is the same finish stopping performs — the
    /// blocks poured onto one timeline, read back and hashed — which for a long meeting is minutes,
    /// and the window would be frozen for every one of them.
    /// </para>
    /// <para>
    /// Neither acts on the recording it was handed. Both find it again in a corpus opened for the
    /// answer — by the folder holding it, which is what a decision about a recording is actually
    /// about — and both say the screen was stale rather than acting when it is no longer there.
    /// That is the same re-read the two presses above do, and it is what stops a list drawn ten
    /// minutes ago from throwing away a recording somebody has since kept at a prompt. What it is
    /// not is a guard against the row under the pointer having changed, which is the screen's and
    /// not the corpus's.
    /// </para>
    /// </remarks>
    private async void Decide(WaitingRow row, WaitingAnswer answer)
    {
        // The read guard again, and not only in Answers: a folder that has not been read through
        // is one a read may still be holding open, and a throw-away that arrived anyway would
        // delete one source and fail on the other. Beside it the two the keep in flight rules
        // out — anything about the recording being kept, and a second keep about any of them.
        if (BeingKept(row)
            || (answer == WaitingAnswer.Keep && _keeping is not null)
            || !row.Allows(answer)
            || !ReadThrough(row))
        {
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            // Said, not swallowed, for the reason the presses above say it.
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            Render();
            return;
        }

        var waiting = row.Recording.Folder.FullName;
        TextLine said;

        if (answer == WaitingAnswer.Discard)
        {
            try
            {
                var again = FoundAgain(folder, waiting);

                // The engine's own, which refuses a recording a capture is still writing and asks
                // the file system again before anything goes. The row already said this one may be
                // thrown away; what protects the blocks is that call and never this screen.
                again?.Spooled.Discard();
                said = TextLine.Says(
                    again is null ? UiTexts.ThatIsNoLongerHowItWas : UiTexts.TheRecordingIsGone);
            }
            catch (Exception refused) when (ScreenFailures.Reportable(refused))
            {
                said = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            Read();

            // The keep still running is what the line goes back to saying, and it wins over this
            // press's own answer: a recording thrown away is gone off the list, which says it
            // without a sentence, while a keep has nothing else on screen at all. Whatever the
            // re-read itself had to say still wins over both, for the reason the presses above
            // give.
            _status ??= _keeping is null ? said : TextLine.Says(UiTexts.TheRecordingIsBeingKept);
            Render();
            return;
        }

        // Said before it starts and not after, because for the minutes it runs it is all there is
        // to say a keep is happening. The row it is about loses its answers while it does and the
        // rest lose only their keep — Answers is what says so — so what is drawn is a list still
        // answering about the recordings this press is not about.
        _keeping = waiting;
        _status = TextLine.Says(UiTexts.TheRecordingIsBeingKept);
        Render();

        try
        {
            var kept = await Task.Run(() =>
            {
                using var context = CorpusDatabase.Open(folder);

                var again = WaitingRecordings.In(context).FirstOrDefault(other =>
                    string.Equals(other.Folder.FullName, waiting, StringComparison.OrdinalIgnoreCase));

                if (again is null)
                {
                    return false;
                }

                // The context this is found in and the context it is recovered through are one,
                // which is what the finish needs: it reads the meeting's row back and writes the
                // length onto it.
                WaitingRecordings.Recover(
                    context, again, UtcTimestamp.From(TimeProvider.System.GetUtcNow()));

                return true;
            });

            said = TextLine.Says(kept ? UiTexts.ItIsAMeetingNow : UiTexts.ThatIsNoLongerHowItWas);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            said = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }
        finally
        {
            _keeping = null;
        }

        // The window may have gone while that ran, in which case there is nothing to draw into.
        // What is on disk is the blocks and whatever of the finish committed before the process
        // went, and the next start offers whichever that leaves — see the note on this file's
        // class about what a keep cut off part way still owes.
        if (_closed)
        {
            return;
        }

        Read();
        _status ??= said;
        Render();
    }

    /// <summary>
    /// The recording in <paramref name="waiting"/> as the corpus holds it now, or nothing when it
    /// is no longer one somebody is deciding about.
    /// </summary>
    /// <remarks>
    /// By the folder and not by the meeting, which is the difference between this and the command
    /// line's own re-find: a folder is what a decision on this list is about, and two of the
    /// recordings it can show name no meeting at all.
    /// </remarks>
    private static WaitingRecording? FoundAgain(DirectoryInfo corpus, string waiting)
    {
        using var context = CorpusDatabase.Open(corpus);

        return WaitingRecordings.In(context).FirstOrDefault(other =>
            string.Equals(other.Folder.FullName, waiting, StringComparison.OrdinalIgnoreCase));
    }
}
