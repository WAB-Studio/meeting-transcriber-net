using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Meetings;

/// <summary>One configured value. Secrets never land here — those live in Credential Manager.</summary>
public class Setting
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public UtcTimestamp UpdatedAt { get; set; }
}

/// <summary>
/// Local audit. Records who asked for something, including an agent over MCP. It outlives the
/// meeting it refers to, so that reference is cleared rather than cascaded away.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }

    public UtcTimestamp OccurredAt { get; set; }

    public AuditActor Actor { get; set; }

    public required string Action { get; set; }

    public Guid? MeetingId { get; set; }

    public string? Detail { get; set; }
}
