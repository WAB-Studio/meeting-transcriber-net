using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Recording;

/// <summary>
/// The microphone and the source somebody has chosen, opened to be heard and not to be recorded:
/// what the meters show before a meeting has started.
/// </summary>
/// <remarks>
/// <para>
/// Each is opened on its own, so a microphone Windows refuses does not take the program's meter
/// with it; what was refused is kept in <see cref="Refused"/> for the screen to say once. A choice
/// not made is a channel not listened to, and it reads as nothing rather than as silence.
/// </para>
/// <para>
/// Two process loopbacks at once are not assumed to work, so this is closed before a meeting opens
/// the same devices. That is the window's to do: it holds one of these and lets it go first.
/// </para>
/// </remarks>
public sealed class ListeningBeforeAMeeting : IDisposable
{
    private SourceListening[] open;

    private ListeningBeforeAMeeting(SourceListening[] open, IReadOnlyList<string> refused)
    {
        this.open = open;
        Refused = refused;
    }

    /// <summary>
    /// Whether a source being listened to has ended by itself, so that what its meter reads is a
    /// device that is gone and not one that is quiet. The window opens the pair again on it.
    /// </summary>
    public bool AnySourceEnded => Volatile.Read(ref open).Any(listening => !listening.Listening);

    /// <summary>What was asked for and would not open, one sentence each.</summary>
    public IReadOnlyList<string> Refused { get; }

    /// <summary>
    /// Opens what was chosen. Never throws for a device: a refusal is a line in
    /// <see cref="Refused"/>. Waits on each device, so it and <see cref="Dispose"/> are called off
    /// the UI thread.
    /// </summary>
    public static ListeningBeforeAMeeting Open(AudioDevice? microphone, RecorderSource? source)
    {
        List<CaptureTarget> wanted = [];

        if (source is not null)
        {
            wanted.Add(source.Follow is { } program
                ? new CaptureTarget.Program(program)
                : new CaptureTarget.TheWholeMachine());
        }

        if (microphone is not null)
        {
            wanted.Add(new CaptureTarget.Endpoint(microphone));
        }

        List<SourceListening> opened = [];
        List<string> refused = [];

        try
        {
            foreach (var target in wanted)
            {
                try
                {
                    opened.Add(SourceListening.Open(target));
                }
                catch (Exception cannot) when (
                    cannot is AudioCaptureException
                        or System.Runtime.InteropServices.COMException)
                {
                    refused.Add($"'{target.Name}' could not be listened to: {cannot.Message}");
                }
            }
        }
        catch
        {
            // Anything else is not a device's refusal: let go of what is open, since nobody will
            // be handed it to dispose, and a program loopback left running would collide with the
            // meeting's own.
            new ListeningBeforeAMeeting([.. opened], refused).Dispose();
            throw;
        }

        return new ListeningBeforeAMeeting([.. opened], refused);
    }

    /// <summary>
    /// What each source being listened to reads, in channel order. Reading empties each level.
    /// </summary>
    public IReadOnlyList<ChannelReading> Read() =>
    [
        .. Volatile.Read(ref open)
            .OrderBy(listening => CapturedAudio.IndexOf(listening.Target.Channel))
            .Select(listening => ChannelReading.Of(
                listening.Target, listening.Level(), stoppedAt: null, opened: listening.Target)),
    ];

    /// <summary>Lets every source go. Never throws.</summary>
    public void Dispose()
    {
        foreach (var listening in Interlocked.Exchange(ref open, []))
        {
            try
            {
                listening.Dispose();
            }
            catch (Exception)
            {
                // Swallowed on purpose: a device that will not let go here is one the meeting
                // finds out about when it opens the same one.
            }
        }
    }
}
