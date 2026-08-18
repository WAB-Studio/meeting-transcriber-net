namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when a device neither gave the application what it asked for nor refused it: a driver
/// still inside a call this process made, which nothing here can bring back.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AudioCaptureException"/>, because it is still this machine not giving up the
/// audio — so every boundary that turns that into a sentence for a person keeps working unchanged.
/// What the narrower type adds is that no answer arrived: the thread that asked is still in there,
/// everything it holds stays held, and the next recording needing that device does not open until
/// the application is restarted.
/// </para>
/// <para>
/// One decision reads the difference, and it reads it at its own catch rather than by this type
/// standing outside the family: a session asked to follow one program falls back to the whole
/// machine's audio when that program cannot be recorded, and a device that never answered is not a
/// program that cannot be followed. Falling back over one would open a second device while the
/// first is still held by a thread nothing can stop, on no answer at all.
/// </para>
/// </remarks>
public sealed class AudioDeviceWedgedException : AudioCaptureException
{
    public AudioDeviceWedgedException(string message)
        : base(message)
    {
    }

    public AudioDeviceWedgedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// What a device that never answered is said to be, in one sentence and in one place. Both
    /// moments on the way into a recording reach it — the device would not be opened, and the
    /// device would not be started — and they say the same thing because from out here they are
    /// the same thing: no answer arrived, and what was asked stays held.
    /// </summary>
    /// <param name="device">What did not answer, said the way a person would hear it.</param>
    public static AudioDeviceWedgedException NoAnswerFrom(string device) =>
        new($"The {device} did not answer within {CaptureLoop.StopsWithin.TotalSeconds:0} seconds, "
            + "and it did not refuse either. It stays held until this application is restarted.");
}
