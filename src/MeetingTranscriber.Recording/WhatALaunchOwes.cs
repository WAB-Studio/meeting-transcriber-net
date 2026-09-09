using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording;

/// <summary>
/// One piece of work a launch owes the corpus: what it is called, and how it is done.
/// </summary>
/// <param name="Name">
/// What this piece of work is, said the way a person would say it. It is what a line reporting the
/// work is prefixed with, so it names the chore and never the type that does it.
/// </param>
/// <param name="Do">
/// The work, over a corpus root, answering with its own lines about what it did not manage. Every
/// chore in <see cref="WhatALaunchOwes.InOrder"/> already answers instead of throwing and already
/// has a list for that, so this is where the list is handed on rather than dropped.
/// </param>
public sealed record LaunchChore(string Name, Func<DirectoryInfo, IReadOnlyList<string>> Do);

/// <summary>What one launch's work came to: what ran, and what it did not manage.</summary>
/// <param name="Ran">
/// The chores that were reached and came back, in the order they ran. A chore that declined
/// something is here — declining is what a correct chore does — and a chore that threw is not.
/// </param>
/// <param name="Left">
/// One line per thing a launch did not manage, each prefixed with the chore that reports it: what
/// a chore declined, in its own words, plus the message of anything a chore threw. It is the two
/// lists the chores already build, in one place and with a name on each line.
/// </param>
public sealed record LaunchDone(IReadOnlyList<string> Ran, IReadOnlyList<string> Left);

/// <summary>
/// What a launch owes the corpus it just opened, as one ordered list, run one piece at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one thing and not two tasks.</b> These two used to be two detached <c>Task.Run</c>s
/// started next to each other by the application, over one SQLite corpus, one of them deleting
/// meeting rows while the other read them. Nothing sequenced them; they were held apart only by
/// which meetings each happened to touch and by SQLite's <c>busy_timeout</c>. That had a reachable
/// cost, and it is written down where it bites:
/// <see cref="MeetingsNobodyRecorded"/> takes the folder away before it opens the write
/// transaction that takes the row, so a launch where the render catch-up held the one write lock
/// past the timeout left a phantom meeting standing in the list for good — the folder was gone,
/// and the sweep finds folders, so no later launch could reach the row. Running them one after
/// another removes the second launch-time writer entirely, which is what removes that.
/// </para>
/// <para>
/// <b>The sweep is first and the renders wait behind it.</b> Both orders are correct; this one is
/// better twice over. A phantom meeting is visible in the meetings list — which
/// <see cref="MeetingsWatch"/> re-reads every two seconds — for the length of a sweep rather than
/// for the length of a whole catch-up, which on a corpus with a backlog is many meetings' worth of
/// file writes. And the render pass then reads a corpus with nothing about to vanish out from
/// under it, which is the independence that used to be assumed, now stated by the order.
/// <b>What it costs</b> is that a render now waits out a full sweep, and a sweep walks every folder
/// under the spool root and hashes each one that holds no recording. It is bounded by that folder
/// count and normally finds nothing to do, and nothing on screen waits on a render — a rendered
/// file says nothing about the stage a meeting is at — so the delay is invisible.
/// </para>
/// <para>
/// <b>What a third chore inherits.</b> It goes in <see cref="InOrder"/>, after the two above, and
/// that is the whole of what adding one means: it runs alone, on the calling thread, and it does
/// not have to absorb what it throws because <see cref="RunIn(DirectoryInfo, IReadOnlyList{LaunchChore})"/>
/// does that for it. Its own report reaches <see cref="LaunchDone.Left"/> with its name on it
/// without it arranging anything. Nobody adding one has to reason about the two already there.
/// </para>
/// <para>
/// <b>The report is built and nobody reads it yet.</b> The one caller drops the
/// <see cref="LaunchDone"/>, exactly as it already dropped the <see cref="MeetingsSwept"/> and the
/// <see cref="RendersCaughtUp"/> — why nobody is told is argued on <see cref="OwedRenders"/> and on
/// <see cref="MeetingsNobodyRecorded"/>, and is not re-argued here. What building it buys today is
/// that the tests below are its reader, and that every line either chore was already producing
/// arrives in one list under the name of the chore that produced it. The day somebody puts a line
/// on the window there is one thing to read and it already holds the sentences.
/// </para>
/// <para>
/// Synchronous, on the caller's thread, and it starts nothing. Which thread this goes on is the
/// application's decision and stays there, the same split <c>docs/layout.md</c> already states for
/// the renders: the rule lives where a build agent runs it, and what the application holds is the
/// call and the thread it goes on.
/// </para>
/// </remarks>
public static class WhatALaunchOwes
{
    /// <summary>
    /// What a launch owes the corpus, in the order it is owed. The order is the point: see the
    /// class remarks for why the sweep is first and what that costs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both names are report strings and neither reaches a screen, so they are written here rather
    /// than in the catalogue a person reads. If a launch report is ever said out loud, that is the
    /// decision <see cref="OwedRenders"/> and <see cref="MeetingsNobodyRecorded"/> already record
    /// as owed, and it needs words in the catalogue rather than these.
    /// </para>
    /// <para>
    /// <b>The sweep.</b> At launch, because a start is where the folders left by every press before
    /// this one are sitting, and because hanging it on the meetings list would tie it to how often
    /// somebody looks. It is not because a launch is quiet: the window is already up and somebody
    /// can press record while this runs. What keeps that safe is the mark the press holds over its
    /// folder, from the moment the folder is made rather than from the moment a device opens, which
    /// the sweep's delete runs into rather than asks about — <see cref="MeetingsNobodyRecorded"/>
    /// says how, and it is the sweep's rule and not this list's. A drawer opened inside the second
    /// it takes reads one phantom meeting that is gone by the next start, which is the list this
    /// fixes rather than a new defect. Nobody is told how it went, and unlike the renders that is
    /// not a decision left owed: what a sweep declines to touch is a folder it was right to leave,
    /// and there is nothing in it for a person to answer.
    /// </para>
    /// <para>
    /// <b>The renders.</b> Here, and not on the meetings screen opening. The response arriving is
    /// what puts a meeting in this state, and launch is where the application learns of one that
    /// arrived while it was closed — today it is the only place it can learn of one at all, because
    /// nothing in this application runs a transcription: taking a stage queues a job and starts
    /// nothing, so there is no completion to hang this off yet. Hanging it on the screen instead
    /// would tie the work to how often somebody looks at a list, which is not what decides the
    /// files. Nobody is told how it went, and that is the decision: the files cost nothing and can
    /// be produced again, so a render that failed is one the next launch tries again. Saying it out
    /// loud instead would need a line on the window and words in the catalogue, which is a screen
    /// decision and not this one's — and it is owed for the failure that is not transient, a
    /// response the parser can never read, which every launch retries and drops again.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<LaunchChore> InOrder { get; } =
    [
        new LaunchChore(
            "the meetings nobody recorded",
            root => MeetingsNobodyRecorded.SweepIn(root).Left),
        new LaunchChore(
            "the renders nobody asked for",
            root => OwedRenders.CatchUpOn(root, TimeProvider.System).CouldNotRender),
    ];

