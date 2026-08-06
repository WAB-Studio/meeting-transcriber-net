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
    private static readonly UtcTimestamp Now =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));

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
    /// Where somebody works is an organization. A project and a ticket are places work happens,
    /// not employers, and the class is carried alongside the id so the key can say so too.
    /// </summary>
    [Fact]
    public void A_person_works_at_an_organization_and_nothing_else()
    {
        var organization = Root(NodeKind.Organization, "TechSed");
        var initiative = Under(organization, NodeKind.Initiative, "Coati");
        var person = new Person { Id = Guid.NewGuid(), DisplayName = "Renée", CreatedAt = Now, UpdatedAt = Now };

        person.WorksAt(organization);
        person.OrganizationId.ShouldBe(organization.Id);
        person.OrganizationKind.ShouldBe(NodeKind.Organization);

        Should.Throw<ClassificationException>(() => person.WorksAt(initiative));
    }

    [Fact]
    public void Somebody_who_works_nowhere_carries_neither_half_of_the_key()
    {
        var person = new Person { Id = Guid.NewGuid(), DisplayName = "Renée", CreatedAt = Now, UpdatedAt = Now };
        person.WorksAt(Root(NodeKind.Organization, "TechSed"));

        person.WorksAt(null);

        person.OrganizationId.ShouldBeNull();
        person.OrganizationKind.ShouldBeNull();
    }

    private static Node Root(NodeKind kind, string name) => Node.Root(Guid.NewGuid(), kind, name, Now);

    private static Node Under(Node parent, NodeKind kind, string name) =>
        Node.Under(Guid.NewGuid(), parent, kind, name, Now);
}
