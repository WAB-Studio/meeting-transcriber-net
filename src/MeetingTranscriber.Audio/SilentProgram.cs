using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Whether channel 0 is following a program that is not the one the meeting is coming out of.
/// </summary>
/// <remarks>
/// <para>
/// A program that cannot be followed does not fail. Windows accepts any process id and hands back
/// a stream of digital silence — following <c>System</c> activates without complaint and brings no
/// audio — so nothing throws, nothing ends, and the only thing that says the wrong program was
/// picked is that channel 0 has stayed at zero. This is the one place that reads that, so what
/// counts as "nothing" and how long is waited for it are one rule rather than one per screen.
/// </para>
/// <para>
/// It answers about a source and never about a recording: it says nothing was heard, and what is
/// done about it is somebody's — see <see cref="CaptureSession.RecordTheWholeMachine"/>.
/// </para>
/// </remarks>
public static class SilentProgram
{
    /// <summary>
    /// How long channel 0 hears nothing at all before it is worth saying so.
    /// </summary>
    /// <remarks>
    /// Somebody pressing record before anybody speaks is the ordinary case, so this cannot be a
    /// second or two. What is on the other side of it is a meeting being recorded against the
    /// wrong program, which costs a minute of the conversation for every minute nobody is told —
    /// and what saying so costs is a line beside a meter, not a recording that stops. Ten seconds
    /// is the shortest wait that is not about somebody's first breath.
    /// </remarks>
    public static readonly Duration Waits = Duration.FromSeconds(10);

    /// <summary>
    /// Whether a source that has been open for <paramref name="open"/> has said, by hearing
    /// nothing at all in that time, that it is following the wrong program.
    /// </summary>
    /// <param name="listening">What the source is listening to.</param>
    /// <param name="loudest">The loudest it has been since it opened, which is what it comes to.</param>
    /// <param name="open">How long it has been open.</param>
    /// <remarks>
    /// The loudest since it opened, and not the last second: a program that played one sentence
    /// and went quiet is being followed correctly, and a meter emptied every time somebody looked
    /// at it would call that meeting the wrong program too.
    /// </remarks>
    public static bool HeardNothing(CaptureTarget listening, LevelReading loudest, Duration open)
    {
        ArgumentNullException.ThrowIfNull(listening);

        // Only ever about a program. A channel listening to the whole machine is silent because
        // nothing is playing, which is the recording somebody asked for and not a wrong choice —
        // there is nothing else to offer them.
        return listening is CaptureTarget.Program && loudest.IsSilent && open >= Waits;
    }
}
