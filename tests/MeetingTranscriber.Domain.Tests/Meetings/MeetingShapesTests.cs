using System.Reflection;

using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// The thirteen meetings <c>arquitectura.md</c> §5.3 lists, as what choosing one opens.
/// </summary>
/// <remarks>
/// The table is the same thirteen <c>ClassificationStoriesTests</c> writes into a corpus, read from
/// the other end: there the story is stored and found again, here it is what a person is handed
/// before they have answered anything. A number that disagrees is a column somebody would have to
/// invent an answer for, or one they cannot say the thing the story says.
/// </remarks>
public class MeetingShapesTests
{
    /// <summary>
    /// Every shape against §5.3, column by column. The rows are the stories and the numbers are how
    /// many empty places each one puts on the screen.
    /// </summary>
    [Theory]
    [InlineData(MeetingShape.Class, 1, 0, 0)]
    [InlineData(MeetingShape.CasualCatchUp, 0, 0, 0)]
    [InlineData(MeetingShape.InterviewAsCandidate, 0, 1, 0)]
    [InlineData(MeetingShape.InterviewAsInterviewer, 1, 0, 0)]
    [InlineData(MeetingShape.TwoProjects, 2, 0, 0)]
    [InlineData(MeetingShape.SellingToAClient, 1, 1, 0)]
    [InlineData(MeetingShape.TeamMeeting, 1, 0, 0)]
    [InlineData(MeetingShape.Conference, 0, 0, 1)]
    [InlineData(MeetingShape.BetweenTwoCompanies, 0, 2, 0)]
    [InlineData(MeetingShape.HumanResources, 1, 0, 0)]
    [InlineData(MeetingShape.RecurringOneToOne, 1, 0, 0)]
    [InlineData(MeetingShape.Daily, 1, 0, 0)]
    [InlineData(MeetingShape.AfterSalesSupport, 1, 1, 0)]
    [InlineData(MeetingShape.FilledByHand, 0, 0, 0)]
    public void Every_shape_opens_what_the_thirteen_stories_need(
        MeetingShape shape, int workOf, int counterpart, int about)
    {
        var opens = MeetingShapes.Opens(shape);

        opens.WorkOf.ShouldBe(workOf);
        opens.Counterpart.ShouldBe(counterpart);
        opens.About.ShouldBe(about);
    }

    /// <summary>
    /// The seven stories that put somebody on the meeting, and how each of them is named.
    /// </summary>
    /// <remarks>
    /// Row 10 is the one worth having a row for: the person a dismissal is about was not in the
    /// room, so the slot opens with the subject toggle on and the attended one off. A screen that
    /// opened it the other way round would file somebody as having been at a meeting they were
    /// deliberately kept out of.
    /// </remarks>
    [Theory]
    [InlineData(MeetingShape.Class, true, false)]
    [InlineData(MeetingShape.InterviewAsCandidate, true, false)]
    [InlineData(MeetingShape.InterviewAsInterviewer, true, false)]
    [InlineData(MeetingShape.SellingToAClient, true, false)]
    [InlineData(MeetingShape.TeamMeeting, true, false)]
    [InlineData(MeetingShape.HumanResources, false, true)]
    [InlineData(MeetingShape.RecurringOneToOne, true, true)]
    public void A_shape_that_puts_somebody_on_the_meeting_opens_one_place_named_the_way_the_story_names_them(
        MeetingShape shape, bool attended, bool subject)
    {
        var slot = MeetingShapes.Opens(shape).Somebody.ShouldHaveSingleItem();

        slot.Attended.ShouldBe(attended);
        slot.Subject.ShouldBe(subject);
    }

    [Fact]
    public void Every_shape_a_meeting_can_be_filed_under_has_an_answer()
    {
        foreach (var shape in Enum.GetValues<MeetingShape>())
        {
            Should.NotThrow(() => MeetingShapes.Opens(shape), shape.ToString());
        }
    }

    [Fact]
    public void A_shape_that_is_not_one_of_the_fourteen_is_refused() =>
        Should.Throw<InvalidOperationException>(() => MeetingShapes.Opens((MeetingShape)99));

    /// <summary>
    /// Nothing a shape opens is filled in.
    /// </summary>
    /// <remarks>
    /// This is a check about the two types and not about the values in them, and it has to stay one.
    /// §5.3 says outright that a template <em>siempre va a pre-llenar nada más</em> — it opens
    /// places and never answers one — so what would break it is not a wrong id in a row of the
    /// table but somebody giving <see cref="ShapeOpens"/> or <see cref="PersonSlot"/> somewhere for
    /// an id to live, and then pre-filling an organization off whoever is using this install.
    /// Reflection is the only way to say that; weakened into a walk over the fourteen asserting the
    /// ids are empty, it would prove nothing the day the field exists and is filled in.
    /// </remarks>
    [Fact]
    public void Nothing_a_shape_opens_is_filled_in()
    {
        var carries = new[] { typeof(ShapeOpens), typeof(PersonSlot) }
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => Mentions(property.PropertyType))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToArray();

