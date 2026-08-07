namespace MeetingTranscriber.Domain.Meetings;

/// <summary>What settled a speaker label onto a person.</summary>
public enum SpeakerAssignmentSource
{
    /// <summary>
    /// The recording gave it for free, which happens in one case: the microphone channel came back
    /// with a single speaker, so there was nobody else it could be. <c>Speakers.Resolve</c> is the
    /// only thing that hands this out.
    /// </summary>
    Channel = 1,

    /// <summary>Somebody resolved a diarized label, which outranks any guess.</summary>
    Person = 2,
}

/// <summary>How a terminology correction matches the text it replaces.</summary>
public enum TerminologyMatchMode
{
    Exact = 1,
    IgnoreCase = 2,
}

/// <summary>Where an action item stands. A person moves it, never an extraction.</summary>
public enum ActionItemState
{
    Open = 1,
    Done = 2,
    Dropped = 3,
}

/// <summary>Who asked for the thing an audit event records.</summary>
public enum AuditActor
{
    User = 1,
    App = 2,

    /// <summary>An LLM agent, today only over the local MCP server.</summary>
    Agent = 3,
}
