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
/// the rebuild's business, not this one's.
/// </para>
/// </remarks>
public static class MeetingRenderer
{
    public static RenderedMeeting Render(CorpusDbContext context, Guid meetingId, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var meeting = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new RenderException($"There is no meeting {meetingId} to render.");
        var turns = Project(context, meeting);
        var header = Header(context, meeting);
        var rendered = TranscriptRenderer.Render(header, turns);

        // Both or neither, and only while this opens the transaction. A transcript naming turns the
        // jsonl does not have is a meeting that reads as two different meetings depending on which
        // file somebody opened, and a caller with none of its own is held to that here. A caller
        // already inside a transaction keeps its own — a rebuild wraps the whole corpus in one, and
        // opening a second here would throw rather than nest — so on that path this pairing is not
        // enforced at all: the transcript's row lands in the caller's transaction and the jsonl
        // refused after it leaves the meeting holding one file of each generation, which nothing
        // reports, because every row still agrees with the file it names. That hole is older than
        // the savepoint below and wider than it: DurableArtifact.WriteText stages and commits in
        // one breath, so the first file has moved before the second is written anywhere. Closing it
        // means staging both and committing both, and it is its own card.
        using var write = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;
        var transcript = DurableArtifact.WriteText(
            context,
            meeting.Id,
            ArtifactKind.Transcript,
            CorpusFiles.PathFor(meeting.Id, "transcript.md"),
            now,
            rendered.Markdown);
        var utterances = DurableArtifact.WriteText(
            context,
            meeting.Id,
            ArtifactKind.Utterances,
            CorpusFiles.PathFor(meeting.Id, "utterances.jsonl"),
            now,
            rendered.Jsonl);
        write?.Commit();

        return new RenderedMeeting(turns.Count, transcript, utterances);
    }

    /// <summary>
    /// Reads the response and puts the meeting's turns back, all of them or none of them.
    /// </summary>
    /// <remarks>
    /// The rows and nothing else. The two files are written after this returns and are outside what
    /// it promises — <see cref="Render"/> says what a meeting refused at that point is left holding.
    /// </remarks>
    private static IReadOnlyList<Turn> Project(CorpusDbContext context, Meeting meeting)
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
    /// way.
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
        }
        catch
        {
            Undo(enclosing);
            Forget(context, meeting);
            throw;
        }

        enclosing.ReleaseSavepoint(BeforeTheTurnsGo);
        own?.Commit();
    }

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
    /// Released as well as rolled back, because SQLite leaves a savepoint on the stack after a
    /// rollback to it — only the ones taken after it are destroyed. Without this every refused
    /// meeting would leave one standing for the rest of the caller's transaction, holding the
    /// statement journal open across the meetings behind it.
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
    /// Before the swap and again if it is undone, and for the same reason both times: the rows are
    /// deleted straight through rather than through the change tracker, and the difference is not
    /// performance. Marking a tracked turn deleted makes EF notice that a tracked claim cites it
    /// and refuse in memory — before any SQL runs, and therefore before the deferred foreign keys
    /// that make replacing a turn possible at all get a say. So the tracker is told nothing about
    /// these rows and has to be stopped from holding turns that are no longer there and colliding
    /// with the ones about to arrive under the same positions.
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
