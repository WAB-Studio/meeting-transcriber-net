using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// Everything in this file is approved by a person. None of it can be inferred from an artifact,
// so a backup that copies only the files loses it.

/// <summary>Somebody who appears in meetings.</summary>
/// <remarks>
/// Where they work is not here: it is an <see cref="Affiliation"/>, of which they have as many as
/// they have, because a contractor works for two clients at once and somebody moving between
/// companies belongs to both for a month.
/// </remarks>
public class Person
{
    public Guid Id { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>
    /// The user of this install. The microphone is theirs by contract; the voices on it are only
    /// theirs when it caught one speaker, and otherwise somebody has to say which is which.
    /// </summary>
    public bool IsMe { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
}

/// <summary>
/// Somebody at an organization, for as long as they were there. Employment is one of these, and so
/// are contracting, membership and being enrolled — the corpus records that the person and the
/// organization go together, not under which contract.
/// </summary>
/// <remarks>
/// <para>
/// It carries a period because a meeting is read years after it happened: without one, hiring the
/// candidate you interviewed rewrites the interview into a meeting with your own employee. Both
/// ends are open rather than unknown — no start is "as far back as this corpus goes", no end is
/// "still there" — and a corpus that never learned the dates has both, which is what the legacy
/// import produces.
/// </para>
/// <para>
/// The class travels beside the id so the pair is one foreign key onto a node's own id and class:
/// that somebody's organization is a node of kind organization is refused by the database, and a
/// project or a ticket has nowhere to be written.
/// </para>
/// </remarks>
public class Affiliation
{
    private Affiliation()
    {
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>Always <see cref="NodeKind.Organization"/>: the half of the key that says so.</summary>
    public NodeKind OrganizationKind { get; private set; }

    /// <summary>When they joined, or null for as far back as this corpus knows.</summary>
    public UtcTimestamp? StartedAt { get; private set; }

    /// <summary>When they left, or null while they are still there.</summary>
    public UtcTimestamp? EndedAt { get; private set; }

    public UtcTimestamp CreatedAt { get; private set; }

    /// <summary>
    /// Puts somebody at an organization. Only an organization will do: a project and a ticket are
    /// places work happens, not places people belong to.
    /// </summary>
    public static Affiliation At(
        Guid id,
        Person person,
        Node organization,
        UtcTimestamp now,
        UtcTimestamp? from = null,
        UtcTimestamp? until = null)
    {
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(organization);

        if (organization.Kind is not NodeKind.Organization)
        {
            throw new ClassificationException(
                $"'{person.DisplayName}' cannot be at '{organization.Name}': it is a {organization.Kind}, not an {NodeKind.Organization}.");
        }

        var affiliation = new Affiliation
        {
            Id = id,
            PersonId = person.Id,
            OrganizationId = organization.Id,
            OrganizationKind = NodeKind.Organization,
            StartedAt = from,
            CreatedAt = now,
        };

        if (until is { } end)
        {
            affiliation.Ended(end);
        }

        return affiliation;
    }

    /// <summary>Closes it. Leaving before arriving is a typo, and this is where it is caught.</summary>
    public void Ended(UtcTimestamp at)
    {
        if (StartedAt is { } start && at < start)
        {
            throw new ClassificationException($"An affiliation cannot end at {at} and have started at {start}.");
        }

        EndedAt = at;
    }

    /// <summary>
    /// Whether it held then, which is the question every reading of an old meeting asks. Half open
    /// on purpose: the day somebody leaves one company and joins another belongs to the new one,
    /// and counting it twice would put them in both.
    /// </summary>
    public bool Held(UtcTimestamp at) =>
        (StartedAt is not { } start || start <= at) && (EndedAt is not { } end || at < end);
}

/// <summary>
/// A person a meeting names, and the part that says how. A person has as many of these as apply:
/// their own one to one is one they attended and are the subject of, and both are true at once.
/// </summary>
public class MeetingPerson
{
    public Guid MeetingId { get; set; }

    public Guid PersonId { get; set; }

    public MeetingPersonRole Role { get; set; } = MeetingPersonRole.Attended;

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>
/// A speaker label resolved onto a person. Labels stay as the provider wrote them; this is what
/// turns one into a name at render time.
/// </summary>
public class SpeakerAssignment
{
    public Guid MeetingId { get; set; }

    public required string SpeakerLabel { get; set; }

    public Guid PersonId { get; set; }

    public SpeakerAssignmentSource AssignedBy { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>
/// Where an action stands and who owns it. An extraction proposes the action; it never closes
/// one and never picks the owner, so neither can sit in <c>action_items</c>, which a rebuild
/// deletes whole.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the extraction run and the position inside it rather than on an action's id, because
/// projecting the same accepted extraction again mints new ids. Nothing here points at
/// <c>action_items</c>: a foreign key to a table that gets deleted and refilled is the same data
/// loss with a constraint on top.
/// </para>
/// <para>
/// That key covers a rebuild and deliberately does not cover a re-extraction. A second extraction
/// is a second run, so its actions arrive with no progress at all and are shown as new, and what
/// somebody marked stays on the run they marked it on — readable, and superseded rather than
/// moved. Neither way of carrying it forward survives contact with what an extraction actually
/// does: the position moves whenever the model adds an item or reorders one, so following it would
/// hand somebody else's state to the wrong action without anything failing, and following the
/// statement text means a reworded line loses its owner while a line repeated in two meetings
/// finds one. A person re-reading the summary they asked for again is the honest outcome.
/// </para>
/// </remarks>
public class ActionItemProgress
{
    public Guid ExtractionRunId { get; set; }

    public int Ordinal { get; set; }

    public ActionItemState State { get; set; } = ActionItemState.Open;

    /// <summary>Who took it, which is a person saying so — never the extraction guessing.</summary>
    public Guid? OwnerPersonId { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
}

/// <summary>
/// A word the transcription gets wrong, and what it should say. Applied when rendering, never
/// written back into the raw response.
/// </summary>
public class TerminologyCorrection
{
    public Guid Id { get; set; }

    /// <summary>
    /// Scoped to a node and everything under it, to a single meeting, or to neither, which makes
    /// it global.
    /// </summary>
    public Guid? NodeId { get; set; }

    public Guid? MeetingId { get; set; }

    public required string WrongText { get; set; }

    public required string CorrectText { get; set; }

    public TerminologyMatchMode MatchMode { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
