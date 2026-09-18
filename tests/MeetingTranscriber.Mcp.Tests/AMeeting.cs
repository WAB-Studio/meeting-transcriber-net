using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Mcp.Tests;

/// <summary>
/// The meeting every fact in this suite asks the server about: five turns, the response they were
/// read out of, and one accepted extraction citing the turn the searchable words are in.
/// </summary>
/// <remarks>
/// The rows themselves are <see cref="MeetingRows"/>'s, which is where a thing two suites open
/// lives. What is here is this suite's own answer to <em>which</em> meeting — the words a search
/// has to find and the turn a citation has to reach — because those are what the assertions below
/// are written against.
/// </remarks>
internal static class AMeeting
{
    internal const string ResponseSha256 =
        "1f2e3d4c5b6a79881f2e3d4c5b6a79881f2e3d4c5b6a79881f2e3d4c5b6a7988";

    internal const string Said = "el presupuesto del cliente queda aprobado";

    internal const string Decided = "queda aprobado el presupuesto";

    internal const string Title = "Reunión con el cliente";

    /// <summary>The turn <see cref="Said"/> is in, and the one every citation anchors on.</summary>
    internal const int Cited = 2;

    internal static UtcTimestamp StartedAt { get; } =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    internal static Guid RecordedIn(CorpusDbContext context, UtcTimestamp? startedAt = null)
    {
        var when = startedAt ?? StartedAt;

        var meeting = MeetingRows.Recorded(
            context,
            when,
            ["turno 0", "turno 1", Said, "turno 3", "turno 4"],
            Title,
            ResponseSha256);

        MeetingRows.Extracted(context, meeting, when, accepted: when, Decided, Cited);

        return meeting;
    }

    /// <summary>The tree this suite's meeting is filed under: an organization, the work inside it,
    /// and the subject inside that, with the meeting linked to the deepest one.</summary>
    internal static (Guid Organization, Guid Initiative, Guid Topic) FiledUnder(
        CorpusDbContext context, Guid meeting)
    {
        var human = new HumanLayer(context, StartedAt);
        var organization = human.Root(NodeKind.Organization, "acme");
        var initiative = human.Under(organization, NodeKind.Initiative, "migración");
        var topic = human.Under(initiative, NodeKind.Topic, "presupuesto");

        human.Link(meeting, topic, MeetingNodeRole.WorkOf);

        return (organization.Id, initiative.Id, topic.Id);
    }
}
