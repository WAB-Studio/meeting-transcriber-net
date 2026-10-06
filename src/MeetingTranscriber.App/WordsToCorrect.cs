namespace MeetingTranscriber.App;

/// <summary>
/// What a screen hands the window when somebody asks to correct words: the meeting they were read
/// in, and the words as they were written when they were selected.
/// </summary>
/// <param name="Meeting">The meeting the words were read in, which is where the corrections screen opens.</param>
/// <param name="AsWritten">
/// The words selected, already trimmed by <c>Spellings.TrimToWords</c>, or nothing when
/// the corrections screen is asked for on its own and has no words to start from.
/// </param>
public sealed record WordsToCorrect(Guid Meeting, string? AsWritten);
