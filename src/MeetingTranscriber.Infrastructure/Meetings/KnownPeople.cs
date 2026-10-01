using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Meetings;

/// <summary>
/// Every person the corpus holds with what a typed name is read against, and the organizations the
/// meeting being filed already stands under.
/// </summary>
/// <param name="Organizations">
/// The organization at the top of everything the meeting is filed under, as the screen holds it now.
/// </param>
public sealed record PeopleAround(IReadOnlyList<KnownPerson> Known, IReadOnlyList<Guid> Organizations);

/// <summary>
/// The corpus half of <see cref="WhoTheyMightBe"/>: the rows its two signals are read from. It
/// writes nothing and every read is no-tracking.
/// </summary>
/// <remarks>
/// <para>
/// <em>Here</em> is this meeting as the screen holds it, and not as it was last saved: its stored
/// people (named on it, or a voice settled onto them) plus whoever the unsaved draft already names,
/// and its stored links plus the nodes of the draft. Somebody added a moment ago on the screen and
/// not yet saved is as much on this meeting as somebody saved last week, and a signal that read only
/// stored rows would go quiet exactly while the form is being filled.
/// </para>
/// <para>
/// The person using this install counts on neither side of having met somebody. The microphone is
/// settled onto them on nearly every recorded meeting, so counting them would make the signal mean
/// <em>has been on a meeting</em>, which every person is.
/// </para>
/// <para>
/// An affiliation counts whenever it was: ended or not, and in any period. Where somebody has
/// belonged is the evidence of who they are, and a name typed today for somebody who left an
/// organization two years ago is still that somebody.
/// </para>
/// </remarks>
public static class KnownPeople
{
    public static PeopleAround Around(CorpusDbContext context, OpenedOver over)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(over);

        var people = context.People.AsNoTracking().ToList();

        var organizationsOf = context.Affiliations.AsNoTracking()
            .Select(affiliation => new { affiliation.PersonId, affiliation.OrganizationId })
            .ToList()
            .GroupBy(affiliation => affiliation.PersonId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Guid>)[.. group.Select(affiliation => affiliation.OrganizationId).Distinct()]);

        var appearances = Appearances(context);
        var me = new HumanLayer(context, TimeProvider.System).Me()?.Id;

        var here = appearances
            .Where(seen => seen.Meeting == over.Meeting)
            .Select(seen => seen.Person)
            .Concat(over.PeopleOnTheDraft)
            .Where(person => person != me)
            .ToHashSet();

        var withSomebodyHere = appearances
            .Where(seen => seen.Meeting != over.Meeting && seen.Person != me)
            .GroupBy(seen => seen.Meeting)
            .SelectMany(meeting =>
            {
                var present = meeting.Select(seen => seen.Person).ToHashSet();
                return present.Where(person => present.Any(other => other != person && here.Contains(other)));
            })
            .ToHashSet();

        return new PeopleAround(
            [
                .. people.Select(person => new KnownPerson(
                    person,
                    organizationsOf.GetValueOrDefault(person.Id) ?? [],
                    withSomebodyHere.Contains(person.Id))),
            ],
            OrganizationsAbove(context, over));
    }

    /// <summary>Who is on which meeting, by being named on it or by a voice settled onto them.</summary>
    private static List<(Guid Meeting, Guid Person)> Appearances(CorpusDbContext context) =>
    [
        .. context.MeetingPeople.AsNoTracking()
            .Select(named => new { named.MeetingId, named.PersonId })
            .ToList()
            .Select(named => (named.MeetingId, named.PersonId)),
        .. context.SpeakerAssignments.AsNoTracking()
            .Select(voice => new { voice.MeetingId, voice.PersonId })
            .ToList()
            .Select(voice => (voice.MeetingId, voice.PersonId)),
    ];

    private static List<Guid> OrganizationsAbove(CorpusDbContext context, OpenedOver over)
    {
        var nodes = context.Nodes.AsNoTracking().ToDictionary(node => node.Id);

        var filedUnder = context.MeetingNodes.AsNoTracking()
            .Where(link => link.MeetingId == over.Meeting)
            .Select(link => link.NodeId)
            .ToList()
            .Concat(over.NodesOnTheDraft);

        return
        [
            .. filedUnder
                .Where(nodes.ContainsKey)
                .Select(id => RootOf(nodes, id))
                .Where(root => root.Kind is NodeKind.Organization)
                .Select(root => root.Id)
                .Distinct(),
        ];
    }

    private static Node RootOf(Dictionary<Guid, Node> nodes, Guid id)
    {
        var node = nodes[id];

        // The tree is three levels at most and the database refuses a cycle, so this ends.
        while (node.ParentId is { } parent && nodes.TryGetValue(parent, out var above))
        {
            node = above;
        }

        return node;
    }
}
