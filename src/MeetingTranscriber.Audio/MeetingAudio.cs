using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// The recording itself: both spools read back onto the shared timeline and poured into one
/// interleaved stereo file, in the interchange format, checked by being read again.
/// </summary>
/// <remarks>
/// <para>
/// This is the file the meeting is — what a transcription is asked about, and what a citation's
/// offsets are offsets into. The files <see cref="BlockSpool.ToWav"/> makes are one source each,
/// unaligned and at whatever rate its device ran, for somebody to hear whether a device was
/// recording at all.
/// </para>
/// <para>
/// It holds nothing the spools do not, so making it again is always allowed and always produces
/// the same bytes: the spools are the recording and this is read out of them. That is what lets a
/// recording somebody's machine died in the middle of become a meeting without anything having to
/// be decided about the spools first.
/// </para>
/// <para>
/// It lands under its own name only after it has been read back off the disk. A half-written
/// <c>audio.wav</c> is worse than none — it is the file everything downstream would take for the
/// meeting — so a failure leaves whatever that name held before it and never half of something:
/// nothing at all the first time, and the recording those same spools already made every time
/// after, which is the same recording this would have produced.
/// </para>
/// </remarks>
public static class MeetingAudio
{
    /// <summary>What the recording is called, beside the spools it was made from.</summary>
    public const string FileName = "audio.wav";

    /// <summary>
    /// What every recording comes out as, in the terms a stream is read in, so that what the file
    /// says about itself can be compared against the contract rather than against a second copy of
    /// the numbers.
    /// </summary>
    public static readonly StreamFormat Interchange = new(
        CapturedAudio.SampleRate,
        CapturedAudio.ChannelCount,
        CapturedAudio.BitsPerSample,
        SampleEncoding.Pcm);

    /// <summary>What a recording is called while it is being written and has not been checked.</summary>
    private const string Unfinished = ".partial";

    /// <summary>Where a folder's recording is, once there is one.</summary>
    public static FileInfo In(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }

    /// <summary>
    /// Reads both of <paramref name="folder"/>'s spools onto one timeline and writes the recording,
    /// replacing whatever was under that name before.
    /// </summary>
    /// <remarks>
    /// Both spools or neither. A meeting with one side of the conversation missing is not a shorter
    /// meeting, and the timeline is two named sources for that reason — so a folder holding one
    /// spool is a recovery somebody has to look at, not a recording to make half of.
    /// </remarks>
    public static Materialised Materialise(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var recording = In(folder);
        var unfinished = new FileInfo(recording.FullName + Unfinished);

        using var loopback = Spool(folder, AudioChannel.Loopback);
        using var microphone = Spool(folder, AudioChannel.Microphone);

        try
        {
            var (frames, timeline) = Pour(loopback, microphone, unfinished);
            if (frames <= 0)
            {
                throw new AudioCaptureException(
                    $"'{folder.FullName}' holds two spools and no audio: neither source delivered a "
                    + "block, so there is no recording in it to make.");
            }

            var peaks = Verify(unfinished, frames);

            // The recording exists at its own name the moment it is one, and not before: everything
            // above can still fail, and everything below is reading a file that is already whole.
            File.Move(unfinished.FullName, recording.FullName, overwrite: true);
            recording.Refresh();

            return new Materialised(recording, frames, timeline, peaks);
        }
        catch
        {
            Erase(unfinished);
            throw;
        }
    }

