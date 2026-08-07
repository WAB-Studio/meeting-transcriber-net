using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// Where a node sits is the tree's to work out. The same rules are constraints in the schema —
/// that is what stops an import or a repair script — and these are what stop the application
/// building the row at all, while the message can still say which node and why.
/// </summary>
public class ClassificationTests
{
    private static readonly UtcTimestamp Now = At(2026, 8, 6);

    /// <summary>Three instants a year apart, for the affiliations that begin and end between them.</summary>
    private static readonly UtcTimestamp Joined = At(2024, 3, 1);

    private static readonly UtcTimestamp Left = At(2025, 6, 30);

    [Fact]
    public void A_root_is_at_the_top_with_nothing_above_it()
    {
        var organization = Root(NodeKind.Organization, "TechSed");

        organization.Depth.ShouldBe(0);
        organization.ParentId.ShouldBeNull();
        organization.ParentKind.ShouldBeNull();
        organization.ParentDepth.ShouldBeNull();
    }

    /// <summary>
    /// Work belonging to nobody in particular is ordinary — a side project, a course nobody is
    /// paying for — and inventing an owner for it would be worse than having none.
    /// </summary>
    [Fact]
    public void Work_with_no_organization_over_it_is_a_root_of_its_own()
    {
        Should.NotThrow(() => Root(NodeKind.Initiative, "an unpaid side project"));
    }

    [Fact]
    public void A_topic_is_never_a_root_because_it_would_be_a_subject_of_nothing()
    {
        Should.Throw<ClassificationException>(() => Root(NodeKind.Topic, "an incident"));
    }

    /// <summary>
    /// The depth and the copy of the parent are taken from the parent, never passed in. The
    /// importer used to compute them, and hardcoded them as zero or one — which would have put
    /// the first third-level node in at the wrong depth.
    /// </summary>
    [Fact]
    public void A_child_takes_its_place_from_the_parent_and_not_from_the_caller()
    {
        var organization = Root(NodeKind.Organization, "TechSed");
        var initiative = Under(organization, NodeKind.Initiative, "Coati");
        var topic = Under(initiative, NodeKind.Topic, "the outage on Friday");

        initiative.Depth.ShouldBe(1);
        initiative.ParentId.ShouldBe(organization.Id);
        initiative.ParentKind.ShouldBe(NodeKind.Organization);
        initiative.ParentDepth.ShouldBe(0);

        topic.Depth.ShouldBe(2);
        topic.ParentDepth.ShouldBe(1);
        topic.ParentKind.ShouldBe(NodeKind.Initiative);
    }

    [Theory]
    [InlineData(NodeKind.Organization, NodeKind.Organization)]
    [InlineData(NodeKind.Organization, NodeKind.Topic)]
    [InlineData(NodeKind.Initiative, NodeKind.Organization)]
    [InlineData(NodeKind.Initiative, NodeKind.Initiative)]
    public void The_classes_go_organization_initiative_topic_and_no_other_way(NodeKind parent, NodeKind child)
    {
        var root = Root(NodeKind.Organization, "TechSed");
        var under = parent is NodeKind.Organization ? root : Under(root, NodeKind.Initiative, "Coati");

        Should.Throw<ClassificationException>(() => Under(under, child, "out of order"));
    }

    [Fact]
    public void Nothing_hangs_off_a_topic_at_all()
    {
        var topic = Under(Under(Root(NodeKind.Organization, "TechSed"), NodeKind.Initiative, "Coati"), NodeKind.Topic, "an outage");

        foreach (var kind in Enum.GetValues<NodeKind>())
        {
            Should.Throw<ClassificationException>(() => Under(topic, kind, "too deep"));
        }
    }

    [Fact]
    public void A_node_without_a_name_is_not_a_node()
    {
        Should.Throw<ArgumentException>(() => Root(NodeKind.Organization, "  "));
        Should.Throw<ArgumentException>(() => Under(Root(NodeKind.Organization, "TechSed"), NodeKind.Initiative, ""));
    }

