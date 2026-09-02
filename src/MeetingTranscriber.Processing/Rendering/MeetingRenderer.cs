using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>A meeting that cannot be rendered, saying which one and what is missing.</summary>
public sealed class RenderException(string message) : Exception(message);

/// <summary>What one render produced.</summary>
public sealed record RenderedMeeting(int Turns, Artifact Transcript, Artifact Utterances);

/// <summary>
/// Everything derived from a meeting's paid response: the turns a citation anchors on, the
/// transcript a person reads and the jsonl a machine lines up against the response.
/// </summary>
/// <remarks>
/// <para>
/// Safe to run again, and meant to be. Nothing here reads the previous render — the response and
/// the human layer are the whole input — so the same corpus renders the same bytes, and a
/// correction or a resolved speaker added today changes today's render of a meeting recorded last
/// year. That is also why it is the only writer of <c>utterances</c>: those rows are a projection
/// and never an edit.
/// </para>
/// <para>
/// It replaces the turns of the meeting it renders, so a claim citing one of them stops it. That is
/// the constraint doing its job rather than a gap: deleting turns out from under the claims that
/// cite them is what a projection deleted out of order looks like, and putting the claims back is
/// the rebuild's business, not this one's. Where the caller has deferred that constraint to make
/// the swap possible at all, <see cref="RefuseStrandedClaims"/> is what still stops it, by name and
/// before anything is deleted.
/// </para>
/// </remarks>
public static class MeetingRenderer
{
    public static RenderedMeeting Render(CorpusDbContext context, Guid meetingId, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var meeting = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new RenderException($"There is no meeting {meetingId} to render.");
        var turns = Project(context, meeting, now);
        var header = Header(context, meeting);
        var rendered = TranscriptRenderer.Render(header, turns);

        // Both or neither, on every caller. A transcript naming turns the jsonl does not have is a
        // meeting that reads as two different meetings depending on which file somebody opened, and
        // nothing in the corpus would say so: each row would still agree with the file it names.
        // Both files are therefore written whole and both destinations emptied before either file
        // is put in place, which is what DurableArtifact.WriteAllText is; the rows land in one
        // save, in whatever unit of work the caller has.
        //
        // The turns are outside it and ahead of it. Project has already replaced them, so a
        // meeting refused here keeps rows from this render and files from the last one — which is
        // the deliberate half of the trade, because those rows are what a claim cites and the
        // caller is the one that decides whether to undo them: OwedRenders and the importer wrap
        // this in a transaction for exactly that, and a rebuild refuses to, because undoing a row
        // under a file already moved is the one direction the corpus never goes.
        //
        // This used to open a transaction when the caller had none, and that was never the thing
        // making the pair either-or: the transcript's row and its file were already committed by
        // the time the jsonl was written anywhere, so a refusal between them left one file of each
        // generation with or without a transaction. A caller already inside one — a rebuild wraps
        // the whole corpus in a single transaction, because every stored claim is checked against
        // the turns it cites at one commit — got no pairing at all. There is nothing left for a
        // transaction here to do, so there is not one.
        var written = DurableArtifact.WriteAllText(
            context,
            meeting.Id,
            now,
            (ArtifactKind.Transcript, CorpusFiles.PathFor(meeting.Id, "transcript.md"), rendered.Markdown),
            (ArtifactKind.Utterances, CorpusFiles.PathFor(meeting.Id, "utterances.jsonl"), rendered.Jsonl));

        return new RenderedMeeting(
            turns.Count, Of(written, ArtifactKind.Transcript), Of(written, ArtifactKind.Utterances));
    }

    /// <summary>The one artifact of that kind the write came back with.</summary>
    /// <remarks>
    /// By kind and not by position. The set goes in as a list and comes back as one, so a third
    /// derived file added in the middle of it would silently move the jsonl into the transcript's
    /// place on this record, and every citation checked against that record would be checked
    /// against the wrong file.
    /// </remarks>
    private static Artifact Of(IReadOnlyList<Artifact> written, ArtifactKind kind) =>
        written.Single(artifact => artifact.Kind == kind);

