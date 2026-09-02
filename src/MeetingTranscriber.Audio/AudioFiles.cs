using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>What one audio file turned out to be, once it was read through.</summary>
/// <param name="Format">What is in it, in the terms a stream is read in.</param>
/// <param name="Frames">
/// Frames the file actually gives up, counted by reading it. Never the length its header
/// declares: that number is one a writer put there, a copy cut off half way leaves it untouched,
/// and it is what every citation into this meeting would then be checked against.
/// </param>
public sealed record AudioOnDisk(StreamFormat Format, long Frames)
{
    /// <summary>How long it is.</summary>
    public Duration Length => Duration.FromSeconds((double)Frames / Format.SampleRate);
}

/// <summary>
/// An audio file this engine did not write: what it is, and how it becomes the single track a
/// meeting with no channels to tell apart is transcribed as.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="MeetingAudio"/>, which reads two spools this application produced onto
/// the shared timeline and knows every number in the file before it opens it. Nothing here knows
/// anything: the rate, the width and the channel count are whatever whoever made the file chose,
/// and the only thing that says so is the file's own header.
/// </para>
/// <para>
/// WAV, and nothing else. There is no FFmpeg behind this application and no codec it could reach
/// for, so a container it cannot open is refused by name rather than half-decoded — and the
/// refusal is the honest answer, not a gap: what this product has is a recorder, and audio from
/// somewhere else arrives as the interchange every recorder on Windows can already write.
/// </para>
/// <para>
/// <b>A header is a claim and the bytes are the fact, and the two part company on exactly the
/// files this exists for.</b> A WAV's data chunk carries its own length, written before the audio
/// and never corrected, so a copy interrupted half way or a recorder whose battery died leaves a
/// file that says it is an hour long and gives up a minute. Everything here counts what it read.
/// <see cref="MeetingAudio.Verify"/> makes the same distinction for a recording this application
/// produced, and for the same reason.
/// </para>
/// <para>
/// Mixing down averages the channels rather than taking one, for the reason
/// <see cref="Samples.ToMono"/> gives: which side of a stereo pair the speech is on belongs to
/// whatever made the file, and a build that took the first channel would file half of those as
/// silence. However many channels there are — a track, a pair, the six a room's microphone array
/// hands over — they average the same way.
/// </para>
/// </remarks>
public static class AudioFiles
{
    /// <summary>How many channels a single track has, which is the shape a diarized meeting is.</summary>
    public const int OneTrack = 1;

    /// <summary>How many frames are read, converted and written at a time.</summary>
    private const int Batch = 4096;

    /// <summary>
    /// What the file at <paramref name="file"/> says it holds, off its header alone.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Read"/> because the two are asked for different reasons and cost
    /// different things. What a file <em>is</em> decides how it is brought in, and that is a
    /// question about eleven bytes of header; how long it is has to be counted, and counting means
    /// reading two hours of audio to answer it.
    /// </remarks>
    public static StreamFormat FormatOf(FileInfo file)
    {
        using var wav = Open(file);
        return Shape(wav, out _);
    }

    /// <summary>Whether this is already the single track a diarized meeting is transcribed as.</summary>
    public static bool IsOneTrack(StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        return format.Channels == OneTrack;
    }

    /// <summary>
    /// Whether this is, to the last field, the shape a recording of this application comes out as.
    /// </summary>
    /// <remarks>
    /// <see cref="MeetingAudio.Interchange"/> is the one declaration of what that shape is, and
    /// this asks it rather than carrying a copy — so a build that changed the rate a recording is
    /// made at changes what counts as one here in the same edit.
    /// </remarks>
    public static bool IsWhatThisApplicationRecords(StreamFormat format) =>
        MeetingAudio.Interchange == format;

    /// <summary>What the file at <paramref name="file"/> is, and how much of it there really is.</summary>
    public static AudioOnDisk Read(FileInfo file)
    {
        using var wav = Open(file);
        var format = Shape(wav, out var frameBytes);
        return new AudioOnDisk(format, Count(wav, frameBytes));
    }

    /// <summary>
    /// Writes <paramref name="file"/> out to <paramref name="into"/> as one track of 16-bit PCM at
    /// the rate it already runs at, and says what landed there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rate is kept and the width is not, and the two are not the same choice. Downsampling
    /// would throw away detail this application has no reason to want gone — the interchange
    /// format's 16 kHz is what a <em>capture</em> resamples its devices to, not a rule about audio
    /// somebody hands over. The width is converted because mixing produces a number rather than a
    /// sample, and 16-bit PCM is what this build reads back and what a recording of this product
    /// already is.
    /// </para>
    /// <para>
    /// A failure leaves nothing at <paramref name="into"/>. Half a WAV under the name the audio of
    /// a meeting is about to be read from is worse than none.
    /// </para>
    /// </remarks>
    public static AudioOnDisk MixDownToOneTrack(FileInfo file, FileInfo into)
    {
        ArgumentNullException.ThrowIfNull(into);

        using var wav = Open(file);
        var source = Shape(wav, out var frameBytes);
        var track = new StreamFormat(
            source.SampleRate, OneTrack, CapturedAudio.BitsPerSample, SampleEncoding.Pcm);

        try
        {
            long frames;
            using (var writer = new WaveFileWriter(into.FullName, BlockSpool.WaveFormatOf(track)))
            {
                frames = Pour(wav, source, frameBytes, writer);
            }

            into.Refresh();
            return new AudioOnDisk(track, frames);
        }
        catch
        {
            BlockSpool.Erase(into);
            throw;
        }
    }

