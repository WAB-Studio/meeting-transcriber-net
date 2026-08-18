namespace MeetingTranscriber.Recording;

/// <summary>
/// Recording a meeting into a corpus went wrong somewhere that is neither the devices' fault nor
/// the corpus's — the two halves were asked to be joined in a way that does not hold.
/// </summary>
/// <remarks>
/// Its own type rather than one of the two it sits between, because those two mean something: an
/// audio failure is a device, and a corpus failure is a row or a file. A meeting that has no row
/// to be finished into is neither, and reporting it as one of them would send whoever reads it to
/// look at the wrong half.
/// </remarks>
public sealed class RecordingException(string message, Exception? inner = null)
    : Exception(message, inner);
