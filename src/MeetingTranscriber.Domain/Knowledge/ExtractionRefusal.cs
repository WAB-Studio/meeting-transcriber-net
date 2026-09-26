namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// One reason an extraction was refused: what was observed, where in the document, and the
/// statement's or question's own words when the refusal is about one.
/// </summary>
/// <param name="Path">
/// Dotted with brackets and with no leading <c>$</c>, except the document itself, which is
/// <c>$</c>. What a person or a screen reads to find the field that failed.
/// </param>
/// <param name="Statement">
/// The offending statement's or question's own text, carried so a person can be shown which one
/// failed without opening the extraction again. Null when the refusal is about the document as a
/// whole rather than about one statement.
/// </param>
/// <remarks>
/// It is here in <c>Domain</c>, and not beside <c>ExtractionCheck</c> which produces it, because
/// <c>MeetingScreen</c> carries one: a screen may show why a meeting has no summary, and a screen
/// names only <c>Domain</c> types.
/// </remarks>
public sealed record ExtractionRefusal(ExtractionCondition Condition, string Path, string? Statement);