    /// <summary>
    /// Opens a file as a WAV, saying so when it is not one rather than letting a parser's own
    /// words reach somebody who asked about a meeting.
    /// </summary>
    /// <remarks>
    /// Three ways a file fails to be a WAV and they arrive as three exceptions: something that is
    /// not RIFF at all, a chunk that does not read, and a file that stops before its header
    /// finishes — which is what an empty one is. All three mean the same thing to whoever typed
    /// the command, so they come back as one sentence naming the file.
    /// <para>
    /// Internal rather than private, because <see cref="Playback"/> hands the reader it gets back
    /// straight to an endpoint instead of closing it. That is the one caller that keeps the stream,
    /// and it goes through here so that a meeting whose audio will not open says the same sentence
    /// whether somebody asked to read it or to hear it.
    /// </para>
    /// </remarks>
    internal static WaveFileReader Open(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        file.Refresh();
        if (!file.Exists)
        {
            throw new AudioCaptureException($"There is no audio at '{file.FullName}'.");
        }

        try
        {
            return new WaveFileReader(file.FullName);
        }
        catch (Exception unreadable)
            when (unreadable is FormatException or InvalidDataException or EndOfStreamException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' does not read as a WAV file, and WAV is the only container "
                + $"this application opens: {unreadable.Message}");
        }
    }

    /// <summary>
    /// What the file says it holds, and how many bytes one frame of it occupies.
    /// </summary>
    /// <remarks>
    /// The frame size is computed from the channel count and the width rather than read off the
    /// header's own <c>BlockAlign</c>, because that is the arithmetic <see cref="Samples.ToMono"/>
    /// lays the bytes out by. A header whose two answers disagree is refused here: the reader
    /// aligns its reads by one of them and this would decode by the other, which lands as audio
    /// with the channels sliding past each other rather than as a failure. A rate of nothing is
    /// refused beside it — a file declaring one is not a shorter meeting, it is a length nothing
    /// can be worked out from.
    /// </remarks>
    private static StreamFormat Shape(WaveFileReader wav, out int frameBytes)
    {
        var format = StreamFormat.Of(wav.WaveFormat);
        frameBytes = format.Channels * format.BytesPerSample;

        if (frameBytes <= 0 || frameBytes != wav.WaveFormat.BlockAlign)
        {
            throw new AudioCaptureException(
                $"A file saying it is {format} lays its frames out in {wav.WaveFormat.BlockAlign} "
                + $"bytes rather than {frameBytes}. Its header does not agree with itself.");
        }

        if (format.SampleRate <= 0)
        {
            throw new AudioCaptureException(
                $"A file saying it is {format} runs at no rate at all, so there is no length its "
                + "audio could have.");
        }

        return format;
    }

    /// <summary>
    /// Reads the file through and says how many whole frames it gave up.
    /// </summary>
    /// <remarks>
    /// The bytes rather than the header, which is the whole reason this costs a read at all. What
    /// it drops is a tail that is not a whole frame — a file cut off mid-frame costs its last one
    /// and not the meeting in it, the same trade a spool makes with the block a machine died
    /// inside.
    /// </remarks>
    private static long Count(WaveFileReader wav, int frameBytes)
    {
        var block = new byte[frameBytes * Batch];
        var bytes = 0L;

        int read;
        while ((read = wav.Read(block, 0, block.Length)) > 0)
        {
            bytes += read;
        }

        return bytes / frameBytes;
    }

    /// <summary>
    /// Runs every whole frame of the file through the mix down and into the writer, and says how
    /// many went in.
    /// </summary>
    /// <remarks>
    /// Every read asks for the whole block and never for what is left of it, which is not a
    /// simplification but the only thing the reader allows: it refuses a request that is not a
    /// whole number of frames outright, so a loop carrying a remainder forward could only ever
    /// carry it into that refusal. A read that comes back short is the file ending inside a frame,
    /// and the frame it ended inside is what it costs.
    /// </remarks>
    private static long Pour(WaveFileReader wav, StreamFormat source, int frameBytes, WaveFileWriter into)
    {
        var block = new byte[frameBytes * Batch];
        var mono = new float[Batch];
        var pcm = new short[Batch];
        var frames = 0L;

        int read;
        while ((read = wav.Read(block, 0, block.Length)) > 0)
        {
            var whole = read - (read % frameBytes);
            var count = whole / frameBytes;

            if (count > 0)
            {
                Samples.ToMono(block.AsSpan(0, whole), source, mono);
                Samples.ToPcm16(mono.AsSpan(0, count), pcm);
                into.Write(MemoryMarshal.AsBytes(pcm.AsSpan(0, count)));
                frames += count;
            }

            if (whole != read)
            {
                break;
            }
        }

        return frames;
    }
}