    /// <summary>
    /// One source's spool, opened under the name that source's blocks go by and held to what its
    /// own header says it holds.
    /// </summary>
    /// <remarks>
    /// The name is how the file is found and the header is what it is. Everything below binds a
    /// format to a source by which file it came out of while the timeline lays each packet out by
    /// the channel written inside it, so a pair somebody swapped while recovering a meeting would
    /// decode one device's bytes as the other's — quietly, and as a recording rather than as a
    /// refusal. The header is checksummed and the name is not, so the header decides.
    /// </remarks>
    private static SpoolReader Spool(DirectoryInfo folder, AudioChannel channel)
    {
        var file = BlockSpool.FileFor(folder, channel);
        var spool = SpoolReader.Open(file);

        try
        {
            if (spool.Channel != channel)
            {
                throw new AudioCaptureException(
                    $"'{file.Name}' says it holds the {spool.Channel} source. A spool carries which "
                    + "source it is, and a recording built on the name instead would put one "
                    + "device's audio on the other one's channel.");
            }

            return spool;
        }
        catch
        {
            spool.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs both spools through the timeline into a WAV, and says how many frames went in and what
    /// each source turned out to be.
    /// </summary>
    private static (long Frames, TimelineSummary Timeline) Pour(
        SpoolReader loopback,
        SpoolReader microphone,
        FileInfo into)
    {
        using var wav = new WaveFileWriter(into.FullName, BlockSpool.WaveFormatOf(Interchange));
        var aligned = new AlignedWav(wav);
        var timeline = SharedTimeline.Of(loopback.Format, microphone.Format, aligned);

        foreach (var packet in InOrder(loopback.Packets(), microphone.Packets()))
        {
            timeline.Take(packet);
        }

        // Closed first and counted after. Closing is where the source that ran out is padded and
        // the resamplers hand over what they were still holding, which is the end of the meeting.
        var timed = timeline.Close();
        return (aligned.Frames, timed);
    }

    /// <summary>
    /// The two spools as one run of packets, in the order the devices read them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Merged rather than one whole source and then the other. The timeline hands on every frame
    /// both sources cover and holds back what only one of them does, so a spool arriving before the
    /// other one had started would be two hours of a meeting held in memory. Both files are already
    /// in their own order, so keeping them in the shared one costs a packet of each.
    /// </para>
    /// <para>
    /// A packet whose instant the device would not vouch for is placed by the last instant that
    /// source did vouch for, and never by its own. Its samples and its position are still the
    /// meeting — that is the whole point of the flag — but its instant is a number the device
    /// disowned, and one that happened to land an hour ahead would hold this source back until the
    /// timeline had given it up and refused everything after it. A recording is not lost over a
    /// value nothing was going to read.
    /// </para>
    /// </remarks>
    private static IEnumerable<CapturePacket> InOrder(
        IEnumerable<CapturePacket> loopback,
        IEnumerable<CapturePacket> microphone)
    {
        using var others = loopback.GetEnumerator();
        using var mine = microphone.GetEnumerator();
        var hasOthers = others.MoveNext();
        var hasMine = mine.MoveNext();

        // The last instant each source was placed by. Only ever read for the packet at the head of
        // that source, which is the one being compared, so a vouched head writing its own instant
        // here is what the unvouched packet behind it will be placed by.
        var placed = new long[CapturedAudio.ChannelCount];

        long Placing(CapturePacket packet)
        {
            var index = CapturedAudio.IndexOf(packet.Channel);
            if (packet.TimingIsSound)
            {
                placed[index] = packet.CapturedAt.Ticks;
            }

            return placed[index];
        }

        while (hasOthers || hasMine)
        {
            if (hasOthers && (!hasMine || Placing(others.Current) <= Placing(mine.Current)))
            {
                yield return others.Current;
                hasOthers = others.MoveNext();
            }
            else
            {
                yield return mine.Current;
                hasMine = mine.MoveNext();
            }
        }
    }

    /// <summary>
    /// Reads the recording back off the disk and asks it what it is: whether it opens, whether it
    /// is the interchange format, whether it is as long as the timeline said, and how loud each
    /// channel got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read again rather than counted while writing. What a writer believes it wrote is not
    /// evidence about a file, and a WAV is the container where that gap is widest: its length lives
    /// in a header patched at the close, so what says the file is that long is a number the writer
    /// put there and not the audio behind it.
    /// </para>
    /// <para>
    /// Which is why the header is asked and then the bytes are counted. A file that says it holds
    /// two hours and stops after twenty minutes agrees with itself on every question but that one,
    /// and it is the one downstream reads the length off.
    /// </para>
    /// <para>
    /// What this cannot say is that the disk kept it: nothing here asks for a flush, so the bytes
    /// read back may be the operating system's. That is deliberate. The recording is produced again
    /// from the spools beside it whenever anybody asks, which is the whole of what makes it
    /// replaceable — the durability ceremony belongs to what cannot be obtained twice.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<LevelReading> Verify(FileInfo wav, long frames)
    {
        using var played = new WaveFileReader(wav.FullName);

        var format = StreamFormat.Of(played.WaveFormat);
        if (format != Interchange)
        {
            throw new AudioCaptureException(
                $"The recording came back as {format}, and a recording is {Interchange}.");
        }

        var frameBytes = Interchange.Channels * Interchange.BytesPerSample;
        if (played.Length != frames * frameBytes)
        {
            throw new AudioCaptureException(
                $"The recording says it is {played.Length / frameBytes} frames long and the timeline "
                + $"handed over {frames}. What is on the disk is not the recording that was made.");
        }

        var peaks = new float[CapturedAudio.ChannelCount];
        var block = new byte[frameBytes * 4096];
        var counted = 0L;
        int read;
        while ((read = played.Read(block, 0, block.Length)) > 0)
        {
            counted += read;
            for (var channel = 0; channel < peaks.Length; channel++)
            {
                peaks[channel] = MathF.Max(
                    peaks[channel], Levels.Peak(block.AsSpan(0, read), Interchange, channel).Peak);
            }
        }

        if (counted != frames * frameBytes)
        {
            throw new AudioCaptureException(
                $"The recording says it is {frames} frames long and gives up {counted / frameBytes}. "
                + "What is on the disk is not the recording that was made.");
        }

        return [.. peaks.Select(peak => new LevelReading(peak))];
    }

    /// <summary>
    /// Removes a recording that never became one. It does not throw: what the caller has to hear is
    /// why the recording failed, and an unfinished file left behind is only litter.
    /// </summary>
    private static void Erase(FileInfo unfinished)
    {
        try
        {
            File.Delete(unfinished.FullName);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see the summary.
        }
    }

    /// <summary>The timeline's frames on their way into a WAV, counted as they pass.</summary>
    /// <remarks>
    /// The count is what the file is then checked against, and it is kept here rather than asked of
    /// the writer because it is the timeline's own answer: how much recording was handed over, in
    /// the frames it was handed over in.
    /// </remarks>
    private sealed class AlignedWav(WaveFileWriter wav) : IAlignedAudio
    {
        /// <summary>Stereo frames written so far.</summary>
        internal long Frames { get; private set; }

        public void Write(ReadOnlySpan<short> frames)
        {
            wav.Write(MemoryMarshal.AsBytes(frames));
            Frames += frames.Length / CapturedAudio.ChannelCount;
        }
    }
}

/// <summary>What one recording turned out to be, once it was written and read back.</summary>
/// <param name="File">The recording, under its own name.</param>
/// <param name="Frames">Stereo frames in it, which is its length with nothing rounded off.</param>
/// <param name="Timeline">What each source contributed to it.</param>
/// <param name="Peaks">The loudest each channel got over the whole of it, in channel order.</param>
public sealed record Materialised(
    FileInfo File,
    long Frames,
    TimelineSummary Timeline,
    IReadOnlyList<LevelReading> Peaks)
{
    /// <summary>How long the recording is.</summary>
    public Duration Length => Timeline.Length;

    /// <summary>The loudest <paramref name="channel"/> got over the whole recording.</summary>
    public LevelReading Loudest(AudioChannel channel) => Peaks[CapturedAudio.IndexOf(channel)];
}
