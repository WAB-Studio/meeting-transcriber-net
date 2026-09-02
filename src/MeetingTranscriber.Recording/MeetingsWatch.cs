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

    /// <summary>What a folder holding no corpus says, which is not what a corpus holding no meeting says.</summary>
    private const string NoCorpusHere = "no corpus";

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
    /// What the corpus said the last time the list was known to be showing it, and <c>null</c> when
    /// nothing read it — the list failed, or the read this made on its own behalf did. Written only
    /// under <see cref="gate"/>, which is what makes it visible to the next look on another thread.
    /// </summary>
    /// <remarks>
    /// <c>null</c> and not an empty string, which is what a corpus holding no meeting really says:
    /// a list that could not be read has to differ from every answer there is, or the one state it
    /// would never be told about again is the empty one.
    /// </remarks>
    private string? told = string.Empty;

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
    /// The list has just read the corpus itself, and this is whether that read went through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both answers are here because both are about the same thing: a change is spent when the list
    /// has drawn it, and not when this managed to read it. Told that a read went through, this
    /// takes what the corpus says now and keeps it, so a window that has just filed a meeting, kept
    /// a recording or answered a stage is not told two seconds later about its own change — which
    /// would clear the line saying what the press did, and rebuild every card under whoever was
    /// reading them.
    /// </para>
    /// <para>
    /// Told that it did not, this forgets, so the next look tells again and the list gets another
    /// go. That is the one way out of a read that failed: the corpus is fine, one connection was
    /// unlucky, and without this the sentence saying so would stay on screen until something else
    /// changed.
    /// </para>
    /// <para>
    /// It reads the corpus again rather than being handed what the list read, and the difference is
    /// the point: the list read through its own connection at its own moment, and what this has to
    /// hold is what a look will find next. Taking the list's own answer would leave this claiming to
    /// have seen a state it never read.
    /// </para>
    /// <para>
    /// Which means there are two reads a press away from each other, and the second one can fail on
    /// its own. When it does, this lands where being told the list's read failed lands — forgetting,
    /// so the next look tells again — and nothing comes back out of here, because the thread saying
    /// this is the thread the window draws on.
    /// </para>
    /// </remarks>
    /// <param name="itWentThrough">Whether the list managed to read the corpus.</param>
    public void TheListHasRead(bool itWentThrough)
    {
        lock (gate)
        {
            told = itWentThrough ? WhatTheCorpusSaysIfItWill() : null;
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
    /// A corpus that refuses never leaves this class, on any of the three threads that ask one for
    /// an answer. A look runs on a timer's callback, where an escape ends the process with whatever
    /// meeting was being recorded. <see cref="Start"/> runs inside the constructor of the
    /// application's first window, where an escape is an application that never opens.
    /// <see cref="TheListHasRead"/> runs on the thread that window draws on, in the middle of a
    /// rebuild of the list. And all three are reading something that belongs to the machine rather
    /// than to this program: a corpus another process has open, a spool folder somebody discarded
    /// from a prompt while the walk was inside it, a volume that went away.
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
    /// A folder with no corpus in it is one of the answers rather than an absence of one, and it is
    /// not the answer a corpus holding no meeting gives. That is the state every installation starts
    /// in — the first recording is what makes the corpus — and it is also what a folder on a volume
    /// somebody unplugged becomes, which is the opposite fact and has to be able to reach a screen.
    /// </remarks>
    private string WhatTheCorpusSays()
    {
        if (!CorpusDatabase.HoldsACorpus(corpus))
        {
            return NoCorpusHere;
        }

        using var context = CorpusDatabase.Open(corpus);
        var said = new StringBuilder();

        // What a meeting's row draws, and what it offers. `MeetingsDrawer.Card` and
        // `ScreenNumbers.When` read the name, the instant and the length off the meeting, and
        // everything else on the card off the stage and the standing — `OwedWork`'s other answers
        // are worked out from those two. A line added to that card is a fact added here.
        foreach (var entry in new MeetingWork(context, clock).Listed())
        {
            said.Append(entry.Meeting.Id).Append(Between)
                .Append(entry.Meeting.Title).Append(Between)
                .Append(entry.Meeting.StartedAt).Append(Between)
                .Append(entry.Meeting.Duration).Append(Between)
                .Append(entry.Owed.Stage).Append(Between)
                .Append(entry.Owed.Standing).Append(Between);
        }

        // And what a recording nobody stopped says it is. Three marks in a folder, each of which
        // changes what the row above the meetings offers — and not what its blocks occupy, which
        // grows for the whole of a meeting and would have this telling about a row that is doing
        // exactly what it was doing.
        foreach (var waiting in WaitingRecordings.In(context))
        {
            said.Append(waiting.Folder.Name).Append(Between)
                .Append(waiting.MeetingId).Append(Between)
                .Append(waiting.Running).Append(Between)
                .Append(waiting.BeingSaved).Append(Between)
                .Append(waiting.NothingToDecideYet).Append(Between);
        }

        return said.ToString();
    }
}

