using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// The draft the screen that files a meeting holds, and what it would write.
/// </summary>
/// <remarks>
/// No corpus is opened here and none can be: this type turns what is on a screen into the rows a
/// save would make, and every one of those rules is about the shape of an answer rather than about
/// a database. What the rows really do to a corpus is <c>MeetingClassifyingTests</c>.
/// </remarks>
public class MeetingFilingTests
{
    private static readonly Guid Company = Guid.NewGuid();

    private static readonly Guid Project = Guid.NewGuid();

    private static readonly Guid Ticket = Guid.NewGuid();

    private static readonly Guid Jo = Guid.NewGuid();

    [Fact]
    public void A_path_with_nothing_in_it_is_no_link()
    {
        var filing = MeetingFiling.Nothing with
        {
            WorkOf = [ChosenPath.Empty],
            Counterpart = [ChosenPath.Empty],
            About = [ChosenPath.Empty],
        };

        filing.Links.ShouldBeEmpty();
    }

    /// <summary>
    /// §5.3 row 1 and row 13: the class links the course, the support meeting links the ticket, and
    /// what stands above either is reached through the tree without being named.
    /// </summary>
    [Fact]
    public void A_link_is_made_on_the_deepest_node_of_the_path()
    {
        var filing = MeetingFiling.Nothing with
        {
            WorkOf = [new ChosenPath([Company, Project, Ticket])],
        };

        filing.Links.ShouldBe([(Ticket, MeetingNodeRole.WorkOf)]);
    }

    [Fact]
    public void The_same_node_twice_under_one_role_is_one_link()
    {
        var filing = MeetingFiling.Nothing with
        {
            WorkOf = [new ChosenPath([Company]), new ChosenPath([Company])],
        };

        filing.Links.ShouldBe([(Company, MeetingNodeRole.WorkOf)]);
    }

    [Fact]
    public void The_same_node_under_two_roles_is_two_links()
    {
        var filing = MeetingFiling.Nothing with
        {
            WorkOf = [new ChosenPath([Company])],
            Counterpart = [new ChosenPath([Company])],
        };

        filing.Links.ShouldBe(
            [(Company, MeetingNodeRole.WorkOf), (Company, MeetingNodeRole.Counterpart)],
            ignoreOrder: true);
    }

    /// <summary>
    /// Every way a meeting relates to a node has a column on this record, which is what fires the
    /// day a fourth role joins the closed vocabulary.
    /// </summary>
    [Fact]
    public void Every_column_a_link_can_be_drawn_in_is_one_this_record_has()
    {
        foreach (var role in Enum.GetValues<MeetingNodeRole>())
        {
            Should.NotThrow(() => MeetingFiling.Nothing.Column(role), role.ToString());
        }
    }

    [Fact]
    public void A_role_that_is_not_one_of_the_three_has_no_column() =>
        Should.Throw<InvalidOperationException>(
            () => MeetingFiling.Nothing.Column((MeetingNodeRole)99));

    /// <summary>§5.3 row 11, which one column made a choice between and lost.</summary>
    [Fact]
    public void Somebody_who_attended_and_is_the_subject_is_two_rows()
    {
        var filing = MeetingFiling.Nothing with
        {
            Somebody = [new ChosenPerson(Jo, Attended: true, Subject: true)],
        };

        filing.Named.ShouldBe(
            [(Jo, MeetingPersonRole.Attended), (Jo, MeetingPersonRole.Subject)],
            ignoreOrder: true);
    }

    /// <summary>
    /// A third way of being named would go red here rather than being quietly dropped by a filing
    /// that answers for two.
    /// </summary>
    [Fact]
    public void Every_way_somebody_can_be_named_has_a_place_on_a_filing()
    {
        var filing = MeetingFiling.Nothing with
        {
            Somebody = [new ChosenPerson(Jo, Attended: true, Subject: true)],
        };

        filing.Named.Select(named => named.Role)
            .ShouldBe(Enum.GetValues<MeetingPersonRole>(), ignoreOrder: true);
    }

    [Fact]
    public void A_place_nobody_was_put_in_names_nobody()
    {
        var filing = MeetingFiling.Nothing with
        {
            Somebody = [new ChosenPerson(null, Attended: true, Subject: true)],
        };

        filing.Named.ShouldBeEmpty();
    }

    /// <summary>
    /// Both toggles off is somebody on the screen and on no row, which is the same answer an empty
    /// path gives. Named as having attended because their row is there, they would be filed as
    /// present at a meeting somebody deliberately took them off.
    /// </summary>
    [Fact]
    public void Somebody_on_the_screen_who_neither_attended_nor_is_the_subject_is_filed_as_nothing()
    {
        var filing = MeetingFiling.Nothing with
        {
            Somebody = [new ChosenPerson(Jo, Attended: false, Subject: false)],
        };

        filing.Named.ShouldBeEmpty();
    }

    /// <summary>
    /// Read off what would be written and never off the shape. The other way round, every meeting
    /// somebody opened this screen over and pressed a chip on would count as filed.
    /// </summary>
    [Fact]
    public void A_meeting_with_nothing_chosen_is_unclassified()
    {
        MeetingFiling.Nothing.IsUnclassified.ShouldBeTrue();
        MeetingFiling.AsShapedBy(MeetingShape.Daily).IsUnclassified.ShouldBeTrue();

        var filed = MeetingFiling.Nothing with { WorkOf = [new ChosenPath([Company])] };
        filed.IsUnclassified.ShouldBeFalse();
    }

    [Fact]
    public void Choosing_a_shape_opens_its_slots_and_files_nothing()
    {
        var filing = MeetingFiling.AsShapedBy(MeetingShape.SellingToAClient);

        filing.Shape.ShouldBe(MeetingShape.SellingToAClient);
        filing.WorkOf.ShouldBe([ChosenPath.Empty]);
        filing.Counterpart.ShouldBe([ChosenPath.Empty]);
        filing.About.ShouldBeEmpty();
        filing.Somebody.ShouldBeEmpty();
        filing.Links.ShouldBeEmpty();
        filing.Named.ShouldBeEmpty();
    }

    /// <summary>
    /// Choosing a shape replaces the columns rather than adding to them, which is what the card's
    /// <em>what each one fills is seen when it is chosen</em> asks for.
    /// </summary>
    [Fact]
    public void Choosing_a_shape_replaces_whatever_was_there()
    {
        var filing = MeetingFiling.AsShapedBy(MeetingShape.Conference);

        filing.WorkOf.ShouldBeEmpty();
        filing.About.ShouldBe([ChosenPath.Empty]);
    }

    [Fact]
    public void A_shape_that_puts_somebody_on_the_meeting_opens_a_place_with_nobody_in_it()
    {
        var slot = MeetingFiling.AsShapedBy(MeetingShape.HumanResources).Somebody.ShouldHaveSingleItem();

        slot.PersonId.ShouldBeNull();
        slot.Attended.ShouldBeFalse();
        slot.Subject.ShouldBeTrue();
    }
}
