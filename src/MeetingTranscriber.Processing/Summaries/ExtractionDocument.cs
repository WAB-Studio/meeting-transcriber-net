using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// An extraction's output, schema version <c>"1"</c>, exactly as it read once every field is where
/// the shape says it belongs and nothing else is there. Nothing here says whether the meeting
/// supports it — <see cref="Summaries.ExtractionCheck"/> is what asks that.
/// </summary>
public sealed record ExtractionDocument(
    string SchemaVersion,
    Guid MeetingId,
    string Abstract,
    string Summary,
    IReadOnlyList<string> Participants,
    IReadOnlyList<ExtractedDecision> Decisions,
    IReadOnlyList<ExtractedAction> Actions,
    IReadOnlyList<ExtractedQuestion> OpenQuestions);

/// <summary>
/// Where a decision, an action or an open question was said: the turn it names, and the words it
/// says that turn holds. Offsets are <see cref="Duration"/> because the contract lets no bare
/// number of milliseconds into the domain.
/// </summary>
public sealed record ExtractedEvidence(
    int UtteranceOrdinal,
    Duration Start,
    Duration End,
    string SpeakerLabel,
    string QuotedText);

/// <summary>One thing the meeting settled, and where it says that happened, or nowhere.</summary>
public sealed record ExtractedDecision(string Statement, ExtractedEvidence? Evidence);

/// <summary>One thing somebody undertook, when it is due, and where it says that happened, or nowhere.</summary>
public sealed record ExtractedAction(string Statement, DateOnly? DueDate, ExtractedEvidence? Evidence);

/// <summary>One thing the meeting left unresolved, and where it says that happened, or nowhere.</summary>
public sealed record ExtractedQuestion(string Question, ExtractedEvidence? Evidence);
