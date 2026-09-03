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

    [Fact]
    public void Choosing_a_shape_opens_its_slots_and_files_nothing()
    {
        var filing = MeetingFiling.Nothing.ShapedBy(MeetingShape.SellingToAClient);

        filing.Shape.ShouldBe(MeetingShape.SellingToAClient);
        filing.WorkOf.ShouldBe([ChosenPath.Empty]);
        filing.Counterpart.ShouldBe([ChosenPath.Empty]);
        filing.About.ShouldBeEmpty();
        filing.Somebody.ShouldBeEmpty();
        filing.Links.ShouldBeEmpty();
        filing.Named.ShouldBeEmpty();
    }

    /// <summary>
    /// A second shape leaves the first one's empty places behind, so pressing chips until one fits
    /// shows what that one asks for and nothing accumulated on the way.
    /// </summary>
    [Fact]
    public void A_second_shape_shows_what_it_asks_for_and_not_the_one_before_it()
    {
        var filing = MeetingFiling.Nothing
            .ShapedBy(MeetingShape.TwoProjects)
            .ShapedBy(MeetingShape.Conference);

        filing.WorkOf.ShouldBeEmpty();
        filing.About.ShouldBe([ChosenPath.Empty]);
    }

    /// <summary>
    /// A shape never takes an answer away, and this is the finding it exists against.
    /// </summary>
    /// <remarks>
    /// Nothing on the screen lights the chip a meeting was filed under, because a shape is a
    /// pre-fill and no meeting carries which one it was. So somebody coming back to a filed meeting
    /// has every reason to press the chip they used last time — and if that press replaced the
    /// draft, it would wipe every path and every person the corpus holds, one <em>Guardar</em> from
    /// permanent.
    /// </remarks>
    [Fact]
    public void A_shape_pressed_over_a_filing_that_is_already_there_takes_nothing_away()
    {
        var filed = MeetingFiling.Nothing with
        {
            WorkOf = [new ChosenPath([Company, Project])],
            Counterpart = [new ChosenPath([Ticket])],
            Somebody = [new ChosenPerson(Jo, Attended: true, Subject: true)],
        };

        var after = filed.ShapedBy(MeetingShape.Daily);

        Down(after.WorkOf).ShouldBe(Down(filed.WorkOf));
        Down(after.Counterpart).ShouldBe(Down(filed.Counterpart));
        after.Somebody.ShouldBe(filed.Somebody);
        after.Links.ShouldBe(filed.Links, ignoreOrder: true);
        after.Named.ShouldBe(filed.Named, ignoreOrder: true);
    }

    /// <summary>
    /// It opens what the story still needs on top of what is answered, and never fewer places than
    /// are already filled.
    /// </summary>
    [Fact]
    public void A_shape_opens_the_places_a_filing_is_still_short_of()
    {
        var filed = MeetingFiling.Nothing with { WorkOf = [new ChosenPath([Company])] };

        // Two projects wants two and one is answered, so one more place opens.
        Down(filed.ShapedBy(MeetingShape.TwoProjects).WorkOf).ShouldBe([[Company], []]);

        // A conference wants none, and the one that is answered stays.
        Down(filed.ShapedBy(MeetingShape.Conference).WorkOf).ShouldBe([[Company]]);
    }

    [Fact]
    public void A_shape_that_puts_somebody_on_the_meeting_opens_a_place_with_nobody_in_it()
    {
        var slot = MeetingFiling.Nothing
            .ShapedBy(MeetingShape.HumanResources)
            .Somebody
            .ShouldHaveSingleItem();

        slot.PersonId.ShouldBeNull();
        slot.Subject.ShouldBeTrue();
    }

    /// <summary>
    /// Replacing one column leaves the other two alone.
    /// </summary>
    /// <remarks>
    /// The write half of what <see cref="MeetingFiling.Column"/> reads, and the reason it is here:
    /// the screen mutates a filing through nothing else, and swapping two arms of that table would
    /// file <em>es trabajo de TechSed</em> as <em>trata sobre TechSed</em> with every other test in
    /// the solution still green.
    /// </remarks>
    [Theory]
    [InlineData(MeetingNodeRole.WorkOf)]
    [InlineData(MeetingNodeRole.Counterpart)]
    [InlineData(MeetingNodeRole.About)]
    public void One_column_is_replaced_and_the_others_are_left_as_they_were(MeetingNodeRole role)
    {
        var filing = new MeetingFiling(
            null,
            [new ChosenPath([Company])],
            [new ChosenPath([Project])],
            [new ChosenPath([Ticket])],
            []);

        var after = filing.With(role, [new ChosenPath([Jo])]);

        Down(after.Column(role)).ShouldBe([[Jo]]);

        foreach (var other in Enum.GetValues<MeetingNodeRole>().Where(found => found != role))
        {
            Down(after.Column(other)).ShouldBe(Down(filing.Column(other)), customMessage: other.ToString());
        }
    }

    [Fact]
    public void A_column_that_is_not_one_of_the_three_cannot_be_replaced() =>
        Should.Throw<InvalidOperationException>(
            () => MeetingFiling.Nothing.With((MeetingNodeRole)99, [ChosenPath.Empty]));

    /// <summary>
    /// Turning one way of naming somebody over leaves the other where it was.
    /// </summary>
    /// <remarks>
    /// The write half of <see cref="ChosenPerson.Carries"/>, here for the same reason: the badges
    /// on the screen go through nothing else, and two arms the wrong way round would put
    /// <em>estuvo</em> on the control that says the meeting is about them.
    /// </remarks>
    [Theory]
    [InlineData(MeetingPersonRole.Attended)]
    [InlineData(MeetingPersonRole.Subject)]
    public void One_way_of_being_named_is_turned_over_and_the_other_is_left_alone(MeetingPersonRole role)
    {
        var place = new ChosenPerson(Jo, Attended: false, Subject: false);
        var after = place.Flipped(role);

        after.Carries(role).ShouldBeTrue();
        after.Flipped(role).ShouldBe(place);

        foreach (var other in Enum.GetValues<MeetingPersonRole>().Where(found => found != role))
        {
            after.Carries(other).ShouldBeFalse(other.ToString());
        }
    }

    [Fact]
    public void A_way_of_being_named_that_is_not_one_of_the_two_is_refused()
    {
        var place = new ChosenPerson(Jo, Attended: true, Subject: false);

        Should.Throw<InvalidOperationException>(() => place.Carries((MeetingPersonRole)99));
        Should.Throw<InvalidOperationException>(() => place.Flipped((MeetingPersonRole)99));
    }

    [Fact]
    public void A_place_added_by_hand_has_nobody_in_it_and_says_they_were_there()
    {
        ChosenPerson.NobodyYet.PersonId.ShouldBeNull();
        ChosenPerson.NobodyYet.Carries(MeetingPersonRole.Attended).ShouldBeTrue();
        ChosenPerson.NobodyYet.Carries(MeetingPersonRole.Subject).ShouldBeFalse();
    }

    /// <summary>
    /// A column as the ids in each of its paths, which is what an assertion about one has to
    /// compare.
    /// </summary>
    /// <remarks>
    /// A <c>ChosenPath</c> is a record over a list, and a record compares a list member by
    /// reference — so two paths holding the same ids are not equal and an assertion written the
    /// obvious way fails over nothing. Nothing in the application compares two paths; every test
    /// that looks like it does goes through here instead, and says so.
    /// </remarks>
    private static IReadOnlyList<Guid[]> Down(IReadOnlyList<ChosenPath> column) =>
        [.. column.Select(path => path.Nodes.ToArray())];
}
