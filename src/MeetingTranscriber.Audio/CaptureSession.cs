using MeetingTranscriber.Domain.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The two sources of one recording, open at the same time: what the machine is playing on
/// channel 0 and what the chosen microphone hears on channel 1.
/// </summary>
/// <remarks>
/// <para>
/// Both or neither. Half a meeting is not a smaller meeting — it is a recording that looks
/// complete and has lost one side of the conversation — so a session that cannot open its second
/// source closes the first one and says so.
/// </para>
/// <para>
/// It captures the whole machine, not one process: which application to follow is the next
/// question and it changes only how channel 0 is opened.
/// </para>
/// </remarks>
public sealed class CaptureSession : IDisposable
{
    private readonly CaptureSource[] sources;
    private readonly SilentPlayback silence;

    private CaptureSession(CaptureSource[] sources, SilentPlayback silence)
    {
        this.sources = sources;
        this.silence = silence;
    }

    /// <summary>Both sources, in channel order.</summary>
    public IReadOnlyList<CaptureSource> Sources => sources;

    /// <summary>How channel 0 was obtained. Everything the machine plays, for now.</summary>
    public CaptureMode Mode => CaptureMode.FullLoopback;

    /// <summary>
    /// Opens both streams into <paramref name="folder"/>, one file each, and starts recording.
    /// </summary>
    /// <param name="folder">Where the two files go. Made if it is not there.</param>
    /// <param name="playback">The endpoint channel 0 listens to.</param>
    /// <param name="microphone">The device channel 1 listens to.</param>
    public static CaptureSession Start(DirectoryInfo folder, AudioDevice playback, AudioDevice microphone)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(microphone);

        folder.Create();

        // Before channel 0 opens, so the endpoint is already handing packets over by the time
        // anything is listening for them.
        var silence = SilentPlayback.On(playback);

        var opened = new List<CaptureSource>();
        try
        {
            foreach (var (channel, device) in InChannelOrder(playback, microphone))
            {
                opened.Add(CaptureSource.Open(channel, device, FileFor(folder, channel), StreamOf(channel)));
            }
        }
        catch
        {
            foreach (var source in opened)
            {
                source.Discard();
            }

            silence.Dispose();
            throw;
        }

        return new CaptureSession([.. opened], silence);
    }

    /// <summary>The source feeding <paramref name="channel"/>.</summary>
    public CaptureSource On(AudioChannel channel) =>
        Array.Find(sources, source => source.Channel == channel)
        ?? throw new AudioContractException($"This capture has no {channel} source.");

    /// <summary>
    /// Stops both streams and finishes both files. Every source is asked to stop before either is
    /// waited on, and every one of them is stopped even when another has something to say about
    /// how it ended — the other one's file is a recording somebody wants either way.
    /// </summary>
    public void Stop()
    {
        var failures = new List<Exception>();

        // Both asked, then both waited on. The other way round leaves the second source recording
        // for however long the first one took to let go, which is a difference between the two
        // files that nothing in the meeting put there.
        foreach (var source in sources)
        {
            Collect(source.AskToStop, failures);
        }

        foreach (var source in sources)
        {
            Collect(source.Finish, failures);
        }

        if (failures.Count == 1)
        {
            throw failures[0] as AudioCaptureException
                ?? new AudioCaptureException(failures[0].Message, failures[0]);
        }

        if (failures.Count > 1)
        {
            throw new AudioCaptureException(
                string.Join(" ", failures.Select(failure => failure.Message)), failures[0]);
        }
    }

    public void Dispose()
    {
        foreach (var source in sources)
        {
            // Every source, whatever the one before it did with its last block: this is where the
            // handles are let go, and Stop is where a failure is reported.
            source.LetGo();
        }

        silence.Dispose();
    }

    /// <summary>
    /// Runs one step of stopping and keeps what it threw, so that the step after it still runs.
    /// Broad on purpose: a source left recording because another one failed is the outcome this
    /// exists to prevent, and nothing is lost — what was caught is what gets thrown.
    /// </summary>
    private static void Collect(Action step, List<Exception> failures)
    {
        try
        {
            step();
        }
        catch (Exception failed)
        {
            failures.Add(failed);
        }
    }

    /// <summary>
    /// Which device feeds which channel, ordered by the contract rather than by the order these
    /// two are written in, so channel 0 is opened first because it is channel 0.
    /// </summary>
    private static IEnumerable<(AudioChannel Channel, AudioDevice Device)> InChannelOrder(
        AudioDevice playback,
        AudioDevice microphone) =>
        new[]
        {
            (Channel: AudioChannel.Microphone, Device: microphone),
            (Channel: AudioChannel.Loopback, Device: playback),
        }.OrderBy(source => CapturedAudio.IndexOf(source.Channel));

    private static Func<MMDevice, WasapiCapture> StreamOf(AudioChannel channel) => channel switch
    {
        AudioChannel.Loopback => endpoint => new WasapiLoopbackCapture(endpoint),
        AudioChannel.Microphone => endpoint => new WasapiCapture(endpoint),
        _ => throw new AudioContractException($"There is no way to capture '{channel}'."),
    };

    private static FileInfo FileFor(DirectoryInfo folder, AudioChannel channel) =>
        new(Path.Combine(folder.FullName, $"{channel.ToString().ToLowerInvariant()}.wav"));
}
