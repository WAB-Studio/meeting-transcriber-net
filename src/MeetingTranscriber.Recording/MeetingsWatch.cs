using System.Text;

using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// The one thing that says a list of meetings already on screen has stopped saying what the corpus
/// says.
/// </summary>
/// <remarks>
/// <para>
/// A meeting's stage is not something the corpus announces. It is read off the files a meeting has
/// and the jobs it carries, and both of those move where nothing tells a window: the command line
/// files a response somebody paid for, a second window answers a stage, and — the moment anything
/// runs a job on its own — every meeting that finishes transcribing does it while somebody is
/// looking at the list. So this looks, and it is the only thing that looks. Everywhere else the
/// list is read is the application drawing what it has just done, at once rather than within a
/// look; none of those is a moment picked because the list might have gone stale by then.
/// </para>
/// <para>
/// It is here rather than beside <see cref="MeetingWork"/> because a list of meetings is two lists.
/// The meetings are the corpus's, and the recordings nobody got to stop are the spool folder's —
/// <see cref="WaitingRecordings.In"/> walks a disk, and what a row of one says comes off marks in a
/// folder rather than rows in a table. A watch that could only see the first half would leave a
/// recording that stopped being written saying it is still running, for as long as the window
/// stayed open. This project is the only one that can see both, which is what it is for.
/// </para>
/// <para>
/// What it compares is every fact that decides what a row says it is and what it offers to do about
/// it. A number that only grows while a recording is running is not one of those — what a spool
/// occupies is drawn from the same read and left to it, because telling about it would rebuild
/// every card on the list every couple of seconds for the whole of a meeting, which is the cost
/// this exists to avoid rather than to pay.
/// </para>
/// <para>
/// Looking is a read, and never a notification. A watcher over the corpus's files loses twice: what
/// Windows raises about a SQLite database in WAL is a write rather than a commit, and the loudest
/// writer here is a recording in progress, whose blocks land in the corpus by the hundred while its
/// meeting's row says exactly what it said before. So the answer would still have to be read to be
/// worth drawing. <c>PRAGMA data_version</c> is the third way and it is a gate rather than an
/// answer: it says another connection has committed, which a spool block does on its own, so it
/// would sit in front of this read rather than replace it — and it needs a connection held open for
/// the life of the window to say anything at all. It is worth reaching for when somebody has
/// measured this read and found it costly; nobody has.
/// </para>
/// </remarks>
public sealed class MeetingsWatch : IDisposable
{
    /// <summary>
    /// How long a change made outside the window can go unnoticed.
    /// </summary>
    /// <remarks>
    /// Short enough that a row correcting itself reads as the row correcting itself rather than as
    /// something that happened while somebody was away, and long enough that the corpus is not
    /// being read at the rate a screen is drawn. Nothing on the other side of it is waiting on a
    /// person, so it does not have to be quick — it has to be soon.
    /// </remarks>
    public static readonly TimeSpan HowOften = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Between two facts. One separator and not two, because every row written here has the same
    /// number of fields: a title carrying the character is the only way two different lists could
    /// read alike, and no keyboard puts this one in one.
    /// </summary>
    private const char Between = '\u001f';

    /// <summary>
    /// Held for the whole of a look, and taken by <see cref="Dispose"/> on its way out.
    /// </summary>
    /// <remarks>
    /// Two jobs and they are one lock. A look that outlasts the gap between two of them is not
    /// joined by the next, which would have two threads reading and writing <see cref="told"/> and
    /// telling somebody about a change twice or about neither; and letting go of the watch waits
    /// for a look already running, which is what makes "nothing is told after <see cref="Dispose"/>
    /// returns" a promise rather than a hope. Whoever lets a watch go is usually about to take the
    /// corpus away underneath it, and a look holding a connection into a folder being deleted is
    /// the failure that costs.
    /// </remarks>
    private readonly Lock gate = new();

    private readonly DirectoryInfo corpus;
    private readonly TimeProvider clock;
    private readonly TimeSpan every;

    /// <summary>
    /// What the list is showing, said the way a look says what it read, and <c>null</c> when
    /// nothing read it — the list failed, or the read this made on its own behalf did. Written only
    /// under <see cref="gate"/>, which is what makes it visible to the next look on another thread.
    /// </summary>
    /// <remarks>
    /// <c>null</c> and not an empty string, which is what a corpus holding no meeting really says:
    /// a list that could not be read has to differ from every answer there is, or the one state it
    /// would never be told about again is the empty one. It starts there, because before
    /// <see cref="Start"/> nothing has read anything.
    /// </remarks>
    private string? told;