    /// <summary>
    /// Reads the response and puts the meeting's turns back, all of them or none of them, and
    /// settles the one speaker the recording settles by itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows and nothing else. The two files are written after this returns and are outside what
    /// it promises — <see cref="Render"/> says what a meeting refused at that point is left holding.
    /// </para>
    /// <para>
    /// The assignment is here and not on the door that files a response, and that is the whole of
    /// why every door agrees about it. Filing one, rendering it at a prompt, rebuilding the corpus
    /// and the sweep the application runs at launch all arrive here, so a meeting cannot come out
    /// of one of them reading a name and out of another reading a label. It is derived from the
    /// response exactly like the turns beside it, which is what makes it safe to do again.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Turn> Project(CorpusDbContext context, Meeting meeting, UtcTimestamp now)
    {
        var response = context.Artifacts.FirstOrDefault(
                artifact => artifact.MeetingId == meeting.Id && artifact.Kind == ArtifactKind.DeepgramResponse)
            ?? throw new RenderException(
                $"Meeting {meeting.Id} has no response to render from; nothing derived can be produced without it.");

        var file = CorpusFiles.Locate(context.Root, response.RelativePath);
        if (!file.Exists)
        {
            throw new RenderException(
                $"Meeting {meeting.Id} names '{response.RelativePath}' as its response and the file is not there.");
        }

        var transcript = DeepgramTranscriptParser.ParseFile(file.FullName, meeting.SourceProfile);
        var turns = Turns.Group(transcript.Segments);
        Replace(context, meeting.Id, turns);

        // The profile is the meeting's own and not a caller's, which is what keeps the label this
        // writes and the label the turns carry the same one: both come off the row this render is
        // of. It never overrules a person — `Assign` refuses that in its own right — and it settles
        // nothing at all until somebody has said who is using this install.
        new HumanLayer(context, now).SettleTheMicrophone(meeting.Id, meeting.SourceProfile, transcript.Segments);

        return turns;
    }

    /// <summary>The name the projection can be put back to, and the only one this file takes.</summary>
    /// <remarks>
    /// A savepoint name is a stack on the connection rather than something this class owns, and a
    /// constant is safe here only because nothing re-enters: <c>Project</c> runs once per
    /// <see cref="Render"/> and a render is never nested inside another. Anything that starts taking
    /// savepoints elsewhere on this connection has to own name allocation, because two of the same
    /// name are both legal and a rollback silently goes to the innermost.
    /// </remarks>
    private const string BeforeTheTurnsGo = "projection";

