namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// What channel 0 was listening to. Both are the same Windows activation under two modes, so what
/// tells them apart is which processes were captured and not how.
/// </summary>
/// <remarks>
/// The names are older than that and say the API instead: "full loopback" was a loopback on the
/// playback endpoint, which this product no longer has. They are what a corpus stores under a
/// CHECK, so renaming them is a migration and is a task of its own rather than something a reading
/// of this file settles.
/// </remarks>
public enum CaptureMode
{
    /// <summary>The chosen process and the processes it started.</summary>
    ProcessLoopback = 1,

    /// <summary>
    /// Everything this machine plays, wherever it comes out. What somebody is offered when the
    /// program they chose turns out to be playing nothing, and it puts notifications and every
    /// other application into the file.
    /// </summary>
    FullLoopback = 2,
}