    private ITimer? timer;
    private bool letGo;

    /// <param name="corpus">The folder the meetings are in, whether or not there is a corpus in it yet.</param>
    /// <param name="clock">What the gap between two looks is measured against.</param>
    public MeetingsWatch(DirectoryInfo corpus, TimeProvider clock)
        : this(corpus, clock, HowOften)
    {
    }

    /// <param name="corpus">The folder the meetings are in, whether or not there is a corpus in it yet.</param>
    /// <param name="clock">What the gap between two looks is measured against.</param>
    /// <param name="every">
    /// How long a change can go unnoticed. <see cref="HowOften"/> is what the application uses; a
    /// probe says its own, because a probe waiting on the answer it is about is a probe that takes
    /// two seconds to say nothing.
    /// </param>
    public MeetingsWatch(DirectoryInfo corpus, TimeProvider clock, TimeSpan every)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(every, TimeSpan.Zero);

        this.corpus = corpus;
        this.clock = clock;
        this.every = every;
    }

    /// <summary>
    /// The corpus says something about the meetings that it did not say when the list last read it.
    /// </summary>
    /// <remarks>
    /// Raised off whichever thread the look ran on, which is never the one a window draws on. A
    /// handler with a screen to change says so to its own dispatcher.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>
    /// Starts looking. Takes what the corpus says now without telling anybody, so the first thing
    /// anybody hears about is a change made after this returned.
    /// </summary>
    /// <remarks>
    /// A corpus that will not open does not stop this. It starts looking anyway, holding that it
    /// has read nothing, and the first look that gets an answer tells — so a launch that began over
    /// an unreadable corpus corrects itself inside one gap rather than for the session. That is
    /// worth more here than anywhere else this reads: this is called from the constructor of the
    /// application's first window, so an exception leaving it is a constructor that never returns
    /// and an application that never opens. Nothing that reads a file off a disk gets to have that.
    /// </remarks>
    /// <exception cref="InvalidOperationException">It was started twice.</exception>
    /// <exception cref="ObjectDisposedException">It was started after being let go of.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(letGo, this);

        if (timer is not null)
        {
            throw new InvalidOperationException("This watch over the meetings is already keeping.");
        }

        lock (gate)
        {
            told = WhatTheCorpusSaysIfItWill();
        }

        timer = clock.CreateTimer(_ => Look(), state: null, every, every);
    }

    /// <summary>
    /// The list has just read the corpus itself, and this is what it read. What is drawn out of
    /// these two is what the list is showing, so this is what a look compares against from here on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change is spent when the list has drawn it, which is what this says: a window that has just
    /// filed a meeting, kept a recording or answered a stage is not told two seconds later about its
    /// own change — which would clear the line saying what the press did, and rebuild every card
    /// under whoever was reading them.
    /// </para>
    /// <para>
    /// The list's own answer and never a read of this class's own, and the difference is the whole
    /// of what this method is for. What is held here has to be <em>what is on screen</em>, because
    /// that is what <see cref="Changed"/> means: a second read taken a moment after the list's would
    /// hold whatever landed in between, mark it as shown, and never tell anybody about it — the row
    /// wrong for the rest of the session, which is the failure this class exists to end. Being
    /// handed less than the corpus now holds is the harmless direction and costs one re-read; being
    /// handed more is the claim.
    /// </para>
    /// <para>
    /// So there is nothing to fail here and nothing to wait for: no connection is opened, and the
    /// only thing held is the same short lock a look takes. That matters because the thread saying
    /// this is the thread the window draws on, in the middle of a rebuild of the list.
    /// </para>
    /// </remarks>
    /// <param name="meetings">The meetings the list drew, in the order it drew them.</param>
    /// <param name="waiting">The recordings nobody stopped that it drew above them.</param>
    public void TheListHasRead(
        IReadOnlyList<MeetingAndWork> meetings, IReadOnlyList<WaitingRecording> waiting)
    {
        ArgumentNullException.ThrowIfNull(meetings);
        ArgumentNullException.ThrowIfNull(waiting);

        var said = WhatThatSays(meetings, waiting);

        lock (gate)
        {
            told = said;
        }
    }

    /// <summary>
    /// The list tried to read the corpus and could not. This forgets, so the next look tells again
    /// and the list gets another go.
    /// </summary>
    /// <remarks>
    /// The one way out of a read that failed: the corpus is fine, one connection was unlucky, and
    /// without this the sentence saying so would stay on screen until something else in the corpus
    /// changed. Separate from <see cref="TheListHasRead"/> rather than a <c>false</c> handed to it,
    /// because a read that did not happen has nothing to hand over — a caller passing two empty
    /// lists would be saying the corpus is empty, which is the one answer that must not be
    /// confused with not having got one.
    /// </remarks>
    public void TheListCouldNotRead()
    {
        lock (gate)
        {
            told = null;
        }
    }

    /// <summary>
    /// Stops looking, and waits for a look already running.
    /// </summary>
    /// <remarks>
    /// The wait is what makes this worth calling: it is a small read of a small database, and on
    /// the other side of it whoever let go is free to take the corpus away. What may still reach a
    /// handler is the raise of a look that was already inside one when this was called — the window
    /// that let go says so to itself as well, because that is true of every answer arriving from off
    /// its drawing thread.
    /// <para>
    /// The flag is set before the timer is let go of, and it is what makes the wait mean anything:
    /// letting a timer go does not call back a callback it has already handed to the pool, so
    /// without the flag a look could start on the far side of this and open a connection into the
    /// folder whoever let go is deleting. <see cref="Look"/> reads it under the gate this holds.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        letGo = true;
        timer?.Dispose();

        lock (gate)
        {
        }
    }

    /// <summary>One look, and the telling if there is anything to tell.</summary>
    /// <remarks>
    /// <para>
    /// Nothing escapes it, and that is wider than <see cref="ScreenFailures.Reportable"/> on
    /// purpose. This runs on a timer's callback, where an exception nobody observes ends the process
    /// — with whatever meeting was being recorded — and there is no window on this thread to say
    /// anything in, so even the defect that ought to be loud is worth more absorbed here. It is the
    /// same call <c>MeetingsDrawer.ReadWhatSurvived</c> makes for the same reason. What it costs is
    /// real and is stated rather than argued away: a defect that makes a read throw for good leaves
    /// this looking and never telling, and the list stops correcting itself for the session. This is
    /// the only place in this class that swallows one, which is what keeps that cost to one place.
    /// </para>
    /// <para>
    /// With the read behind <see cref="WhatTheCorpusSaysIfItWill"/>, what this catch is really left
    /// holding is the telling — and a telling that failed has to leave this not knowing rather than
    /// believing. A change marked told that nobody was told about is the one that never arrives:
    /// the next look would find the corpus saying exactly what this already holds, stay quiet, and
    /// the row would sit wrong until something else in the corpus moved.
    /// </para>
    /// </remarks>
    private void Look()
    {
        if (!gate.TryEnter())
        {
            return;
        }

        try
        {
            if (letGo)
            {
                // A callback the pool was already holding when the timer was let go of. Nothing is
                // told after `Dispose` returns, and nothing opens a connection into a folder whose
                // owner has been told it may take it away.
                return;
            }

            var says = WhatTheCorpusSaysIfItWill();
            if (says is null || says == told)
            {
                // A read that would not answer is not a change. What was said last stands, so the
                // next look asks again rather than reporting one nothing established — and the
                // list, which is showing what it read for itself, is left alone over a single
                // unlucky connection.
                return;
            }

            told = says;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception thrown) when (Absorbable(thrown))
        {
            _ = thrown;

            // Forgotten and not kept. This is reached with `told` already moved on, so keeping it
            // would be this holding a change it never managed to pass on.
            told = null;
        }
        finally
        {
            gate.Exit();
        }
    }

    /// <summary>
    /// True when carrying on is the lesser harm. The one that is not is a heap that is gone, where
    /// the next look would be built out of the same exhaustion — the same rule, spelled the same
    /// way, as the two per-item sweeps in <c>Processing</c> and the one in
    /// <see cref="MeetingsNobodyRecorded"/>.
    /// </summary>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;

    /// <summary>What the corpus says, or nothing at all when it would not be read.</summary>
    /// <remarks>
    /// <para>
    /// A corpus that refuses never leaves this class, on either of the two threads that ask one for
    /// an answer. A look runs on a timer's callback, where an escape ends the process with whatever
    /// meeting was being recorded. <see cref="Start"/> runs inside the constructor of the
    /// application's first window, where an escape is an application that never opens.
    /// Both are reading something that belongs to the machine rather than to this program: a corpus
    /// another process has open, a spool folder somebody discarded from a prompt while the walk was
    /// inside it, a volume that went away.
    /// </para>
    /// <para>
    /// <see cref="ScreenFailures.Reportable"/> and not everything, which is the whole difference
    /// between this and <see cref="Look"/>'s own catch below. Those are the failures that are
    /// somebody's circumstance and that a screen already has a sentence for. A defect is this
    /// program being wrong, and it goes straight through here: the same read the list makes for
    /// itself is a moment away on every one of the three paths, on a thread that has a window, and
    /// stopping there is worth more than two seconds' quiet and a list that is confidently wrong.
    /// </para>
    /// <para>
    /// Nothing rather than an empty answer, which is what a corpus holding no meeting says: a read
    /// that did not happen has to differ from every answer there is, or the one state the list would
    /// never be told about again is the empty one.
    /// </para>
    /// </remarks>
    private string? WhatTheCorpusSaysIfItWill()
    {
        try
        {
            return WhatTheCorpusSays();
        }
        catch (Exception thrown) when (ScreenFailures.Reportable(thrown))
        {
            _ = thrown;
            return null;
        }
    }

    /// <summary>What the corpus says about the meetings on the list, and the recordings above them.</summary>
    /// <remarks>
    /// A folder with no corpus in it says what a corpus holding no meeting says, and it is the one
    /// place the two are not told apart. That is the state every installation starts in — the first
    /// recording is what makes the corpus — and a list drawn over either of them is the same empty
    /// list, so a separate answer for it would only ever mean this and the list saying the same
    /// state two ways, which is a telling every look for as long as the window stayed open. What a
    /// corpus that <em>was</em> there and is not now has to say to a person is the list's own to
    /// say, off the folder rather than off this.
    /// <para>
    /// Read only, because reading is all this does and <see cref="CorpusDatabase.Open"/> would make
    /// a corpus out of a folder that lost one between the question above and the line below —
    /// somewhere nobody asked for one, from a timer, with no window to say so in.
    /// </para>
    /// </remarks>
    private string WhatTheCorpusSays()
    {
        if (!CorpusDatabase.HoldsACorpus(corpus))
        {
            return string.Empty;
        }

        using var context = CorpusDatabase.OpenReadOnly(corpus);

        return WhatThatSays(
            new MeetingWork(context, clock).Listed(), WaitingRecordings.In(context));
    }

    /// <summary>
    /// What a list of meetings and the recordings above them say, out of the two lists themselves
    /// and nothing else.
    /// </summary>
    /// <remarks>
    /// Nothing is read in here, and that is what lets the list hand back what it drew rather than
    /// have this class read the corpus a second time behind it. Both callers put the same facts in
    /// the same order, because it is the same function.
    /// </remarks>
    private static string WhatThatSays(
        IReadOnlyList<MeetingAndWork> meetings, IReadOnlyList<WaitingRecording> waiting)
    {
        var said = new StringBuilder();

        // What a meeting's row draws, and what it offers. `MeetingsDrawer.Card` and
        // `ScreenNumbers.When` read the name, the instant and the length off the meeting, and
        // everything else on the card off the stage and the standing — `OwedWork`'s other answers
        // are worked out from those two. A line added to that card is a fact added here.
        foreach (var entry in meetings)
        {
            said.Append(entry.Meeting.Id).Append(Between)
                .Append(entry.Meeting.Title).Append(Between)
                .Append(entry.Meeting.StartedAt).Append(Between)
                .Append(entry.Meeting.Duration).Append(Between)
                .Append(entry.Owed.Stage).Append(Between)
                .Append(entry.Owed.Standing).Append(Between);
        }

        // And what a recording nobody stopped says it is. The marks in its folder and the one
        // reason it could not become a meeting, which between them are every arm of
        // `WaitingRows.StandingOf` — and not what its blocks occupy, which grows for the whole of a
        // meeting and would have this telling about a row that is doing exactly what it was doing.
        foreach (var recording in waiting)
        {
            said.Append(recording.Folder.Name).Append(Between)
                .Append(recording.MeetingId).Append(Between)
                .Append(recording.Running).Append(Between)
                .Append(recording.BeingSaved).Append(Between)
                .Append(recording.NothingToDecideYet).Append(Between)
                .Append(recording.Unrecoverable?.Why).Append(Between);
        }

        return said.ToString();
    }
}