    /// <summary>
    /// Swaps one meeting's turns for the ones just read, as one thing that either happens or does
    /// not. The rows go before the new ones arrive rather than in one statement, because the
    /// position of a turn in its meeting is unique and the two generations share every one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The savepoint is what makes "either" true, and it has to be this one rather than the one EF
    /// takes around a <c>SaveChanges</c> inside a caller's transaction. The delete is a statement
    /// that has already run — <c>ExecuteDelete</c> goes to the database rather than to the change
    /// tracker, and so escapes that unit of work entirely — so a refusal arriving between it and the
    /// save used to leave the meeting holding neither generation: no turns at all, with its
    /// transcript and its jsonl still standing and naming turns it no longer had, and nothing in
    /// <see cref="Infrastructure.Storage.CorpusIntegrity"/> able to see it, because a meeting with
    /// no turns breaks no foreign key. <c>utterances</c> is what a citation anchors on, so that is a
    /// meeting losing what its claims point at, silently.
    /// </para>
    /// <para>
    /// Deleting a turn a claim cites is only possible at all while foreign keys are deferred, which
    /// is the caller's to arrange and <c>CorpusRebuild</c> is what arranges it. That is why the
    /// undo has to reach the delete and not only the insert: the delete raises the deferred count
    /// once per claim, and a refusal that left it raised would take the caller's whole commit rather
    /// than the meeting. Rolling back to a savepoint lowers it again and leaves the deferral itself
    /// standing, which is what
    /// <c>CorpusRebuildTests.A_meeting_refused_with_cited_turns_costs_that_meeting_and_not_the_run</c>
    /// is for.
    /// </para>
    /// <para>
    /// It reaches exactly as far as the rows and stops before anything on disk moves. A caller
    /// already inside a transaction gets a savepoint in it and keeps its own transaction — a
    /// rebuild runs the whole corpus under one, because every stored claim is checked against the
    /// turns it cites at a single commit, and undoing a meeting further than this would undo an
    /// artifact row under a file <c>StagedArtifact.Commit</c> has already moved into place. A
    /// caller with no transaction gets one for the length of the swap and nothing wider, which is
    /// what a single <c>render</c> from the command line is: there the delete and the save are two
    /// separately committed statements, and a meeting that had turns loses them exactly the same
    /// way. On that path it is the transaction and not the savepoint that makes "either" true —
    /// measured, by deleting the three savepoint calls and finding
    /// <c>MeetingRendererTests.A_render_outside_a_transaction_leaves_a_refused_meeting_the_turns_it_had</c>
    /// still green, and red only once the transaction went too. One mechanism across both paths is
    /// the choice; which half is load-bearing depends on which caller arrived.
    /// </para>
    /// <para>
    /// The release and the commit are inside the guard rather than after it, which is not tidiness:
    /// a release that throws with them outside would dispose <c>own</c> unrolled-back, losing turns
    /// the save had just accepted while the tracker still held them as
    /// <see cref="EntityState.Unchanged"/> — rows EF believes are in the database and are not, and
    /// the one shape <c>CorpusRebuild.Discard</c> cannot sweep, because it is not
    /// <see cref="EntityState.Added"/>. Inside, the same throw reaches <see cref="Forget"/>.
    /// </para>
    /// <para>
    /// The tracker is emptied of this meeting's turns rather than left holding either generation.
    /// Rolling back to a savepoint leaves EF's opinion of what is pending untouched, so turns added
    /// for a swap that did not happen would still be waiting to be sent by whatever saves next —
    /// which is the poison the rebuild's own discard exists to stop, arriving from inside the thing
    /// that caused it. The old turns are back in the database and tracked by nothing, which is the
    /// state every read here starts from anyway.
    /// </para>
    /// </remarks>
    private static void Replace(CorpusDbContext context, Guid meeting, IReadOnlyList<Turn> turns)
    {
        RefuseStrandedClaims(context, meeting, turns);

        using var own = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;
        var enclosing = context.Database.CurrentTransaction!;
        enclosing.CreateSavepoint(BeforeTheTurnsGo);

        try
        {
            Forget(context, meeting);
            context.Utterances.Where(turn => turn.MeetingId == meeting).ExecuteDelete();

            foreach (var turn in turns)
            {
                context.Utterances.Add(new Utterance
                {
                    Id = Guid.NewGuid(),
                    MeetingId = meeting,
                    Ordinal = turn.Ordinal,
                    Start = turn.Start,
                    End = turn.End,
                    Channel = turn.Channel,
                    SpeakerLabel = turn.SpeakerLabel,
                    // The words the provider returned. Corrections are applied to the rendered
                    // files and never here: this row is what a citation is checked against, and a
                    // quote that has been silently corrected no longer matches the evidence it
                    // claims to be from.
                    Text = turn.Text,
                    Confidence = turn.Confidence,
                });
            }

            context.SaveChanges();
            enclosing.ReleaseSavepoint(BeforeTheTurnsGo);
            own?.Commit();
        }
        catch
        {
            Undo(enclosing);
            Forget(context, meeting);
            throw;
        }
    }

