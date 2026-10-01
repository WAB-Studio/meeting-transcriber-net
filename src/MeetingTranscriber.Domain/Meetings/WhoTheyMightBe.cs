using MeetingTranscriber.Domain.Knowledge;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>Somebody the corpus holds, with the two things about them a typed name is read against.</summary>
/// <param name="Organizations">
/// The id of every organization they were ever affiliated with, ended or not: where they have
/// belonged is not where they belong now, and a person typed again years later is the same person.
/// </param>
/// <param name="MetSomebodyHere">
/// Whether they appear on another meeting together with somebody who is on this one.
/// </param>
public sealed record KnownPerson(Person Person, IReadOnlyList<Guid> Organizations, bool MetSomebodyHere);

/// <summary>A person a typed name might already be, and why they came up.</summary>
public sealed record PossiblySame(
    Person Person,
    double Resemblance,
    bool SharesAnOrganization,
    bool MetSomebodyHere);

/// <summary>
/// What the screen that opened the dialogue holds, as it holds it now: the meeting, who and what its
/// unsaved draft already names, and who it may not take.
/// </summary>
/// <param name="NotOffered">
/// People the opening screen's own picker leaves out. A person offered here goes straight into a slot
/// through whatever the screen does with an answer, so offering one it would refuse is a pill that
/// fails when pressed.
/// </param>
public sealed record OpenedOver(
    Guid Meeting,
    IReadOnlyCollection<Guid> PeopleOnTheDraft,
    IReadOnlyCollection<Guid> NodesOnTheDraft,
    IReadOnlySet<Guid> NotOffered);

/// <summary>
/// Who somebody being added might already be: the people the corpus holds under a name spelled
/// nearly the same way, closest first.
/// </summary>
/// <remarks>
/// <para>
/// Resemblance of the name decides who is a candidate at all; a signal on its own never does, since
/// a shared organization with a different name is a different person. The two signals — they share
/// an organization with what this meeting is filed under, they have met somebody who is here — only
/// rank candidates that are about equally alike: ordering is by the resemblance in tenths, then by
/// how many signals hold, then by the exact resemblance. A score adding the signals to resemblance
/// would rank a worse spelling above a better one, which is the one thing a person reading the
/// pills would not forgive.
/// </para>
/// <para>
/// No more than <see cref="MostOffered"/>: the pills sit under one field and three fit.
/// </para>
/// </remarks>
public static class WhoTheyMightBe
{
    /// <summary>How alike a name has to be to be offered.</summary>
    public const double Alike = 0.75;

    /// <summary>The most people offered at once.</summary>
    public const int MostOffered = 3;

    public static IReadOnlyList<PossiblySame> For(
        string typed,
        IEnumerable<KnownPerson> known,
        IReadOnlyCollection<Guid> organizations,
        IReadOnlySet<Guid> notOffered)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(notOffered);

        if (string.IsNullOrWhiteSpace(typed))
        {
            return [];
        }

        return
        [
            .. known
                .Where(candidate => !notOffered.Contains(candidate.Person.Id))
                .Select(candidate => new PossiblySame(
                    candidate.Person,
                    Spellings.Resemblance(typed, candidate.Person.DisplayName),
                    candidate.Organizations.Any(organizations.Contains),
                    candidate.MetSomebodyHere))
                .Where(possible => possible.Resemblance >= Alike)
                .OrderByDescending(Tenths)
                .ThenByDescending(possible => (possible.SharesAnOrganization ? 1 : 0) + (possible.MetSomebodyHere ? 1 : 0))
                .ThenByDescending(possible => possible.Resemblance)
                .ThenBy(possible => possible.Person.DisplayName, StringComparer.Ordinal)
                .ThenBy(possible => possible.Person.Id)
                .Take(MostOffered),
        ];
    }

    // A small epsilon so 0.9 computed as 1 - 1/10 is the tenth it reads as and not the one below.
    private static int Tenths(PossiblySame possible) => (int)Math.Floor((possible.Resemblance * 10) + 1e-9);
}
