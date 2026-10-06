using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One source opened for listening and nothing else: its level, read the way a meter reads it,
/// with no spool, no file and no meeting.
/// </summary>
/// <remarks>
/// <para>
/// What a person choosing a microphone or a program needs is to hear whether the one they picked
/// is the one that is playing, and that is a question asked before there is anything to record.
/// So this is <see cref="CaptureSource"/> with everything that makes a recording taken out: the
/// stream is drained and every block is measured and dropped.
/// </para>
/// <para>
/// It lives in this assembly because <see cref="CaptureTarget"/> opens its stream through a member
/// only this assembly can reach, and because opening goes through the target's own rule, which is
/// what keeps the deadline <see cref="DeviceOpen"/> gives every device and the refusals
/// <see cref="ReopenedSource"/> makes. A stream that ends stops feeding and nothing else: there is
/// no recording for it to have cut short, so it is never reported as one that died.
/// </para>
/// </remarks>
public sealed class SourceListening : IDisposable
{
    private readonly WasapiStream stream;
    private readonly SourceMeter meter = new();
    private volatile bool listening = true;

    private SourceListening(CaptureTarget target, WasapiStream stream)
    {
        Target = target;
        this.stream = stream;
    }

    /// <summary>What this is listening to, which also says which channel it would feed.</summary>
    public CaptureTarget Target { get; }

    /// <summary>
    /// Whether blocks are still arriving from it: false once its stream has ended — a device
    /// unplugged while somebody was choosing, which reads as silence and is not.
    /// </summary>
    public bool Listening => listening;

    /// <summary>
    /// Opens <paramref name="target"/> and starts measuring it. Anything the machine refuses, or a
    /// device that never answers, comes back as a throw with nothing left open. It waits on the
    /// device for up to <see cref="DeviceOpen"/>'s deadline, so it is not called on a UI thread.
    /// </summary>
    public static SourceListening Open(CaptureTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var stream = target.Open(carryingOn: null);
        try
        {
            var format = StreamFormat.Of(stream.WaveFormat);

            // Asked before the device is started, for the reason a recording asks it: a width
            // nothing here can read is knowable now, and a meter that throws on its first block is
            // a thread nobody is watching.
            Levels.EnsureMeterable(format);

            var opened = new SourceListening(target, stream);
            stream.Start(
                packet =>
                {
                    if (packet.Samples.Length > 0)
                    {
                        opened.meter.Add(Levels.Peak(packet.Samples.Span, format));
                    }
                },
                _ => opened.listening = false);
            return opened;
        }
        catch
        {
            DeviceRelease.LetGoOf(stream);
            throw;
        }
    }

    /// <summary>
    /// The loudest it has been since the look before. Reading empties it, as every meter does.
    /// </summary>
    public LevelReading Level() => meter.Read();

    /// <inheritdoc/>
    public void Dispose()
    {
        listening = false;
        DeviceRelease.LetGoOf(stream);
    }
}
