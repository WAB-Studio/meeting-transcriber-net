using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// Everything in this file is approved by a person. None of it can be inferred from an artifact,
// so a backup that copies only the files loses it.

/// <summary>Somebody who appears in meetings.</summary>
public class Person
{
    public Guid Id { get; set; }

    /// <summary>
    /// Where they work, when that is known. It has to be a node of kind
    /// <see cref="NodeKind.Organization"/>, which the schema cannot say and a CHECK cannot reach.
    /// One person at a time, which is already known to be too few — a contractor works for two
    /// clients, and somebody leaving one company for another belongs to both for a month.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    public required string DisplayName { get; set; }

    /// <summary>The user of this install. Channel 1 is theirs by contract, not by guess.</summary>
    public bool IsMe { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
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
/// Keyed on the extraction run and the position inside it rather than on an action's id, because
/// projecting the same accepted extraction again mints new ids. Nothing here points at
/// <c>action_items</c>: a foreign key to a table that gets deleted and refilled is the same data
/// loss with a constraint on top.
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
