using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Jobs;

/// <summary>What one sweep settled: the jobs it stopped on a person, and one line per failure.</summary>
public sealed record RestartSettled(IReadOnlyList<Guid> Stopped, IReadOnlyList<string> Left);

/// <summary>
/// What a restart is: a job still marked <see cref="JobState.Running"/> that nobody holds this
/// corpus's <see cref="RunnerLease"/> over, and settling it before anything else touches the queue.
/// </summary>
/// <remarks>
/// <para>
/// A job left <c>Running</c> is a call this product cannot tell was charged for, so it is never
/// retried on its own — <see cref="ProcessingJob.RecoverAfterRestart"/> is the one move, and it goes
/// to a person. What decides whether a job is one of these is not the row alone: a live process is
/// still holding the lease over exactly the job it is sending, and stopping that job here would be
/// this product cancelling a call it is itself making. Holding the lease is what makes "nobody is
/// running this corpus's queue" answerable at all — without it, two windows of this application, or
/// a second launch over the same corpus, would each read the other's in-flight call as abandoned.
/// </para>
/// </remarks>
public static class JobsARestartFound
{
    /// <summary>
    /// Settles every job this restart found <see cref="JobState.Running"/>, under a lease already
    /// held.
    /// </summary>
    /// <remarks>
    /// One transaction: every <c>Running</c> row is recovered, and the batch is saved and committed
    /// together, so a launch that opened this corpus never reads half a sweep. It answers instead of
    /// throwing — the one caller nobody is waiting on synchronously for a sweep to succeed — and it
    /// absorbs everything but running out of memory into one line with no reason of its own beyond
    /// the exception's own message: a defect here is exactly as informative as any other refusal a
    /// caller reads off <see cref="RestartSettled.Left"/>.
    /// </remarks>
    /// <param name="lease">The lease over the corpus to settle. Its <see cref="RunnerLease.Root"/> is the corpus.</param>
    public static RestartSettled Holding(RunnerLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        try
        {
            using var context = CorpusDatabase.Open(lease.Root);
            using var recovering = context.Database.BeginTransaction();

            var stopped = new List<Guid>();

            foreach (var job in context.ProcessingJobs.Where(job => job.State == JobState.Running))
            {
                if (job.RecoverAfterRestart())
                {
                    stopped.Add(job.Id);
                }
            }

            context.SaveChanges();
            recovering.Commit();

            return new RestartSettled(stopped, []);
        }
        catch (Exception thrown) when (Absorbable(thrown))
        {
            return new RestartSettled([], [thrown.Message]);
        }
    }

    /// <summary>
    /// What a launch runs: settles the corpus in <paramref name="root"/> if this process can take
    /// its lease, and says why nothing moved when it cannot.
    /// </summary>
    /// <remarks>
    /// A folder holding no corpus yet owes nobody a sweep, the same answer <c>OwedRenders</c> and
    /// <c>MeetingsNobodyRecorded</c> give it, and for the same reason: a corpus is never made here,
    /// only ever read. A corpus whose lease another process holds is left exactly alone — that
    /// process, launch or pump, is the one running this corpus's queue, and a second one stopping a
    /// job the first is mid-call on would be cancelling a call this product is itself making.
    /// </remarks>
    /// <param name="root">The corpus root.</param>
    public static RestartSettled In(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            if (!CorpusDatabase.HoldsACorpus(root))
            {
                return new RestartSettled([], []);
            }

            using var lease = RunnerLease.TryTake(root);

            if (lease is null)
            {
                return new RestartSettled(
                    [],
                    [
                        "the corpus's runner lease is held by another process — another instance "
                        + "of this application, or something reading the folder — so nothing was "
                        + "stopped",
                    ]);
            }

            return Holding(lease);
        }
        catch (Exception thrown) when (Absorbable(thrown))
        {
            return new RestartSettled([], [thrown.Message]);
        }
    }

    /// <summary>
    /// True when carrying on is the lesser harm. The one that is not is a heap that is gone — the
    /// same closed exclusion every background seam in this product states for itself, spelled here
    /// rather than shared for the reason <c>WhatALaunchOwes.Absorbable</c> gives.
    /// </summary>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
