using System.Collections.Frozen;

namespace MeetingTranscriber.Domain.Jobs;

/// <summary>
/// Which moves between <see cref="JobState"/> values exist, as one table rather than as a
/// condition repeated by every caller that writes a state.
/// </summary>
/// <remarks>
/// The table is the whole contract: a state not listed as a destination cannot be reached, and a
/// state whose destinations are empty is where a job stops for good. Everything a runner is
/// allowed to do to a job goes through <see cref="ProcessingJob"/>, which asks here first.
/// </remarks>
public static class JobStates
{
    private static readonly FrozenDictionary<JobState, FrozenSet<JobState>> Moves =
        new Dictionary<JobState, FrozenSet<JobState>>
        {
            // Queued. It either runs, waits for a person to approve what it will cost, or is
            // dropped before it ever started.
            [JobState.Pending] = Set(JobState.Running, JobState.AwaitingUser, JobState.Cancelled),

            // In flight. Every way an attempt can end hangs off here.
            [JobState.Running] = Set(
                JobState.Succeeded,
                JobState.FailedRetryable,
                JobState.FailedPermanent,
                JobState.AwaitingUser,
                JobState.Cancelled),

            // Stopped on a person. It goes back to the queue only because somebody put it there,
            // which is what makes an uncertain charge impossible to repeat by accident. It can
            // also be resolved outright: a restart that finds the paid response already on disk
            // settles the job without spending again.
            [JobState.AwaitingUser] = Set(JobState.Pending, JobState.Succeeded, JobState.FailedPermanent, JobState.Cancelled),

            // Failed in a way that can be repeated. The runner picks it up again on its own once
            // next_attempt_at arrives; it never goes back through pending to get there.
            [JobState.FailedRetryable] = Set(
                JobState.Running,
                JobState.AwaitingUser,
                JobState.FailedPermanent,
                JobState.Cancelled),

            // Terminal. Work that has to happen after one of these is a new job with its own
            // idempotency key, not this one moved back to life.
            [JobState.Succeeded] = Set(),
            [JobState.FailedPermanent] = Set(),
            [JobState.Cancelled] = Set(),
        }.ToFrozenDictionary();

    /// <summary>Where a job in this state can still go.</summary>
    public static IReadOnlySet<JobState> Next(this JobState state) => Moves.TryGetValue(state, out var next)
        ? next
        : throw new JobTransitionException($"Unknown job state '{state}'.");

    /// <summary>True when the job stops here and nothing moves it again.</summary>
    public static bool IsTerminal(this JobState state) => state.Next().Count == 0;

    /// <summary>
    /// True when the runner may start this job by itself, once it is due. The one state left out
    /// on purpose is <see cref="JobState.AwaitingUser"/>: that is the whole point of it.
    /// </summary>
    public static bool IsQueued(this JobState state) =>
        state is JobState.Pending or JobState.FailedRetryable;

    public static bool CanMoveTo(this JobState from, JobState to) => from.Next().Contains(to);

    /// <summary>Throws unless the move is one the table has.</summary>
    public static void EnsureCanMoveTo(this JobState from, JobState to)
    {
        if (from.CanMoveTo(to))
        {
            return;
        }

        var reachable = from.Next().Count == 0
            ? "nowhere: it is a terminal state"
            : string.Join(", ", from.Next().Order());

        throw new JobTransitionException($"A job in '{from}' cannot move to '{to}'; it reaches {reachable}.");
    }

    private static FrozenSet<JobState> Set(params JobState[] states) => states.ToFrozenSet();
}
