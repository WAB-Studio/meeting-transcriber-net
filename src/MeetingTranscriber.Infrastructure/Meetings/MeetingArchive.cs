using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>
/// A meeting the corpus has never held, as the door bringing it in knows it.
/// </summary>
/// <remarks>
/// Every field is one the door had to have decided before it got here, which is why there is no
/// <c>LifecycleState</c> on it and no <c>CreatedAt</c>: the first is what a new meeting always is
/// and is said once, on <see cref="Meeting"/> itself; the second is the instant this write happens.
/// A door that could say either could say something else.
/// </remarks>
/// <param name="Duration">
/// How long the meeting turned out to be, off whichever of the two things the door can read — the
/// audio that is going in, or what the provider says it transcribed. Never null: a meeting arriving
/// through a door has its source in hand, and one that does not is a recording rather than an
/// arrival.
/// </param>
public sealed record NewMeeting(
    Guid Id,
    UtcTimestamp StartedAt,
    Duration Duration,
    SourceProfile Profile,
    string Language,
    string? Title = null,
    string? Context = null);

/// <summary>
/// The file a meeting arrived on and the word the corpus keeps about how it arrived.
/// </summary>
/// <param name="Kind">What the file is, which is what decides whether it may ever be written over.</param>
/// <param name="FileName">Its name inside the meeting's folder — <c>CorpusFiles.PathFor</c> makes the path.</param>
/// <param name="Contents">Where the bytes come from, written into the stream the durable write hands over.</param>
/// <param name="Verb">
/// The audit trail's word for this door. One per door and never one for two: a meeting made out of a
/// WAV somebody had and one whose response they paid for are different things to find later, and one
/// word for both would make the audit unable to tell them apart.
/// </param>
/// <param name="Detail">The rest of the audit line — where it came from, and what it was.</param>
public sealed record ArrivedOn(
    ArtifactKind Kind,
    string FileName,
    Action<Stream> Contents,
    string Verb,
    string Detail);

/// <summary>
/// The path a meeting arrives in this corpus down, for every door that brings one from outside.
/// </summary>
/// <remarks>
/// <para>
/// Three doors arrive down this: a paid response somebody already has, audio somebody brought in,
/// and a response filed onto a meeting the corpus recorded. Each decides what it can decide — what
/// the file is, how long the meeting turned out to be, and the verb the audit keeps for it — and
/// none of them decides the order the rows and the file land in, because that order is the same for
/// all three and was written twice over before this existed.
/// </para>
/// <para>
/// <b>A fourth writer is outside it, and that is settled rather than unfinished.</b>
/// <c>MeetingRecordings</c> opens a meeting before there is anything to file — no source, no length
/// and no verb, which is why <see cref="NewMeeting.Duration"/> can be non-null here — and
/// <c>Finish</c> stages the recording's audio <em>outside</em> its transaction, because an hour of
/// WAV copied under SQLite's <c>BEGIN IMMEDIATE</c> would refuse every other writer in the
/// application for minutes. <see cref="ArrivedOn.Contents"/> goes straight to
/// <c>DurableArtifact.Write</c>, which stages and commits as one act, so this shape cannot express
/// that: folding <c>Finish</c> in would mean taking a staged artifact instead. That is a real
/// question and it is not this type's.
/// </para>
/// <para>
/// <b>The recovery card is written after the commit and never inside it.</b> The source is a kind
/// the corpus may never write over, so a card refused inside the transaction rolls the source's row
/// back out from under a file already renamed into place. What that costs depends on who named the
/// meeting. Through <see cref="Onto"/> the caller did, so the next attempt lands on the same path,
/// meets a file with no row, and is refused by the durable write for good — that meeting could
/// never be filed again without somebody deleting the file by hand. Through <see cref="New"/> the
/// door mints a fresh id, so the same failure strands the file under an id nothing holds, which
/// <c>ArtifactReconciler.Check</c> reports as <c>Unrecorded</c> and a sweep will not take, because
/// it is a finished file rather than a write that never completed.
/// </para>
/// <para>
/// <b>What a card refused after the commit costs instead, said plainly because it is not free.</b>
/// The card is refused before its row exists, so there is no row for
/// <c>ArtifactReconciler.Check</c> to find missing and nothing reports it: a meeting with no card
/// reads as sound. What puts it right is running the door again — the source is found by its hash,
/// the filing takes its already-here branch, and that branch writes the card — and <c>rebuild</c>
/// writes one per meeting too. So the trade is a folder that can never be filed again against a
/// card two ordinary commands replace, and the second is the one to take.
/// </para>
/// <para>
/// That is also the answer to <c>AudioIntake.Filed</c>, which wrote the card first and argued for
/// it: <em>"written after, a card that would not land would leave the command reporting a refusal
/// over a meeting that is in the corpus for good — which is the one shape a person cannot act
/// on."</em> The refusal is real and the meeting does stay. What the argument missed is that its own
/// alternative left <c>audio.wav</c> on disk under rolled-back rows, which is the shape nobody can
/// act on. <c>MeetingRecordings.Finish</c> reached this answer first and for the same reason.
/// </para>
/// <para>
/// It joins a transaction the caller already has rather than refusing one, which is what both doors
/// did before it — and that is the one place this differs from <c>MeetingRecordings.Finish</c>,
/// which opens a bare <c>BeginTransaction</c> so a caller holding one is refused loudly. The
/// asymmetry is deliberate: <c>Finish</c> has one caller and can dictate to it, while both doors
/// here already composed before this existed, so refusing would be a new refusal for a caller that
/// does not exist. The cost is on the record rather than hidden — a caller inside its own unit of
/// work gets the rows in that unit of work and the card inside it too, which is the trap the
/// paragraphs above argue against, so a caller that wants what they promise opens no transaction
/// of its own.
/// </para>
/// </remarks>
public static class MeetingArchive
{
    /// <summary>A meeting the corpus has never held, and the file it arrived on.</summary>
    /// <returns>
    /// The source that was filed and the recovery card written from the row. Both, because both are
    /// artifacts this produced and a caller reading one of them back out of the context a line later
    /// would be asking a question this already answered.
    /// </returns>
    public static (Artifact Source, Artifact Card) New(
        CorpusDbContext corpus, NewMeeting meeting, ArrivedOn source, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(meeting);
        ArgumentException.ThrowIfNullOrWhiteSpace(meeting.Language);

        return Filed(corpus, meeting.Id, meeting, source, now);
    }