    /// <summary>
    /// Refuses a swap the meeting's own claims could not survive — a position they cite that the
    /// turns just read do not have — before a single row is deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A citation anchors on the meeting and the position of the turn inside it, so the same
    /// response projected again lands every claim back on the words it came from. A response that
    /// is no longer that one — a folder half restored from somewhere else, a re-transcription —
    /// produces its own turns, and where there are fewer of them a claim citing a position past the
    /// end is a claim citing nothing. The database says so too: the citation foreign keys are
    /// <c>(meeting_id, utterance_ordinal)</c> with no cascade. But only at the end of the statement,
    /// or, where the caller has deferred them, at its commit.
    /// </para>
    /// <para>
    /// Which is the whole reason this is asked here rather than left to that. On the rebuild's path
    /// the answer arrived at <c>CorpusRebuild</c>'s corpus-wide commit, outside every per-meeting
    /// guard, so one meeting whose response had changed underneath it took every meeting the run
    /// had rebuilt and the report naming the one it could not — the failure absorbing a refusal
    /// exists to stop, arriving through the one door left open. Asked before the delete, the same
    /// refusal costs that meeting: it keeps the turns it had, the run carries on, and the line in
    /// the report says which positions are cited and how many turns the response now produces,
    /// which is what somebody needs to know whether the file or the claims are the thing to put
    /// right. It is asked on every caller, deferral or not, because it is asked before the delete
    /// rather than after the save.
    /// </para>
    /// <para>
    /// What it is not is a check that the response is the one the claims were made from. It reads
    /// positions and nothing else, so a response that still reaches every cited position — the same
    /// count, or more, or the same ordinals regrouped — passes here, passes the deferred check at
    /// the commit, and leaves every claim of that meeting anchored on different words, which
    /// <c>CorpusIntegrity.Check</c> cannot see either: it never compares a citation to a turn.
    /// Closing that means comparing what arrives against what the claim quoted, or refusing to
    /// render from a response whose bytes are not the ones the artifact row names — which is the
    /// reconciler's question and its own command today. Neither is this, and neither is on the
    /// board.
    /// </para>
    /// <para>
    /// Before the delete is also what makes it cost nothing to undo: no row has gone, so no deferred
    /// count has been raised and the savepoint below has nothing to put back. The claims are read
    /// out of the database and not out of the tracker, and that is a precondition rather than a
    /// fact about the code — nothing writes a claim anywhere in <c>src/</c> yet, and when accepting
    /// an extraction does, a caller staging claims and rendering in one unit of work would walk
    /// past this.
    /// </para>
    /// </remarks>
    private static void RefuseStrandedClaims(
        CorpusDbContext context, Guid meeting, IReadOnlyList<Turn> turns)
    {
        var arriving = turns.Select(turn => turn.Ordinal).ToHashSet();
        var stranded = Cited(context, meeting)
            .Where(ordinal => !arriving.Contains(ordinal))
            .Distinct()
            .Order()
            .ToArray();

        if (stranded.Length > 0)
        {
            throw new RenderException(
                $"Meeting {meeting} has claims citing turns the response it renders from no longer "
                + $"reaches. It produces {turns.Count} turns and they cite "
                + $"{string.Join(", ", stranded)}, so producing them again would leave those claims "
                + "citing nothing.");
        }
    }

