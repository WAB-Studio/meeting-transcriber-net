using System.Linq.Expressions;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// What the application still owes one meeting: the stage it is at, what it would do to it next,
/// and where that stands.
/// </summary>
/// <remarks>
/// <para>
/// Worked out from the meeting rather than remembered beside it, which is what makes it survive
/// the application closing. Everything it is built from is already durable — the files the meeting
/// has and the job rows it carries — so there is nothing here that a restart could lose, and
/// nothing a screen has to write back when somebody looks away.
/// </para>
/// <para>
/// It answers for one meeting and holds no title, date or duration. What a person reads on a
/// meeting is the meeting's row; what this adds is the one thing that row cannot say.
/// </para>
/// </remarks>
/// <param name="MeetingId">Which meeting this is about.</param>
/// <param name="Stage">How far it has got.</param>
/// <param name="Standing">What is happening about the part it has not got to.</param>
public sealed record OwedWork(Guid MeetingId, MeetingStage Stage, StageStanding Standing)
{
    /// <summary>
    /// Why the newest job of the stage's own kind failed for good, or null when there is no such
    /// job or it did not fail. An init property and not a fourth positional member:
    /// <c>MeetingScreenTests</c> constructs <see cref="OwedWork"/> directly, and a rename of one
    /// more positional member there is not worth this record growing a fourth one every time a
    /// screen wants to know one more thing about a job.
    /// </summary>
    public JobFailure? Failed { get; init; }

    /// <summary>What the application would do to this meeting next, or nothing when it is done.</summary>
    /// <remarks>
    /// Worked out on every read rather than held from construction. A record is copyable, and a
    /// copy made with another stage would otherwise carry the action of the stage it came from —
    /// which is the summary button on a meeting that has not been transcribed.
    /// </remarks>
    public JobKind? Next => Stage.Offers();

    /// <summary>True when the stage's action can be asked for.</summary>
    public bool MayBeTaken => Standing.MayBeTaken();

    /// <summary>
    /// True when the stage can be left for now — including one already asked for and not yet run,
    /// which is what keeps the press that spends money from being the one with no way back.
    /// </summary>
    public bool MayBeLeft => Standing.MayBeLeft();

    /// <summary>
    /// The one job row that stops a meeting on a person, said once as an expression because the
    /// same question gets asked in two places that cannot share a call: <see cref="Of"/> asks it
    /// of rows already in hand, and a reader wanting to narrow to the meetings that might be
    /// stopped asks it of the table without loading any of them.
    /// </summary>
    public static Expression<Func<ProcessingJob, bool>> StopsOnAPerson { get; } = Rule();

    /// <summary>The same rule put to a row already in hand, compiled once.</summary>
    private static readonly Func<ProcessingJob, bool> Stopped = Rule().Compile();

    /// <summary>
    /// The rule itself, built by a call rather than by one of the two above reading the other.
    /// A static field initialiser that reads another one is ordered against it, which this type
    /// had: the compiled copy came out null the moment somebody moved it up the file. A method
    /// has no such order, so neither member here depends on where it is written.
    /// </summary>
    private static Expression<Func<ProcessingJob, bool>> Rule() =>
        job => job.State == JobState.AwaitingUser;

    /// <summary>
    /// True when this meeting is stopped on a person. The state with money or data riding on it,
    /// and the reason it is asked separately rather than read off the standing at every call site
    /// that has to make it obvious.
    /// </summary>
    public bool WaitsOnSomebody => Standing is StageStanding.StoppedOnAPerson;

    /// <summary>
    /// True when the application is still waiting to be told what to do here.
    /// </summary>
    /// <remarks>
    /// A stage somebody declined is not owed and can still be taken, and that pair is the whole of
    /// what declining does. The meeting stays exactly where it was and its button is still there —
    /// what stops is the application counting the meeting among the things it is waiting on.
    /// Anything else would make ignoring either pointless or permanent, and it is neither.
    /// </remarks>
    public bool IsOwed => Standing is StageStanding.Offered;

    /// <summary>
    /// What is owed on a meeting, from what that meeting has and what it carries.
    /// </summary>
    /// <param name="meetingId">The meeting.</param>
    /// <param name="artifacts">The kinds of file it has.</param>
    /// <param name="jobs">Every job row it carries, of any kind and in any order.</param>
    public static OwedWork Of(Guid meetingId, IEnumerable<ArtifactKind> artifacts, IEnumerable<ProcessingJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(jobs);

        var mine = jobs.ToArray();

        // A meeting answered out of another meeting's jobs would be wrong in the one direction
        // that costs money — a stage shown as underway when nothing is running, or offered when
        // something is. Loud rather than plausible.
        if (Array.Find(mine, job => job.MeetingId != meetingId) is { } stray)
        {
            throw new ArgumentException(
                $"A {stray.Kind} job of meeting {stray.MeetingId} was passed as meeting {meetingId}'s.",
                nameof(jobs));
        }

        var stage = MeetingStages.Of(
            artifacts,
            mine.Where(job => job.State is JobState.Succeeded).Select(job => job.Kind));

        // A precedence, loudest first, the way `MeetingStages.StandingOf` reads its own. The stop
        // is asked of the whole meeting and of every kind of job on it, not only of the stage it
        // is at: a charge that may already have happened is the meeting's problem wherever in the
        // meeting it happened, so a transcription left unsettled by a restart whose response then
        // turned up, or a capture the restart stopped, would otherwise be invisible on the only
        // screen that shows one at all — and one of them would have an accent button beside it
        // offering to spend again.
        var next = stage.Offers();
        var ofNext = next is { } kind ? mine.Where(job => job.Kind == kind).ToArray() : [];

        var standing = mine.Any(Stopped)
            ? StageStanding.StoppedOnAPerson
            : next is not null
                ? MeetingStages.StandingOf(ofNext.Select(job => job.State))
                : StageStanding.NothingToDo;

        // The newest attempt of the stage's own kind, and only that kind: a failed Extract job is
        // not what a Transcribe row is offered again over, and an answer that came after a failure
        // is what this ordering is for. By CreatedAt alone: a CreatedAt-then-StartedAt tiebreak was
        // tried and dropped, because on a tie it sorted an already-started (and possibly stale) job
        // ahead of a fresh, not-yet-started one — StartedAt is null until Start() runs, and
        // OrderByDescending puts null last — which is backwards for "the newest wins". Nothing in
        // this application reaches that tie today: MeetingWork refuses a second job of a kind while
        // an earlier one of it is not yet terminal, so two same-kind jobs on one meeting can only
        // ever have distinct CreatedAt values. Getting a tiebreak right for a case nothing reaches
        // is not worth the machinery.
        var newest = ofNext.OrderByDescending(job => job.CreatedAt).FirstOrDefault();

        return new OwedWork(meetingId, stage, standing)
        {
            Failed = newest is { State: JobState.FailedPermanent } failed ? failed.Failure : null,
        };
    }
}