    /// <summary>
    /// A file arriving onto a meeting the corpus already holds, which adds no row of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller is what has established that the meeting is there and that this file belongs to
    /// it. Nothing here checks either: a lookup would be a second answer to a question the caller
    /// had to have asked to have got this far, and this method's job is the order, not the identity.
    /// A meeting that is not there is still loud rather than silent, just not kindly so — the audit
    /// row this adds carries a foreign key to <c>meetings.id</c> and the interceptor turns foreign
    /// keys on, so the save below refuses it as a <c>DbUpdateException</c> wrapping SQLite's
    /// constraint failure. That is a refusal both <c>Cli.IsRefusal</c> and
    /// <c>ScreenFailures.Reportable</c> name, so it comes out as a sentence rather than a stack
    /// trace; what it does not do is say which meeting, which is the caller's to say and the reason
    /// both callers check first.
    /// </para>
    /// <para>
    /// Whatever the caller has already tracked on the context goes down in this method's save, in
    /// the transaction that files the source. A door that touched the meeting row on its way here —
    /// an <c>UpdatedAt</c> for a meeting that has just gained a source it did not have — gets that
    /// written with the rest, or written nowhere and left pending on a change tracker a rollback
    /// does not undo. A refusal from here therefore hands back a context nobody may save again,
    /// which is what <c>MeetingRecordings.Finish</c> says about its own and what every caller here
    /// has to do the same with.
    /// </para>
    /// </remarks>
    public static (Artifact Source, Artifact Card) Onto(
        CorpusDbContext corpus, Guid meetingId, ArrivedOn source, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        return Filed(corpus, meetingId, null, source, now);
    }

    /// <summary>
    /// The order, and the only place it is written: the row when there is one, the audit line, one
    /// save, the file, the commit, and the card after it.
    /// </summary>
    private static (Artifact Source, Artifact Card) Filed(
        CorpusDbContext corpus,
        Guid meetingId,
        NewMeeting? arriving,
        ArrivedOn source,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(source);

        // The two strings on `source` that reach storage, guarded together because they are the
        // same kind of thing: `action` is a required column and `FileName` becomes the path a row
        // names and a file is written to. A blank either way is a corpus that stores nothing
        // readable and says nothing about it.
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Verb);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.FileName);

        using var filing = corpus.Database.CurrentTransaction is null
            ? corpus.Database.BeginTransaction()
            : null;

        if (arriving is { } meeting)
        {
            // No `LifecycleState` here on purpose. What a meeting's lifecycle starts as is said
            // once, on `Meeting` itself, and a door repeating it is a second place to change and a
            // second place to get wrong. The one writer left that does repeat it —
            // `MeetingRecordings.Open` — agrees with it today, which is why this is duplication and
            // was never a defect.
            corpus.Meetings.Add(new Meeting
            {
                Id = meeting.Id,
                Title = meeting.Title,
                Context = meeting.Context,
                StartedAt = meeting.StartedAt,
                Duration = meeting.Duration,
                SourceProfile = meeting.Profile,
                Language = meeting.Language,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        // Where this meeting came from, in the table provenance belongs in. A corpus holding a
        // meeting nobody recorded on this machine is worth being able to explain later.
        corpus.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = now,
            Actor = AuditActor.App,
            Action = source.Verb,
            MeetingId = meetingId,
            Detail = source.Detail,
        });

        // Before the file, because an artifact cannot point at a row that is not there yet.
        corpus.SaveChanges();

        var stored = DurableArtifact.Write(
            corpus,
            meetingId,
            source.Kind,
            CorpusFiles.PathFor(meetingId, source.FileName),
            now,
            source.Contents);

        filing?.Commit();

        // After the commit, for the reason this type's remarks give.
        return (stored, MeetingManifest.Write(corpus, meetingId, now));
    }
}
