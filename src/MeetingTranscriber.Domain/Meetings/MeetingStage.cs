using System.Collections.Frozen;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// How far along a meeting is in what the application still owes it. Deliberately not
/// <see cref="LifecycleState"/>, which says whether the meeting is here at all: a meeting can be
/// active and owed a transcript, and one on its way out is owed nothing whatever stage it reached.
/// </summary>
public enum MeetingStage
{
    /// <summary>
    /// There is no audio under this meeting yet, so there is nothing to do anything with. A
    /// meeting being recorded right now is here, and so is one whose recording never finished —
    /// which is the recovery screen's to offer, not this one's. What matters is that neither is
    /// ever offered for transcription: paying a provider for a meeting with no audio is the one
    /// mistake this stage exists to make impossible.
    /// </summary>
    Recording = 1,

    /// <summary>Recorded or imported, and nothing has been made of it yet.</summary>
    Recorded = 2,

    /// <summary>The response somebody paid for is in. What is left is a summary.</summary>
    Transcribed = 3,

    /// <summary>Summarised. The application owes this meeting nothing more.</summary>
    Summarised = 4,
}

/// <summary>
/// Where the meeting's current stage stands, which is the half of the answer the stage itself
/// cannot give. The stage says what has been made; this says what is happening about what has not.
/// </summary>
public enum StageStanding
{
    /// <summary>Nobody has answered yet, and the application is waiting to be told.</summary>
    Offered = 1,

    /// <summary>
    /// A job for this stage is queued or running. There is nothing to start twice, and it can
    /// still be left: work nobody has run is work nobody has paid for.
    /// </summary>
    Underway = 2,

    /// <summary>
    /// A job stopped on a person somewhere on this meeting. This is the standing money or data is
    /// riding on — a charge that may already have happened — so it is never quietly one of the
    /// others, and it is the one standing where neither answer is the application's to take.
    /// </summary>
    StoppedOnAPerson = 3,

    /// <summary>
    /// It was offered and turned down. The application no longer owes this meeting the stage, and
    /// the stage is still there to be taken: an ignored transcription is one somebody can pay for
    /// next month.
    /// </summary>
    Declined = 4,

    /// <summary>
    /// The stage has no action at all, so there is nothing to offer, decline or run. Its own
    /// value rather than <see cref="Offered"/> over an empty list, which would have a finished
    /// meeting reading as one still waiting to be told something.
    /// </summary>
    NothingToDo = 5,
}

/// <summary>
/// Which stage a meeting is at, what the application would do to it next, and what the jobs it
/// carries say about that — as one table rather than as conditions repeated by whichever screen or
/// command needed them first.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is stored. A meeting's stage is worked out from what the meeting has, every time
/// it is asked, and that is the whole reason closing and opening the application leaves every
/// meeting exactly where it was: there is no remembered copy to be lost, to go stale, or to
/// disagree with the corpus. A column saying "waiting for transcription" would be a second answer
/// to a question the files already answer, and the two would part company the first time a write
/// landed and the column did not.
/// </para>
/// <para>
/// The one thing that <i>is</i> stored is the answer a person gave, because nothing else can
/// produce it: a stage that was offered and turned down is a <see cref="JobState.Cancelled"/> job
/// of that stage's kind, which the job table already calls dropped before it ever started.
/// </para>
/// </remarks>
public static class MeetingStages
{
    /// <summary>
    /// The whole ladder, bottom rung first. Every other question here is this table read from a
    /// different end, so a stage cannot be added to one answer and forgotten in another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendering the transcript's files is not a rung and cannot become one, which is the point of
    /// the table being closed. Those cost nothing and can be produced again from what has already
    /// been paid for, so they are never something a person is asked about — and a ladder that
    /// could name <see cref="JobKind.Render"/> would be one press away from asking. Every action
    /// on it spends either the user's money or their Claude Code quota, so every one of them
    /// waits to be told.
    /// </para>
    /// <para>
    /// The two stages off the ladder are the two with no action. <see cref="MeetingStage.Recording"/>
    /// is below it, where there is nothing yet to work on, and <see cref="MeetingStage.Summarised"/>
    /// is above it, where there is nothing left the application owes.
    /// </para>
    /// </remarks>
    private static readonly Rung[] Ladder =
    [
        new(MeetingStage.Recorded, JobKind.Transcribe, ArtifactKind.DeepgramResponse),
        new(MeetingStage.Transcribed, JobKind.Extract, ArtifactKind.Extraction),
    ];

    /// <summary>The file whose absence means there is nothing under this meeting to work on.</summary>
    private const ArtifactKind Captured = ArtifactKind.Audio;

    private static readonly FrozenDictionary<MeetingStage, JobKind?> Actions = Ladder
        .Select(rung => KeyValuePair.Create(rung.Stage, (JobKind?)rung.Action))
        .Append(KeyValuePair.Create(MeetingStage.Recording, (JobKind?)null))
        .Append(KeyValuePair.Create(MeetingStage.Summarised, (JobKind?)null))
        .ToFrozenDictionary();

    /// <summary>
    /// The only kinds of file that say how far a meeting has got. Everything else it has — its
    /// recovery card, the files rendered off the response — says nothing about that, so a reader
    /// answering this question loads these and leaves the rest alone.
    /// </summary>
    public static IReadOnlySet<ArtifactKind> Milestones { get; } =
        Ladder.Select(rung => rung.Marker).Append(Captured).ToFrozenSet();

    /// <summary>
    /// What the application would do to a meeting at this stage next, or nothing when it is done
    /// with it.
    /// </summary>
    public static JobKind? Offers(this MeetingStage stage) => Actions.TryGetValue(stage, out var kind)
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown meeting stage.");