        carries.ShouldBeEmpty(
            "a shape opens places and never fills one, so nothing it hands back has room for the "
            + "id of a node or a person: " + string.Join("; ", carries));
    }

    /// <summary>
    /// Every kind of meeting says what it calls each level it opens and each person it puts on, and
    /// the two agree with <see cref="MeetingShapes.Opens"/> on how many.
    /// </summary>
    /// <remarks>
    /// Names that fell behind the places they name are a pill drawn with the generic word over a
    /// level the meeting has its own for, or a place for somebody called by no name at all.
    /// </remarks>
    [Fact]
    public void Every_shape_names_every_level_it_opens_and_every_place_for_somebody()
    {
        foreach (var shape in Enum.GetValues<MeetingShape>().Where(shape => shape is not MeetingShape.FilledByHand))
        {
            var opens = MeetingShapes.Opens(shape);
            var names = MeetingShapes.Names(shape);

            foreach (var role in Enum.GetValues<MeetingNodeRole>())
            {
                names.Levels.ContainsKey(role).ShouldBe(opens.Paths(role) > 0, $"{shape} {role}");

                if (names.Levels.TryGetValue(role, out var levels))
                {
                    levels.ShouldNotBeEmpty($"{shape} {role}");
                    levels.ShouldBeUnique($"{shape} {role}");
                }
            }

            names.Somebody.ShouldBe(opens.Somebody.Select(slot => slot.Name), $"{shape}");
        }
    }

    /// <summary>
    /// A name that is somebody is never a class of node, and every other name is one.
    /// </summary>
    /// <remarks>
    /// <see cref="MeetingShapes.Holds"/> is the only thing that turns a word on the screen into what
    /// naming a new one there would write, and a person's name answering with a node class would put
    /// a <em>Profesor</em> into the tree.
    /// </remarks>
    [Fact]
    public void A_name_for_somebody_is_never_a_node()
    {
        PlaceName[] somebody =
            [PlaceName.Person, PlaceName.Teacher, PlaceName.Interviewer, PlaceName.Candidate, PlaceName.Contact];

        foreach (var name in Enum.GetValues<PlaceName>())
        {
            if (somebody.Contains(name))
            {
                Should.Throw<ArgumentOutOfRangeException>(() => MeetingShapes.Holds(name), name.ToString());
            }
            else
            {
                Should.NotThrow(() => MeetingShapes.Holds(name), name.ToString());
            }
        }

        Should.Throw<ArgumentOutOfRangeException>(() => MeetingShapes.Holds((PlaceName)99));
    }

    /// <summary>
    /// Nothing lit, or <em>Ninguna — la lleno yo</em>, names every level with the generic words.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(MeetingShape.FilledByHand)]
    public void With_no_shape_lit_the_generic_names_stand(MeetingShape? shape)
    {
        var names = MeetingShapes.Names(shape);

        names.Levels[MeetingNodeRole.WorkOf].ShouldBe([PlaceName.Organization, PlaceName.Project, PlaceName.Topic]);
        names.Levels[MeetingNodeRole.About].ShouldBe([PlaceName.Organization, PlaceName.Project, PlaceName.Topic]);
        names.Levels[MeetingNodeRole.Counterpart]
            .ShouldBe([PlaceName.OtherOrganization, PlaceName.Project, PlaceName.Topic]);
        names.Somebody.ShouldBeEmpty();
    }

    [Fact]
    public void A_shape_that_is_not_one_of_the_fourteen_has_no_names() =>
        Should.Throw<InvalidOperationException>(() => MeetingShapes.Names((MeetingShape)99));

    /// <summary>
    /// What each of the thirteen stories stores, and the two crossings, written out: the shape that
    /// is lit, the role of a link, how deep in its path the node is, and what class it is.
    /// </summary>
    /// <remarks>
    /// The rows are read off <c>ClassificationStoriesTests</c>' <c>Links</c> and <c>CrossedLinks</c>
    /// against its <c>Tree</c> — that project is not one this can reference, so they are written
    /// here, and the thing they hold the table to is the one that matters: every node a story stores
    /// is offered at a level whose name holds its class. A crossing is filed by hand, so it is read
    /// against the generic names.
    /// </remarks>
    [Theory]
    [InlineData(MeetingShape.Class, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.InterviewAsCandidate, MeetingNodeRole.Counterpart, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.InterviewAsInterviewer, MeetingNodeRole.WorkOf, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.TwoProjects, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.SellingToAClient, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.SellingToAClient, MeetingNodeRole.Counterpart, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.TeamMeeting, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.Conference, MeetingNodeRole.About, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.BetweenTwoCompanies, MeetingNodeRole.Counterpart, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.HumanResources, MeetingNodeRole.WorkOf, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.RecurringOneToOne, MeetingNodeRole.WorkOf, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.Daily, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.AfterSalesSupport, MeetingNodeRole.WorkOf, 2, NodeKind.Topic)]
    [InlineData(MeetingShape.AfterSalesSupport, MeetingNodeRole.Counterpart, 0, NodeKind.Organization)]
    [InlineData(MeetingShape.FilledByHand, MeetingNodeRole.WorkOf, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.FilledByHand, MeetingNodeRole.About, 1, NodeKind.Initiative)]
    [InlineData(MeetingShape.FilledByHand, MeetingNodeRole.Counterpart, 0, NodeKind.Organization)]
    public void Every_stored_story_is_offered_at_its_level(
        MeetingShape shape, MeetingNodeRole role, int depth, NodeKind stored)
    {
        var level = MeetingShapes.Names(shape).Levels[role][depth];

        MeetingShapes.Holds(level).ShouldBe(stored, $"{shape} {role} level {depth} is called {level}");
    }

    /// <summary>Whether a type is a <see cref="Guid"/> or is built out of them.</summary>
    private static bool Mentions(Type type) =>
        type == typeof(Guid)
        || type == typeof(Guid?)
        || type.GetGenericArguments().Any(Mentions);
}
