using System.Globalization;

using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The bottom half of the screen the application opens on: one card per meeting, the stage it is
/// at, and a button naming that stage's action — under a header that raises the list to the whole
/// window and lowers it again.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about a meeting. Every card is a <see cref="OwedWork"/> read out — which
/// stage, which standing, whether the action can be pressed — and this control's whole job is
/// turning that into controls and the two presses back into calls. Which means the half of this
/// screen with rules in it is the half a build agent runs, and the half that needs a window is the
/// half a person presses.
/// </para>
/// <para>
/// It holds no context between presses either. A corpus is opened for each read and each write and
/// let go of, so what is on screen is what is on disk rather than what a long-lived context
/// remembers loading — which is the same reason nothing here caches a stage.
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

    public MeetingsDrawer()
    {
        InitializeComponent();
    }

    /// <summary>The drawer moved between its two positions.</summary>
    public event EventHandler? OpennessChanged;

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
    /// What a text says in the language this screen is being read in. The XAML binds to it, which
    /// is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>
    /// What a card says about the stage a meeting has got to.
    /// </summary>
    /// <remarks>
    /// The last arm stops rather than substituting, for the reason
    /// <c>MainWindow.SayWhereTheCorpusIs</c> gives about a refusal it has no text for: a
    /// stage added to <see cref="MeetingStage"/> and not given a text here would otherwise be
    /// shown to somebody as one of the others — a meeting with no audio reading as one ready to be
    /// paid for. <c>MeetingCardTextTests</c> is what catches it before it can be thrown.
    /// </remarks>
    private static UiText Reached(MeetingStage stage) => stage switch
    {
        MeetingStage.Recording => UiTexts.NoAudioYet,
        MeetingStage.Recorded => UiTexts.Recorded,
        MeetingStage.Transcribed => UiTexts.Transcribed,
        MeetingStage.Summarised => UiTexts.Summarised,
        _ => throw new InvalidOperationException($"This screen has no text for meeting stage '{stage}'."),
    };

    /// <summary>
    /// What a card says about where that stage stands, or nothing when the stage has no action for
    /// anything to be standing over.
    /// </summary>
    /// <remarks>The last arm stops for the reason <see cref="Reached"/> gives.</remarks>
    private static UiText? Standing(StageStanding standing) => standing switch
    {
        StageStanding.Offered => UiTexts.WaitingToBeTold,
        StageStanding.Underway => UiTexts.AlreadyInTheQueue,
        StageStanding.StoppedOnAPerson => UiTexts.StoppedWaitingForAPerson,
        StageStanding.Declined => UiTexts.IgnoredForNow,
        StageStanding.NothingToDo => null,
        _ => throw new InvalidOperationException($"This screen has no text for stage standing '{standing}'."),
    };

    /// <summary>
    /// What the button offering a stage's action says.
    /// </summary>
    /// <remarks>
    /// Only the kinds a stage can offer are here, and the throw is what keeps it that way. The one
    /// this must never grow is <see cref="JobKind.Render"/>: the rendered files cost nothing and
    /// can be made again, so they are never a press, and a screen that had a word for the button
    /// would be one edit from showing it.
    /// </remarks>
    private static UiText Action(JobKind kind) => kind switch
    {
        JobKind.Transcribe => UiTexts.Transcribe,
        JobKind.Extract => UiTexts.Summarise,
        _ => throw new InvalidOperationException($"This screen offers nothing for job kind '{kind}'."),
    };

    /// <summary>
    /// What this screen says rather than throws over. Deliberately not
    /// <see cref="InvalidOperationException"/>, which is caught on its own where it means
    /// something: everywhere else it is a defect, and a screen that swallowed it would leave one
    /// looking like a corpus somebody could not read.
    /// </summary>
    private static bool Reportable(Exception thrown) => thrown
        is IOException
        or UnauthorizedAccessException
        or SqliteException
        or DbUpdateException;

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
            }
            catch (Exception unreadable) when (Reportable(unreadable))
            {
                _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            }
        }

        Render();
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
            : _meetings.Count == 0
                ? In(UiTexts.NoMeetingsHereYet)
                : UiTexts.SomeAreWaitingToBeTold.In(_language, _meetings.Count(entry => entry.Owed.IsOwed));

        MeetingsStatusText.Text = _status?.In(_language) ?? string.Empty;

        Cards.Children.Clear();

        foreach (var entry in _meetings)
        {
            Cards.Children.Add(Card(entry));
        }
    }

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

        lines.Children.Add(new TextBlock
        {
            Text = named ? entry.Meeting.Title : In(UiTexts.AMeetingNobodyHasNamed),
            Style = Chrome(named ? "MeetingName" : "MeetingUnnamed"),
        });

        // Data and not a sentence, so it reads the same in either language. Written to the minute
        // and never to the second: what tells two meetings apart on a list is which one it was,
        // not how far into a minute it started. The length comes after it where there is one — a
        // meeting still being recorded, and one whose recording never finished, have none.
        lines.Children.Add(new TextBlock
        {
            Text = When(entry.Meeting),
            Style = Chrome("MeetingWhen"),
        });

        lines.Children.Add(new TextBlock
        {
            Text = In(Reached(entry.Owed.Stage)),
            Style = Chrome("MeetingLine"),
        });

        if (Standing(entry.Owed.Standing) is { } standing)
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
    /// When the meeting was and how long it ran, as one line of data: it is the machine's own
    /// numbers and reads the same in either language.
    /// </summary>
    private static string When(Meeting meeting)
    {
        var started = meeting.StartedAt.Value.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        return meeting.Duration is { } length
            ? $"{started} · {length.ToTimeSpan().ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)}"
            : started;
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
            var take = new Button { Content = In(Action(next)), Style = Chrome("TakeTheStage") };
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
        catch (Exception refused) when (Reportable(refused))
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
}
