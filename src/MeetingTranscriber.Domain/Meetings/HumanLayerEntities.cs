using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

// Everything in this file is approved by a person. None of it can be inferred from an artifact,
// so a backup that copies only the files loses it.

/// <summary>A body of work meetings are grouped under.</summary>
public class Project
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
}

/// <summary>Somebody who appears in meetings.</summary>
public class Person
{
    public Guid Id { get; set; }

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

    public string? Role { get; set; }

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
/// A word the transcription gets wrong, and what it should say. Applied when rendering, never
/// written back into the raw response.
/// </summary>
public class TerminologyCorrection
{
    public Guid Id { get; set; }

    /// <summary>Scoped to a project, to a single meeting, or to neither, which makes it global.</summary>
    public Guid? ProjectId { get; set; }

    public Guid? MeetingId { get; set; }

    public required string WrongText { get; set; }

    public required string CorrectText { get; set; }

    public TerminologyMatchMode MatchMode { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
