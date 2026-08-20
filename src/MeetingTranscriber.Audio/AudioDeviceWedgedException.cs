namespace MeetingTranscriber.Audio;

/// <summary>
/// Raised when this machine neither gave the application what it asked for nor refused it: a driver
/// or the audio service still inside a call this process made, which nothing here can bring back.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="AudioCaptureException"/>, because it is still this machine not giving up the
/// audio — so every boundary that turns that into a sentence for a person keeps working unchanged.
/// What the narrower type adds is that no answer arrived and the thread that asked is still in
/// there. What that costs is the two factories' to say and not this type's: a device that never
/// answered is one this application holds until it is restarted, while a machine that never
/// answered a question about its devices holds nothing and is simply not asked again until it
/// comes back.
/// </para>
/// <para>
/// One decision reads the difference, and it reads it at its own catch rather than by this type
/// standing outside the family: a session asked to follow a program Windows refuses says what
/// recording the whole machine instead would cost, and a device that never answered is not a
/// program that was refused. Saying it over one would offer somebody a second recording while a
/// thread nothing can stop is still inside the first device, on no answer at all.
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

    /// <summary>
    /// What a machine that will not say what it can record from is said to be. The other half of
    /// the same sentence: nothing was opened, so nothing is held — what it costs is that this
    /// question, and every other one about this machine's audio, is refused until it comes back.
    /// </summary>
    /// <param name="asked">
    /// What was asked about, said the way a person would hear it and read as the end of the
    /// sentence. It is the question still out there, which on the second go is not the one the
    /// caller just asked.
    /// </param>
    public static AudioDeviceWedgedException NoAnswerAbout(string asked) =>
        new($"Windows has not answered about {asked} in {CaptureLoop.StopsWithin.TotalSeconds:0} "
            + "seconds, and it has not refused either. Nothing more is asked about this machine's "
            + "audio until that question comes back.");
}
