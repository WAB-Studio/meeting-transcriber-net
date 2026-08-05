using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

// Everything here is rebuildable from deepgram.json and the accepted extractions. Deleting all
// of it and rendering again has to stay a safe thing to do.

/// <summary>One speaker turn, ordered on the meeting timeline.</summary>
public class Utterance
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public int Ordinal { get; set; }

    /// <summary>Offset from the start of the meeting.</summary>
    public Duration Start { get; set; }

    public Duration End { get; set; }

    /// <summary>
    /// Null for a diarized single track, which has no channel to be deterministic about.
    /// </summary>
    public AudioChannel? Channel { get; set; }

    /// <summary>
    /// What the provider called the speaker. Never overwritten with a person's name: names are
    /// applied when rendering, so evidence stays comparable to the raw response.
    /// </summary>
    public required string SpeakerLabel { get; set; }

    public required string Text { get; set; }

    public double? Confidence { get; set; }
}

/// <summary>
/// Where a claim came from. Validation refuses an extraction whose citation does not land on a
/// real turn, so there is no state in which the corpus holds a claim with nothing behind it.
/// </summary>
public class Citation
{
    public Guid UtteranceId { get; set; }

    public Duration Start { get; set; }

    public Duration End { get; set; }

    public required string SpeakerLabel { get; set; }

    public required string QuotedText { get; set; }

    /// <summary>Hash of the artifact the quote was read out of, so a rerender cannot fake it.</summary>
    public required string SourceArtifactSha256 { get; set; }
}

/// <summary>The readable summary of a meeting, from one extraction.</summary>
public class Summary
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    public string? Abstract { get; set; }

    public string? Body { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>Something the meeting settled.</summary>
public class Decision
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    public required string Statement { get; set; }

    public Guid? DecidedByPersonId { get; set; }

    public required Citation Evidence { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>Something the meeting left for somebody to do.</summary>
public class ActionItem
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    public required string Statement { get; set; }

    public Guid? OwnerPersonId { get; set; }

    /// <summary>A calendar day, not an instant, so it is stored as written.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Moved by a person. An extraction proposes; it never closes one.</summary>
    public ActionItemState State { get; set; } = ActionItemState.Open;

    public required Citation Evidence { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
