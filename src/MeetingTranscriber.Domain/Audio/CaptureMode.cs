namespace MeetingTranscriber.Domain.Audio;

/// <summary>
/// What channel 0 was listening to. Both are the same Windows activation under two modes, so what
/// tells them apart is which processes were captured and not how.
/// </summary>
/// <remarks>
/// These member names are the stored format, not a private spelling: a corpus holds them under a
/// CHECK and a recording's folder carries one beside its blocks, both derived from the identifier
/// here. So renaming a member rewrites what is on disk, and is a migration rather than something a
/// reading of this file settles.
/// </remarks>
public enum CaptureMode
{
    /// <summary>The chosen process and the processes it started.</summary>
    OneProgram = 1,

    /// <summary>
    /// Everything this machine plays, wherever it comes out. What somebody is offered when the
    /// program they chose turns out to be playing nothing, and it puts notifications and every
    /// other application into the file.
    /// </summary>
    WholeMachine = 2,
}