    /// <summary>
    /// Somebody belongs to an organization. A project and a ticket are places work happens, not
    /// places people belong to, and the class is carried alongside the id so the key can say so too.
    /// </summary>
    [Fact]
    public void Somebody_is_at_an_organization_and_nothing_else()
    {
        var organization = Root(NodeKind.Organization, "TechSed");
        var initiative = Under(organization, NodeKind.Initiative, "Coati");
        var person = Somebody();

        var affiliation = Affiliation.At(Guid.NewGuid(), person, organization, Now);
        affiliation.PersonId.ShouldBe(person.Id);
        affiliation.OrganizationId.ShouldBe(organization.Id);
        affiliation.OrganizationKind.ShouldBe(NodeKind.Organization);

        Should.Throw<ClassificationException>(() => Affiliation.At(Guid.NewGuid(), person, initiative, Now));
    }

    /// <summary>
    /// The case the single column could not hold. Both are open, both are true, and neither is a
    /// row somebody had to invent to say the other.
    /// </summary>
    [Fact]
    public void Somebody_can_be_at_two_organizations_at_once()
    {
        var person = Somebody();
        var one = Affiliation.At(Guid.NewGuid(), person, Root(NodeKind.Organization, "TechSed"), Now);
        var other = Affiliation.At(Guid.NewGuid(), person, Root(NodeKind.Organization, "A Client"), Now);

        one.Held(Now).ShouldBeTrue();
        other.Held(Now).ShouldBeTrue();
    }

    /// <summary>
    /// An affiliation with no dates held then and holds now: a corpus that never learned the dates
    /// is the ordinary case — it is what the legacy import produces — and not a broken one.
    /// </summary>
    [Fact]
    public void An_affiliation_open_at_both_ends_holds_whenever_it_is_asked()
    {
        var affiliation = Affiliation.At(Guid.NewGuid(), Somebody(), Root(NodeKind.Organization, "TechSed"), Now);

        affiliation.Held(Joined).ShouldBeTrue();
        affiliation.Held(Left).ShouldBeTrue();
        affiliation.Held(Now).ShouldBeTrue();
    }

    /// <summary>
    /// Why the period is there at all: reading a meeting from the year they were somewhere else has
    /// to give the answer that was true then, not the one that is true now.
    /// </summary>
    [Fact]
    public void An_affiliation_that_ended_stops_holding_the_moment_it_did()
    {
        var affiliation = Affiliation.At(
            Guid.NewGuid(), Somebody(), Root(NodeKind.Organization, "TechSed"), Now, from: Joined, until: Left);

        affiliation.Held(At(2024, 2, 29)).ShouldBeFalse();
        affiliation.Held(Joined).ShouldBeTrue();
        affiliation.Held(At(2025, 6, 29)).ShouldBeTrue();
        // Half open, so the day somebody moves belongs to where they went and not to both.
        affiliation.Held(Left).ShouldBeFalse();
        affiliation.Held(Now).ShouldBeFalse();
    }

    [Fact]
    public void An_affiliation_cannot_end_before_it_started()
    {
        var affiliation = Affiliation.At(
            Guid.NewGuid(), Somebody(), Root(NodeKind.Organization, "TechSed"), Now, from: Left);

        Should.Throw<ClassificationException>(() => affiliation.Ended(Joined));
    }

    private static UtcTimestamp At(int year, int month, int day) =>
        UtcTimestamp.From(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    private static Person Somebody() =>
        new() { Id = Guid.NewGuid(), DisplayName = "Renée", CreatedAt = Now, UpdatedAt = Now };

    private static Node Root(NodeKind kind, string name) => Node.Root(Guid.NewGuid(), kind, name, Now);

    private static Node Under(Node parent, NodeKind kind, string name) =>
        Node.Under(Guid.NewGuid(), parent, kind, name, Now);
}