    /// <summary>Every position this meeting's claims cite, once per claim.</summary>
    /// <remarks>
    /// The three kinds an extraction produces with evidence under them. A summary has none, and an
    /// action's progress hangs off the action rather than off a turn. Hand-listed and held to the
    /// model by
    /// <c>MeetingRendererTests.Every_kind_of_claim_the_model_hangs_off_a_turn_is_one_the_renderer_asks_about</c>,
    /// because a fourth kind added and not asked here would not fail: its citation would go back to
    /// being found at the corpus-wide commit, silently costing the whole run again.
    /// </remarks>
    private static IEnumerable<int> Cited(CorpusDbContext context, Guid meeting) =>
    [
        .. context.Decisions.Where(row => row.MeetingId == meeting)
            .Select(row => row.Evidence.UtteranceOrdinal),
        .. context.ActionItems.Where(row => row.MeetingId == meeting)
            .Select(row => row.Evidence.UtteranceOrdinal),
        .. context.OpenQuestions.Where(row => row.MeetingId == meeting)
            .Select(row => row.Evidence.UtteranceOrdinal),
    ];

    /// <summary>
    /// Puts the turns back where the savepoint found them and takes the savepoint off the stack,
    /// without ever becoming the reason the caller hears about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing that goes wrong here is worth saying, and saying it would cost the sentence that
    /// matters. A refusal SQLite answered by aborting the whole transaction — the disk full, an I/O
    /// error — leaves no savepoint to go back to, so the rollback throws <c>no such savepoint</c>,
    /// and thrown from a catch block that is what the report would carry instead of the refusal that
    /// caused it. <c>CorpusRebuild.Why</c> exists so a run never names a meeting with a cause nobody
    /// can act on; this is the door that would put one there.
    /// </para>
    /// <para>
    /// What makes swallowing safe rather than hopeful is what a failure here implies. The savepoint
    /// was taken six lines above on this same transaction, so the only way back to it is gone is
    /// that the transaction is: aborted, completed, or on a connection that has closed. In every one
    /// of those the delete this would have undone cannot be committed either — the caller's
    /// transaction is the one that would have to commit it — so nothing reaches the disk half done,
    /// and the run does not come back clean: a rebuild carries on over a transaction that can no
    /// longer commit and ends loudly at <c>rebuild.Commit()</c>. What is lost is a sentence in the
    /// report, not a meeting's turns. Out of memory is the one refusal let past, for the reason
    /// <c>CorpusRebuild.Absorbable</c> gives: it says nothing about this meeting and carrying on
    /// means attempting the rest of the corpus under the pressure that just refused it.
    /// </para>
    /// <para>
    /// Released as well as rolled back, because SQLite leaves a savepoint on the stack after a
    /// rollback to it — only the ones taken after it are destroyed. Without this every refused
    /// meeting would leave one standing for the rest of the caller's transaction, holding the
    /// statement journal open across the meetings behind it. A release that fails after a rollback
    /// that did not is the same argument again: the transaction is gone, so the stack it would have
    /// leaked into has nowhere to spend the leak.
    /// </para>
    /// </remarks>
    private static void Undo(IDbContextTransaction enclosing)
    {
        try
        {
            enclosing.RollbackToSavepoint(BeforeTheTurnsGo);
            enclosing.ReleaseSavepoint(BeforeTheTurnsGo);
        }
        catch (Exception unrecoverable) when (unrecoverable is not OutOfMemoryException)
        {
            // Deliberately nothing. The refusal on its way out is the one worth having.
        }
    }

