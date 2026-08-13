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

    private CaptureSession(CaptureSource[] sources) => this.sources = sources;

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
                try
                {
                    source.Discard();
                }
                catch (Exception left) when (left is IOException or UnauthorizedAccessException)
                {
                    // Why this one is swallowed: what the caller needs is the reason the session
                    // could not start, and a file that would not delete is a smaller problem than
                    // that reason arriving as "the file is in use".
                }
            }

            throw;
        }

        return new CaptureSession([.. opened]);
    }

    /// <summary>The source feeding <paramref name="channel"/>.</summary>
    public CaptureSource On(AudioChannel channel) =>
        Array.Find(sources, source => source.Channel == channel)
        ?? throw new AudioContractException($"This capture has no {channel} source.");

    /// <summary>
    /// Stops both streams and finishes both files. Every source is stopped even when one of them
    /// has something to say about how it ended, because the other one's file is a recording
    /// somebody wants either way.
    /// </summary>
    public void Stop()
    {
        var failures = new List<Exception>();
        foreach (var source in sources)
        {
            try
            {
                source.Stop();
            }
            catch (AudioCaptureException failure)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count == 1)
        {
            throw failures[0];
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
            source.Dispose();
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
