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
/// What the application still owes each meeting: one card per meeting, the stage it is at, and a
/// button naming that stage's action.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing. Every card is a <see cref="OwedWork"/> read out — which stage, which
/// standing, whether the action can be pressed — and this window's whole job is turning that into
/// controls and the two presses back into calls. Which means the half of this screen with rules
/// in it is the half a build agent runs, and the half that needs a window is the half a person
/// presses.
/// </para>
/// <para>
/// It holds no context between presses either. A corpus is opened for each read and each write and
/// let go of, so what is on screen is what is on disk rather than what a long-lived context
/// remembers loading — which is the same reason nothing here caches a stage.
/// </para>
/// </remarks>
public sealed partial class MeetingsWindow : Window
{
    private readonly CorpusFolder _corpus;
    private readonly List<MeetingAndWork> _meetings = [];

    private UiLanguage _language;
    private TextLine? _status;

    public MeetingsWindow(UiLanguage language, CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        // Before InitializeComponent: the bindings in the XAML are read while it runs.
        _language = language;
        _corpus = corpus;

        InitializeComponent();

        ReadIn(language);
    }

    /// <summary>
    /// Reads the whole window in this language: what the XAML bound, the title, every card and the
    /// status line. The cards are built again rather than translated, so nothing on screen is left
    /// in the language before.
    /// </summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();
        Title = UiTexts.Meetings.In(language);

        Load();
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
    public void Report(UiText text)
    {
        _status = TextLine.Says(text);
        Render();
    }

    /// <summary>
    /// What a card says about the stage a meeting has got to.
    /// </summary>
    /// <remarks>
    /// The last arm stops rather than substituting, for the reason
    /// <c>RecordingWindow.SayWhereTheCorpusIs</c> gives about a refusal it has no text for: a
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
    private void Load()
    {
        _meetings.Clear();
        _status = null;

        if (_corpus.Folder is not { } folder)
        {
            // The one case an empty list would be a lie about. A corpus folder is there exactly
            // when nothing refused it, so falling through here would tell somebody whose corpus is
            // unreachable that they have no meetings.
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, _corpus.Path);
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

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    /// <summary>
    /// Everything on screen, built from the meetings last read. Nothing here decides anything: it
    /// is the reading of an answer already given.
    /// </summary>
    private void Render()
    {
        CountText.Text = _meetings.Count == 0
            ? In(UiTexts.NoMeetingsHereYet)
            : UiTexts.SomeAreWaitingToBeTold.In(_language, _meetings.Count(entry => entry.Owed.IsOwed));

        StatusText.Text = _status?.In(_language) ?? string.Empty;

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

        lines.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.Meeting.Title)
                ? In(UiTexts.AMeetingWithoutATitle)
                : entry.Meeting.Title,
            Style = Chrome("MeetingName"),
        });

        // Data and not a sentence, so it reads the same in either language. Written to the minute
        // and never to the second: what tells two meetings apart on a list is which one it was,
        // not how far into a minute it started.
        lines.Children.Add(new TextBlock
        {
            Text = entry.Meeting.StartedAt.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
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
    /// </remarks>
    private UIElement Presses(Guid meeting, JobKind next, OwedWork owed)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

        if (owed.MayBeTaken)
        {
            var take = new Button { Content = In(Action(next)), Style = Chrome("TakeTheStage") };
            take.Click += (_, _) => Answer(meeting, decline: false);
            row.Children.Add(take);
        }

        if (owed.MayBeLeft)
        {
            var leave = new Button { Content = In(UiTexts.Ignore) };
            leave.Click += (_, _) => Answer(meeting, decline: true);
            row.Children.Add(leave);
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
        if (_corpus.Folder is not { } folder)
        {
            // Said, not swallowed. A button that visibly does nothing is worse than one that says
            // the corpus is not reachable, which is what the list already says.
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, _corpus.Path);
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
        // the answer to this press wiped by the list it caused.
        Load();
        _status = said;
        Render();
    }
}
