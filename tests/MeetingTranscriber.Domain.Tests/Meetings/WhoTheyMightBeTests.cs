using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// Who a name being typed might already be, judged by the spelling alone and ranked by what holds of
/// each person against the meeting being filed.
/// </summary>
public class WhoTheyMightBeTests
{
    private static readonly Guid Nubeko = Guid.NewGuid();

    private static readonly IReadOnlySet<Guid> Nobody = new HashSet<Guid>();

    private static KnownPerson Somebody(string name, bool organization = false, bool met = false) =>
        new(
            new Person
            {
                Id = Guid.NewGuid(),
                DisplayName = name,
                CreatedAt = UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)),
                UpdatedAt = UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)),
            },
            organization ? [Nubeko] : [],
            met);

    private static IReadOnlyList<PossiblySame> Offered(string typed, params KnownPerson[] known) =>
        WhoTheyMightBe.For(typed, known, [Nubeko], Nobody);

    [Fact]
    public void A_name_spelled_a_little_differently_offers_the_person_already_there()
    {
        var offered = Offered("Marina Robless", Somebody("Marina Robles"));

        offered.Select(possible => possible.Person.DisplayName).ShouldBe(["Marina Robles"]);
    }

    [Fact]
    public void A_name_nothing_resembles_offers_nobody() =>
        Offered("Zoe Quispe", Somebody("Marina Robles")).ShouldBeEmpty();

    [Fact]
    public void Nothing_typed_offers_nobody() =>
        Offered("   ", Somebody("Marina Robles")).ShouldBeEmpty();

    /// <summary>
    /// Both at 0.81: two letters off over eleven. The one who sorts later by name comes first, so
    /// it is the signal that moved her and not the ordering underneath it.
    /// </summary>
    [Fact]
    public void Of_two_alike_the_one_who_shares_an_organization_comes_first()
    {
        var plain = Somebody("Elena Pratwy");
        var sharing = Somebody("Elena Pratzz", organization: true);

        Offered("Elena Prat", plain, sharing)[0].Person.Id.ShouldBe(sharing.Person.Id);
    }

    [Fact]
    public void Of_two_alike_the_one_who_met_somebody_here_comes_first()
    {
        var plain = Somebody("Elena Pratwy");
        var met = Somebody("Elena Pratzz", met: true);

        Offered("Elena Prat", plain, met)[0].Person.Id.ShouldBe(met.Person.Id);
    }

    /// <summary>0.92 with nothing against 0.86 with both: the signals rank only near-equals.</summary>
    [Fact]
    public void A_closer_spelling_comes_before_a_further_one_with_every_signal()
    {
        var closer = Somebody("Marina Roblesx");
        var further = Somebody("Marina Robbbles", organization: true, met: true);

        var offered = Offered("Marina Robles", further, closer);

        offered[0].Person.Id.ShouldBe(closer.Person.Id);
        offered[0].Resemblance.ShouldBeGreaterThan(offered[1].Resemblance);
    }

    [Fact]
    public void Somebody_the_screen_cannot_take_is_never_offered()
    {
        var taken = Somebody("Marina Robles");

        WhoTheyMightBe.For("Marina Robles", [taken], [Nubeko], new HashSet<Guid> { taken.Person.Id })
            .ShouldBeEmpty();
    }

    [Fact]
    public void No_more_than_three_are_offered()
    {
        var many = Enumerable.Range(0, 5)
            .Select(n => Somebody($"Marina Robles{(char)('a' + n)}"))
            .ToArray();

        Offered("Marina Robles", many).Count.ShouldBe(WhoTheyMightBe.MostOffered);
    }

    /// <summary>
    /// <c>Marina</c> against <c>Marina Robles</c> resembles it by about 0.46, far under the line, so
    /// resemblance alone offers nobody — and a first name is the commonest way a duplicate person is
    /// typed.
    /// </summary>
    [Fact]
    public void A_first_name_offers_the_full_name_already_held()
    {
        var held = Somebody("Marina Robles");

        Offered("Marina", held).Select(possible => possible.Person.Id).ShouldBe([held.Person.Id]);
    }

    [Fact]
    public void A_surname_offers_the_full_name_already_held()
    {
        var held = Somebody("Marina Robles");

        Offered("Robles", held).Select(possible => possible.Person.Id).ShouldBe([held.Person.Id]);
    }

    /// <summary>
    /// Both come up for <c>Marina</c>: <c>Marinna</c> by resemblance and <c>Marina Robles</c> only as
    /// containing it. The closer spelling is first, though the other holds both signals.
    /// </summary>
    [Fact]
    public void A_part_comes_after_every_closer_spelling()
    {
        var contains = Somebody("Marina Robles", organization: true, met: true);
        var resembles = Somebody("Marinna");

        Offered("Marina", contains, resembles)
            .Select(possible => possible.Person.Id)
            .ShouldBe([resembles.Person.Id, contains.Person.Id]);
    }

    [Fact]
    public void Two_letters_offer_nobody_by_being_a_start() =>
        Offered("Ma", Somebody("Marina Robles")).ShouldBeEmpty();
}
