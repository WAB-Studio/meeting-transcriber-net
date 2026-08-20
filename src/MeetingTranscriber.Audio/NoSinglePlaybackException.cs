namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when a source's blocks cannot become one playable file because the source changed
/// device mid meeting and the one that took over handed over another format.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AudioCaptureException"/>, because from outside it is still audio this machine
/// will not hand over in the shape that was asked for — so every boundary that turns one of those
/// into a sentence for a person keeps working unchanged, and so does every catch that already
/// takes the family.
/// </para>
/// <para>
/// What the narrower type adds is that nothing is wrong. The spool is whole, every block in it
/// hashes, and the recording those blocks make is already written; what does not exist is the
/// single-format file, because a WAV is one format all the way down and this source is two. That
/// is the difference between a recording somebody can still recover and one they cannot, and it
/// is the only reason to tell the two apart: a caller that has other work to do can say this one
/// and carry on, and must not say a spool that would not open and carry on, because that is the
/// artifact being unreadable by the build that just wrote it.
/// </para>
/// </remarks>
public sealed class NoSinglePlaybackException : AudioCaptureException
{
    public NoSinglePlaybackException(string message)
        : base(message)
    {
    }

    public NoSinglePlaybackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
