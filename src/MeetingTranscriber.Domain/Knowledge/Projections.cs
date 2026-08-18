using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Knowledge;

// Everything here is rebuildable from deepgram.json and the accepted extractions. Deleting all
// of it and rendering again has to stay a safe thing to do.

/// <summary>
/// A row an extraction produced, named by the run it came out of and where it sat inside it.
/// </summary>
/// <remarks>
/// <para>
/// The pair is the identity, and an id is not: projecting the same accepted extraction again
/// deletes these rows and mints new ids, so anything a person pinned to one would be pointing at a
/// row that no longer exists. The position is the order of the items in the file, never the order
/// rows happened to be written in — which is the whole of what makes projecting twice reproduce it.
/// It counts within its own list: an extraction returns decisions, actions and open questions
/// separately, so the first of each is at zero and what tells them apart is which list they are in.
/// </para>
/// <para>
/// Carried as a contract rather than as tables that happen to agree, because what enforces it is a
/// uniqueness the writer never sees: two rows of one kind at one position would make somebody's
/// note ambiguous rather than wrong, which is the harder kind of bug to see. Taking this on is what
/// makes a row one of these, and the storage layer holds every one of them to the same rule — the
/// mapping still has to ask for it, so what stops a row from being anchored halfway is a test that
/// reads this list back off the model.
/// </para>
/// </remarks>
public interface IExtractionPosition
{
    Guid ExtractionRunId { get; }

    int Ordinal { get; }
}

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
/// <para>
/// A citation names the meeting and the position of the turn inside it, never a turn's id: the
/// ids belong to the projection, and a rebuild deletes them and mints new ones. The pair is what
/// the projection reproduces from <c>deepgram.json</c>, so it is what an extraction writes down
/// and what survives the rebuild that reinserts the claim.
/// </para>
/// <para>
/// A deterministic id derived from that same pair would have survived a rebuild too, and would
/// have left the schema alone. It was refused because it promises what it does not mean: it says
/// "the turn's identity" where the pair says "meeting and position", and it makes every extraction
/// already stored depend on a derivation function that breaks all of them silently if it changes.
/// </para>
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
/// <remarks>
/// Anchored by <see cref="IExtractionPosition"/> for the same reason an action is: whether a
/// decision still stands is a person's word and not the extraction's, so it is written down
/// somewhere a rebuild does not reach, and it has to find its decision again afterwards.
/// </remarks>
public class Decision : IExtractionPosition
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    /// <inheritdoc cref="IExtractionPosition.Ordinal"/>
    public int Ordinal { get; set; }

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
public class ActionItem : IExtractionPosition
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    /// <inheritdoc cref="IExtractionPosition.Ordinal"/>
    public int Ordinal { get; set; }

    public required string Statement { get; set; }

    /// <summary>A calendar day, not an instant, so it is stored as written.</summary>
    public DateOnly? DueDate { get; set; }

    public required Citation Evidence { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}

/// <summary>
/// Something the meeting raised and did not settle, exactly as the extraction proposed it.
/// </summary>
/// <remarks>
/// It carries evidence like a decision and an action do. A question nothing said supports is a
/// sentence the model wrote with nothing in the meeting to check it against, which is what the
/// citation rules exist to keep out of the corpus — and the turn it was raised at is what somebody
/// reading it months later opens to find out whether it ever got answered.
/// </remarks>
public class OpenQuestion : IExtractionPosition
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid ExtractionRunId { get; set; }

    /// <inheritdoc cref="IExtractionPosition.Ordinal"/>
    public int Ordinal { get; set; }

    public required string Question { get; set; }

    public required Citation Evidence { get; set; }

    public UtcTimestamp CreatedAt { get; set; }
}
