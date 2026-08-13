using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One device being recorded onto one channel: the stream, the file it lands in and how loud it
/// has been. Nothing here knows there is another one.
/// </summary>
/// <remarks>
/// What it writes is the device's own format, unconverted and unaligned with anything else. That
/// is the point of it — the two sources arriving as they really are is what the timeline then has
/// to reconcile, and a resample done here would hide the very thing being measured.
/// </remarks>
public sealed class CaptureSource : IDisposable
{
    private static readonly TimeSpan StopsWithin = TimeSpan.FromSeconds(5);

    private readonly MMDevice endpoint;
    private readonly WasapiCapture client;
    private readonly WaveFileWriter writer;
    private readonly SourceMeter meter = new();
    private readonly ManualResetEventSlim ended = new(initialState: false);
    private readonly int bytesPerSecond;
    private long bytes;
    private Exception? failure;
    private bool running;

    private CaptureSource(
        AudioChannel channel,
        AudioDevice device,
        StreamFormat format,
        FileInfo file,
        MMDevice endpoint,
        WasapiCapture client,
        WaveFileWriter writer)
    {
        Channel = channel;
        Device = device;
        Format = format;
        File = file;
        this.endpoint = endpoint;
        this.client = client;
        this.writer = writer;
        bytesPerSecond = client.WaveFormat.AverageBytesPerSecond;
    }

    /// <summary>Which of the two channels this device feeds.</summary>
    public AudioChannel Channel { get; }

    /// <summary>The device Windows opened for it.</summary>
    public AudioDevice Device { get; }

    /// <summary>The format that device handed over.</summary>
    public StreamFormat Format { get; }

    /// <summary>Where its samples are being written.</summary>
    public FileInfo File { get; }

    /// <summary>When its stream opened.</summary>
    public UtcTimestamp StartedAt { get; private set; }

    /// <summary>How much audio has arrived, from the bytes that have actually been written.</summary>
    public Duration Recorded => bytesPerSecond > 0
        ? Duration.FromMilliseconds(Interlocked.Read(ref bytes) * 1000 / bytesPerSecond)
        : Duration.Zero;

    /// <summary>How many bytes have been written to <see cref="File"/>.</summary>
    public long Bytes => Interlocked.Read(ref bytes);

    /// <summary>The loudest this source has been since it opened.</summary>
    public LevelReading Loudest => meter.Loudest;

    /// <summary>
    /// Whether the stream is over. True before <see cref="Stop"/> means it ended on its own, and
    /// <see cref="Stop"/> is what says why.
    /// </summary>
    public bool HasEnded => ended.IsSet;

    /// <summary>The loudest block since this was last asked, which is what a meter shows.</summary>
    public LevelReading Level() => meter.Read();

    /// <summary>
    /// Stops the stream and finishes the file. A stream that had already ended by itself throws
    /// here, carrying the reason it ended.
    /// </summary>
    public void Stop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        client.StopRecording();

        if (!ended.Wait(StopsWithin))
        {
            throw new AudioCaptureException(
                $"The {Channel} stream did not stop within {StopsWithin.TotalSeconds:0} seconds.");
        }

        writer.Flush();

        if (failure is not null)
        {
            throw new AudioCaptureException(
                $"The {Channel} stream on '{Device.Name}' ended by itself: {failure.Message}", failure);
        }
    }

    public void Dispose()
    {
        // First, and on purpose: NAudio stops the stream and waits for its thread, so once this
        // returns nothing is still handing blocks to the writer.
        client.Dispose();

        // What patches the WAV header with the length actually written, which is why a capture
        // that was killed still leaves a file something can play.
        writer.Dispose();
        endpoint.Dispose();
        ended.Dispose();
        running = false;
    }

    /// <summary>
    /// Closes a source that never became part of a recording and takes its file with it. What it
    /// leaves behind would otherwise be a file with nothing in it, standing exactly where the
    /// next attempt wants to write.
    /// </summary>
    internal void Discard()
    {
        Dispose();
        File.Refresh();
        if (File.Exists)
        {
            File.Delete();
        }
    }

    /// <summary>
    /// Opens <paramref name="device"/> onto <paramref name="channel"/> and starts recording it.
    /// Anything the machine refuses comes back as a throw with nothing left open.
    /// </summary>
    internal static CaptureSource Open(
        AudioChannel channel,
        AudioDevice device,
        FileInfo file,
        Func<MMDevice, WasapiCapture> stream)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(stream);

        if (file.Exists)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' is already there, and a recording is not written over another one.");
        }

        var endpoint = AudioDevices.Open(device);
        WasapiCapture? client = null;
        WaveFileWriter? writer = null;
        try
        {
            client = stream(endpoint);
            var format = StreamFormat.Of(client.WaveFormat);
            writer = new WaveFileWriter(file.FullName, client.WaveFormat);

            var source = new CaptureSource(channel, device, format, file, endpoint, client, writer);
            source.Start();
            return source;
        }
        catch
        {
            writer?.Dispose();
            client?.Dispose();
            endpoint.Dispose();
            throw;
        }
    }

    private void Start()
    {
        client.DataAvailable += Captured;
        client.RecordingStopped += Ended;

        // NAudio initialises the device on this thread, so a device Windows will not hand over
        // throws here rather than on a capture thread nobody is watching.
        client.StartRecording();
        StartedAt = UtcTimestamp.From(TimeProvider.System.GetUtcNow());
        running = true;
    }

    private void Captured(object? sender, WaveInEventArgs block)
    {
        if (block.BytesRecorded <= 0)
        {
            return;
        }

        meter.Add(Levels.Peak(block.Buffer.AsSpan(0, block.BytesRecorded), Format));
        writer.Write(block.Buffer, 0, block.BytesRecorded);
        Interlocked.Add(ref bytes, block.BytesRecorded);
    }

    private void Ended(object? sender, StoppedEventArgs stopped)
    {
        failure = stopped.Exception;
        ended.Set();
    }
}
