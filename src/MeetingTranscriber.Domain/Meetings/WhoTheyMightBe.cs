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
/// Who is a candidate at all is decided by the name and by nothing else; a signal on its own never
/// does, since a shared organization with a different name is a different person. A name is a
/// candidate in one of two ways: it resembles the typed one at least <see cref="Alike"/>, or the
/// typed one is a part of it — the start of the held name, or one of its words, of at least
/// <see cref="ShortestPart"/> characters. The second is how a first name or a surname finds the
/// full name already held, which scores far below <see cref="Alike"/> as a whole-name resemblance
/// (<c>Marina</c> against <c>Marina Robles</c> is under half). Every candidate that resembles the
/// typed name comes before every candidate that only contains it, because a closer spelling is the
/// likelier person. Inside each of the two the order is: the resemblance in tenths, then by how
/// many signals hold, then by the exact resemblance. The two signals — they share
/// an organization with what this meeting is filed under, they have met somebody who is here — only
/// rank candidates that are about equally alike. A score adding the signals to resemblance would
/// rank a worse spelling above a better one, which is the one thing a person reading the pills
/// would not forgive.
/// </para>
/// <para>
/// No more than <see cref="MostOffered"/>: the pills sit under one field and three fit.
/// </para>
/// </remarks>
public static class WhoTheyMightBe
{
    /// <summary>How alike a name has to be to be offered.</summary>
    public const double Alike = 0.75;

    /// <summary>
    /// The fewest characters, folded, a typed name may have to be offered as a part of a held name.
    /// Two letters are the start of a great many names and say nothing about whom was meant.
    /// </summary>
    public const int ShortestPart = 3;

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

        var typedFolded = Spellings.Folded(typed);

        return
        [
            .. known
                .Where(candidate => !notOffered.Contains(candidate.Person.Id))
                .Select(candidate => new PossiblySame(
                    candidate.Person,
                    Spellings.Resemblance(typed, candidate.Person.DisplayName),
                    candidate.Organizations.Any(organizations.Contains),
                    candidate.MetSomebodyHere))
                .Where(possible => possible.Resemblance >= Alike || IsAPartOf(typedFolded, possible.Person))
                .OrderByDescending(possible => possible.Resemblance >= Alike)
                .ThenByDescending(Tenths)
                .ThenByDescending(possible => (possible.SharesAnOrganization ? 1 : 0) + (possible.MetSomebodyHere ? 1 : 0))
                .ThenByDescending(possible => possible.Resemblance)
                .ThenBy(possible => possible.Person.DisplayName, StringComparer.Ordinal)
                .ThenBy(possible => possible.Person.Id)
                .Take(MostOffered),
        ];
    }

    // The typed name, already folded, is the start of the held name's folded form or equal to the
    // folded form of one of its words. Folded drops white space, so "marina ro" is the start of
    // "marinarobles" and a surname typed whole is one of the words.
    private static bool IsAPartOf(string typedFolded, Person person)
    {
        if (typedFolded.Length < ShortestPart)
        {
            return false;
        }

        return Spellings.Folded(person.DisplayName).StartsWith(typedFolded, StringComparison.Ordinal)
            || Spellings.WordsOf(person.DisplayName)
                .Any(word => Spellings.Folded(word).Equals(typedFolded, StringComparison.Ordinal));
    }

    // A small epsilon so 0.9 computed as 1 - 1/10 is the tenth it reads as and not the one below.
    private static int Tenths(PossiblySame possible) => (int)Math.Floor((possible.Resemblance * 10) + 1e-9);
}
