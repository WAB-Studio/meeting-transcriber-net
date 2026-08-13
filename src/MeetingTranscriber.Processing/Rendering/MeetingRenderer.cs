using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;

using Microsoft.EntityFrameworkCore;

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

        // Both or neither: a transcript naming turns the jsonl does not have is a meeting that
        // reads as two different meetings depending on which file somebody opened. A caller already
        // inside a transaction keeps its own — a rebuild wraps every meeting in one, and opening a
        // second here would throw rather than nest.
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
    /// Reads the response and puts the turns back. The rows go before the new ones arrive rather
    /// than in one statement, because the position of a turn in its meeting is unique and the two
    /// generations share every one of them.
    /// </summary>
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

        // Deleted straight through rather than through the change tracker, and the difference is
        // not performance. Marking a tracked turn deleted makes EF notice that a tracked claim
        // cites it and refuse in memory — before any SQL runs, and therefore before the deferred
        // foreign keys that make replacing a turn possible at all get a say. Detaching first is
        // what stops the tracker holding turns that are no longer there and colliding with the
        // ones about to arrive under the same positions.
        foreach (var tracked in context.ChangeTracker.Entries<Utterance>()
            .Where(entry => entry.Entity.MeetingId == meeting.Id)
            .ToArray())
        {
            tracked.State = EntityState.Detached;
        }

        context.Utterances.Where(turn => turn.MeetingId == meeting.Id).ExecuteDelete();

        foreach (var turn in turns)
        {
            context.Utterances.Add(new Utterance
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting.Id,
                Ordinal = turn.Ordinal,
                Start = turn.Start,
                End = turn.End,
                Channel = turn.Channel,
                SpeakerLabel = turn.SpeakerLabel,
                // The words the provider returned. Corrections are applied to the rendered files
                // and never here: this row is what a citation is checked against, and a quote that
                // has been silently corrected no longer matches the evidence it claims to be from.
                Text = turn.Text,
                Confidence = turn.Confidence,
            });
        }

        context.SaveChanges();
        return turns;
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
