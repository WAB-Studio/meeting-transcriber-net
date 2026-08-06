using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// Everything in this file is approved by a person. None of it can be inferred from an artifact,
// so a backup that copies only the files loses it.

/// <summary>Somebody who appears in meetings.</summary>
public class Person
{
    public Guid Id { get; set; }

    /// <summary>
    /// Where they work, when that is known. One person at a time, which is already known to be too
    /// few — a contractor works for two clients, and somebody leaving one company for another
    /// belongs to both for a month.
    /// </summary>
    /// <remarks>
    /// Set through <see cref="WorksAt"/>, which is what keeps it in step with
    /// <see cref="OrganizationKind"/>: the pair is one foreign key onto a node's id and class, so
    /// "their organization is a node of kind organization" is refused by the database and not
    /// merely asserted here.
    /// </remarks>
    public Guid? OrganizationId { get; private set; }

    /// <summary>
    /// Always <see cref="NodeKind.Organization"/> when there is an organization at all. It is the
    /// half of the key that says which class the node has to be.
    /// </summary>
    public NodeKind? OrganizationKind { get; private set; }

    public required string DisplayName { get; set; }

    /// <summary>The user of this install. Channel 1 is theirs by contract, not by guess.</summary>
    public bool IsMe { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }

    /// <summary>
    /// Records where they work, or clears it when given nothing. Only an organization will do: a
    /// project and a ticket are places work happens, not people's employers.
    /// </summary>
    public void WorksAt(Node? organization)
    {
        if (organization is not null && organization.Kind is not NodeKind.Organization)
        {
            throw new ClassificationException(
                $"'{DisplayName}' cannot work at '{organization.Name}': it is a {organization.Kind}, not an {NodeKind.Organization}.");
        }

        OrganizationId = organization?.Id;
        OrganizationKind = organization is null ? null : NodeKind.Organization;
    }
}

/// <summary>A person confirmed to have been in a meeting.</summary>
public class MeetingParticipant
{
    public Guid MeetingId { get; set; }

    public Guid PersonId { get; set; }

    /// <summary>Why they are on it. Most people attended; some are what it was about.</summary>
    public ParticipantRole Role { get; set; } = ParticipantRole.Attended;

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