    /// <summary>
    /// Does everything a launch owes the corpus in <paramref name="root"/>, in the stated order.
    /// </summary>
    /// <param name="root">The corpus root.</param>
    public static LaunchDone RunIn(DirectoryInfo root) => RunIn(root, InOrder);

    /// <summary>
    /// Does <paramref name="chores"/> over the corpus in <paramref name="root"/>, one at a time, in
    /// the order they are given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A chore that threw does not stop the ones behind it, and its message lands in
    /// <see cref="LaunchDone.Left"/> under its name instead. Both chores in
    /// <see cref="InOrder"/> already answer instead of throwing, so what this catches is a defect
    /// in one of them or whatever a third one turns out to raise — and it is caught here so that
    /// third one does not have to argue the boundary again.
    /// </para>
    /// <para>
    /// Running out of memory is the exception that leaves, which is the same closed exclusion both
    /// existing chores state for themselves: attempting the rest of a launch under the pressure
    /// that just refused one is building the next attempt out of the same exhaustion.
    /// </para>
    /// </remarks>
    /// <param name="root">The corpus root.</param>
    /// <param name="chores">What to do, in the order to do it.</param>
    public static LaunchDone RunIn(DirectoryInfo root, IReadOnlyList<LaunchChore> chores)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(chores);

        var ran = new List<string>();
        var left = new List<string>();

        foreach (var chore in chores)
        {
            try
            {
                foreach (var line in chore.Do(root))
                {
                    left.Add($"{chore.Name}: {line}");
                }

                ran.Add(chore.Name);
            }
            catch (Exception thrown) when (Absorbable(thrown))
            {
                left.Add($"{chore.Name}: {thrown.Message}");
            }
        }

        return new LaunchDone(ran, left);
    }

    /// <summary>
    /// True when carrying on with the rest of a launch is the lesser harm. The one that is not is a
    /// heap that is gone.
    /// </summary>
    /// <remarks>
    /// Spelled here rather than shared, like every other copy of it in this repository, and that is
    /// deliberate: the expression is one line and the argument is different at every site, which is
    /// what each of those sites writes out at length. A shared name would promise one reason that
    /// none of them actually has, and the next site to need it would inherit an argument instead of
    /// making one. The argument here is that a launch is work nobody is waiting on: a chore that
    /// went wrong is one the next start does again, and nothing it was owed is worth ending an
    /// application over.
    /// </remarks>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