    /// <summary>
    /// How far a meeting has got, read off what it has rather than off anything remembering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two accounts, and a rung is passed when either says so. The files are the ordinary one: a
    /// meeting has been transcribed when the paid response is in its folder, which is also how a
    /// meeting imported from somewhere else lands at the right stage with no job rows at all.
    /// </para>
    /// <para>
    /// A job that came back saying it landed counts too, and that is not belt and braces. A
    /// transcription whose job succeeded and whose artifact row is missing — a reconciler that has
    /// not run, a file moved out from under the corpus — would otherwise be offered for
    /// transcription a second time, and the second time is a second charge for work already done.
    /// Reading both means the button is withheld and the meeting is shown as transcribed with a
    /// file missing, which is a problem somebody can look at rather than one they pay for.
    /// </para>
    /// </remarks>
    /// <param name="artifacts">The kinds of file this meeting has.</param>
    /// <param name="succeeded">The kinds of job this meeting has that came back succeeded.</param>
    public static MeetingStage Of(IEnumerable<ArtifactKind> artifacts, IEnumerable<JobKind> succeeded)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(succeeded);

        var has = artifacts.ToHashSet();
        var landed = succeeded.ToHashSet();

        // Read from the top down and answered by the highest rung already climbed, so a meeting
        // that skipped one — an import carrying a summary and no response of its own — lands
        // above it rather than being offered work whose output is already there.
        for (var rung = Ladder.Length - 1; rung >= 0; rung--)
        {
            if (!has.Contains(Ladder[rung].Marker) && !landed.Contains(Ladder[rung].Action))
            {
                continue;
            }

            return rung + 1 < Ladder.Length ? Ladder[rung + 1].Stage : MeetingStage.Summarised;
        }

        return has.Contains(Captured) ? MeetingStage.Recorded : MeetingStage.Recording;
    }

    /// <summary>
    /// What the jobs a meeting carries for one kind say about that stage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A precedence rather than the newest row, and deliberately so. A stage that was declined and
    /// then taken has two rows, and asking which is newer makes the answer turn on two timestamps
    /// a millisecond apart. What the order says instead is which of the true things is the one
    /// somebody needs to see, loudest first: work in flight before an answer somebody already
    /// gave, because the answer they gave is the one the work replaced.
    /// </para>
    /// <para>
    /// <see cref="JobState.Succeeded"/> decides nothing here, because a stage whose job succeeded
    /// is not the stage the meeting is at any more — <see cref="Of"/> has already moved it on. Nor
    /// does <see cref="JobState.FailedPermanent"/>, and that is a decision rather than an
    /// oversight: an attempt that failed for good is work that did not happen, so the stage is
    /// owed and offered exactly as it was before anybody tried. What a person is told about the
    /// failure belongs beside whatever runs jobs, which is where the failure is produced and where
    /// what to do about it is known; nothing runs one yet.
    /// </para>
    /// <para>
    /// <see cref="JobState.AwaitingUser"/> is not here either, and for the opposite reason: it is
    /// asked of the whole meeting rather than of one stage's kind, so it is
    /// <see cref="OwedWork"/>'s. A charge that may already have happened is the meeting's problem
    /// wherever in the meeting it happened.
    /// </para>
    /// </remarks>
    /// <param name="states">The states of this meeting's jobs of the stage's kind, in any order.</param>
    public static StageStanding StandingOf(IEnumerable<JobState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var seen = states.ToHashSet();

        if (seen.Any(state => state is JobState.Running || state.IsQueued()))
        {
            return StageStanding.Underway;
        }

        return seen.Contains(JobState.Cancelled) ? StageStanding.Declined : StageStanding.Offered;
    }

    /// <summary>
    /// True when the stage's action is there to be taken.
    /// </summary>
    /// <remarks>
    /// The two that refuse it are the two where taking would do harm. Something already queued or
    /// running does not need starting twice. And a job stopped on a person is stopped precisely
    /// because nobody can say whether it was charged for, so offering to run it again is offering
    /// to pay twice — the release belongs beside that job, not on a fresh press of the same button.
    /// </remarks>
    public static bool MayBeTaken(this StageStanding standing) => standing switch
    {
        StageStanding.Offered or StageStanding.Declined => true,
        StageStanding.Underway or StageStanding.StoppedOnAPerson or StageStanding.NothingToDo => false,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "Unknown stage standing."),
    };

    /// <summary>
    /// True when the stage can be left for now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is not <see cref="MayBeTaken"/> upside down, and the difference is
    /// <see cref="StageStanding.Underway"/>. Work that is queued and that nothing has run is work
    /// nobody has paid for, so somebody who asked for it can still say never mind — and without
    /// that, asking would be the one press on this screen with no way back, which is exactly
    /// backwards from the press that spends nothing being the reversible one.
    /// </para>
    /// <para>
    /// The one it refuses is a job stopped on a person. There, "not now" is not an answer anybody
    /// can give: what is unsettled is whether a charge already happened, and dropping the job is
    /// throwing away the only record that it might have.
    /// </para>
    /// </remarks>
    public static bool MayBeLeft(this StageStanding standing) => standing switch
    {
        StageStanding.Offered or StageStanding.Declined or StageStanding.Underway => true,
        StageStanding.StoppedOnAPerson or StageStanding.NothingToDo => false,
        _ => throw new ArgumentOutOfRangeException(nameof(standing), standing, "Unknown stage standing."),
    };

    /// <summary>
    /// One rung: the stage a meeting sits at until it is climbed, the work that climbs it, and the
    /// file that says it was climbed.
    /// </summary>
    private readonly record struct Rung(MeetingStage Stage, JobKind Action, ArtifactKind Marker);
}
