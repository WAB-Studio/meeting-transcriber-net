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
    /// The four stories that put somebody on the meeting, and how each of them is named.
    /// </summary>
    /// <remarks>
    /// Row 10 is the one worth having a row for: the person a dismissal is about was not in the
    /// room, so the slot opens with the subject toggle on and the attended one off. A screen that
    /// opened it the other way round would file somebody as having been at a meeting they were
    /// deliberately kept out of.
    /// </remarks>
    [Theory]
    [InlineData(MeetingShape.InterviewAsInterviewer, true, false)]
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

    /// <summary>Whether a type is a <see cref="Guid"/> or is built out of them.</summary>
    private static bool Mentions(Type type) =>
        type == typeof(Guid)
        || type == typeof(Guid?)
        || type.GetGenericArguments().Any(Mentions);
}
