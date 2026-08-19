namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// Raised when a meeting is asked for an answer its stage does not have — because the stage moved,
/// because somebody already answered, or because the meeting is not there.
/// </summary>
/// <remarks>
/// A type of its own rather than an <see cref="InvalidOperationException"/>, because the caller
/// that has to tell this apart is a screen and the two answers are opposite ones. This is the
/// ordinary outcome of a screen that was drawn before somebody answered the same question
/// somewhere else: it is said and the screen re-reads. Everything else that shape is a defect, and
/// a screen that caught both would show a fault in the code as a corpus somebody could not read.
/// </remarks>
public sealed class MeetingStageException(string message) : InvalidOperationException(message);