    /// <summary>
    /// Every turn of this meeting the context is holding an opinion about, dropped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before the swap and again if it is undone, and not for the same reason both times. Before, it
    /// is the whole of what follows. After, only the turns this swap added can be there — nothing
    /// between the first call and the save reads one, and EF accepts no change when
    /// <c>SaveChanges</c> throws — so the second call is the narrow one, and it is here rather than
    /// left to <c>CorpusRebuild.Discard</c> because three of the four callers have no discard.
    /// </para>
    /// <para>
    /// The rows are deleted straight through rather than through the change tracker, and the
    /// difference is not performance.
    /// Marking a tracked turn deleted makes EF notice that a tracked claim cites it
    /// and refuse in memory — before any SQL runs, and therefore before the deferred foreign keys
    /// that make replacing a turn possible at all get a say. So the tracker is told nothing about
    /// these rows and has to be stopped from holding turns that are no longer there and colliding
    /// with the ones about to arrive under the same positions.
    /// </para>
    /// <para>
    /// Whatever state each one is in, which is wider than the <see cref="EntityState.Added"/> line
    /// <c>CorpusRebuild.Discard</c> holds and is bounded somewhere else instead. That line exists
    /// because <c>Discard</c> sweeps every kind of row on the context, and an artifact is
    /// <see cref="EntityState.Modified"/> exactly when <c>StagedArtifact.Commit</c> has already
    /// moved its file: dropping one abandons a row under a file on disk, the one direction that
    /// whole design refuses. This sweeps one kind, and that kind is a projection nothing edits —
    /// <see cref="Replace"/> is its only writer, a correction reaches the rendered files and never
    /// the stored turn, and the turns that go, go through <c>ExecuteDelete</c>. So
    /// <see cref="EntityState.Modified"/> and <see cref="EntityState.Deleted"/> are not states a
    /// tracked <see cref="Utterance"/> is ever in, and an <see cref="EntityState.Unchanged"/> one
    /// is a read that has to stop being trusted because the row behind it has just been replaced.
    /// Narrowing by state here would name a case that cannot arise and would leave the one that
    /// does — a stale read — held. The two scopes differ because one is bounded by type and the
    /// other cannot be.
    /// </para>
    /// </remarks>
    private static void Forget(CorpusDbContext context, Guid meeting)
    {
        foreach (var tracked in context.ChangeTracker.Entries<Utterance>()
            .Where(entry => entry.Entity.MeetingId == meeting)
            .ToArray())
        {
            tracked.State = EntityState.Detached;
        }
    }

    /// <summary>What the human layer says about this meeting, which is everything the files add.</summary>
    private static TranscriptHeader Header(CorpusDbContext context, Meeting meeting)
    {
        var names = context.SpeakerAssignments
            .Where(assignment => assignment.MeetingId == meeting.Id)
            .Join(
                context.People,
                assignment => assignment.PersonId,
                person => person.Id,
                (assignment, person) => new { assignment.SpeakerLabel, person.DisplayName })
            .ToDictionary(pair => pair.SpeakerLabel, pair => pair.DisplayName, StringComparer.Ordinal);

        return new TranscriptHeader(
            meeting.Id,
            meeting.StartedAt,
            meeting.Language,
            meeting.Title,
            meeting.Context,
            names,
            Corrections(context, meeting.Id));
    }

    /// <summary>
    /// Every correction that reaches this meeting: the global ones, its own, and the ones written
    /// against a node it hangs off or anything above that node.
    /// </summary>
    /// <remarks>
    /// Upwards and not downwards, which is the direction that reads oddly until it is said out
    /// loud: a correction on an organization applies to the work under it, so a meeting linked to a
    /// project inherits its organization's terminology. The walk is bounded by the depth of the
    /// tree, which is why the tree has one.
    /// </remarks>
    private static IReadOnlyList<TerminologyCorrection> Corrections(CorpusDbContext context, Guid meetingId)
    {
        var frontier = context.MeetingNodes
            .Where(link => link.MeetingId == meetingId)
            .Select(link => link.NodeId)
            .ToArray();

        var scope = new List<Guid>(frontier);
        for (var level = 0; level < Node.MaxDepth && frontier.Length > 0; level++)
        {
            var below = frontier;
            frontier = context.Nodes
                .Where(node => below.Contains(node.Id) && node.ParentId != null)
                .Select(node => node.ParentId!.Value)
                .Distinct()
                .ToArray();
            scope.AddRange(frontier);
        }

        return
        [
            .. context.TerminologyCorrections.Where(correction =>
                (correction.NodeId == null && correction.MeetingId == null)
                || correction.MeetingId == meetingId
                || (correction.NodeId != null && scope.Contains(correction.NodeId.Value))),
        ];
    }
}
