namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when a spool cannot be read because something else on this machine is holding it, which
/// here means a capture writing it.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AudioCaptureException"/>, so every boundary that turns one of those into a
/// sentence for a person keeps working unchanged and so does every catch that already takes the
/// family. <see cref="AudioCaptureException"/>'s own remarks say what a narrower type has to earn:
/// a caller whose answer comes out differently, read at that caller rather than assumed from the
/// type. This one has exactly that caller.
/// </para>
/// <para>
/// <see cref="UnfinishedRecording.Export"/> lets one source that will not read cost its own file
/// and nothing else — but a source something is holding is not a damaged source, it is a meeting
/// that has not stopped, and all three outcomes refuse one of those whole. Telling the two apart by
/// re-opening the file afterwards asks a second time and gets a second answer: a capture that
/// stopped in between makes an intact source read as damaged, and one that started makes a whole
/// export go back. The open that failed already knew, and this is that knowledge carried out.
/// </para>
/// </remarks>
public sealed class SpoolInUseException : AudioCaptureException
{
    public SpoolInUseException(string message)
        : base(message)
    {
    }

    public SpoolInUseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
