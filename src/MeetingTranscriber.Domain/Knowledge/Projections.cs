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

    /// <summary>
    /// Position on the meeting timeline. With the meeting it is what a <see cref="Citation"/>
    /// anchors on, so it is an identity and not only an ordering: projecting the same response
    /// again has to put the same turn back at the same number.
    /// </summary>
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
/// <remarks>
/// A citation names the meeting and the position of the turn inside it, never a turn's id: the
/// ids belong to the projection, and a rebuild deletes them and mints new ones. The pair is what
/// the projection reproduces from <c>deepgram.json</c>, so it is what an extraction writes down
/// and what survives the rebuild that reinserts the claim.
/// </remarks>
public class Citation
{
    /// <summary>
    /// The meeting half of the anchor. It is the owner's own meeting column, shared rather than
    /// copied, so there is no way to cite a turn belonging to another meeting.
    /// </summary>
    public Guid MeetingId { get; set; }

    /// <summary>Position of the cited turn on the meeting timeline.</summary>
    public int UtteranceOrdinal { get; set; }

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

/// <summary>
/// Something the meeting left for somebody to do, exactly as the extraction proposed it. Where
/// it stands and who owns it are moved by a person, so they live in
/// <see cref="ActionItemProgress"/> and this row can be thrown away and projected again.
/// </summary>
public class ActionItem
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    /// <summary>
    /// Position in the extraction this was read out of. With the run it is the identity a person
    /// pins their state to, so projecting the same accepted extraction again has to reproduce it:
    /// it is the order of the items in the file, never the order rows happened to be written in.
    /// </summary>
    public int Ordinal { get; set; }

    public required string Statement { get; set; }

    /// <summary>A calendar day, not an instant, so it is stored as written.</summary>
    public DateOnly? DueDate { get; set; }

    public required Citation Evidence { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
