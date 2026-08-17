using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The recording two spools become: one file, in the interchange format, with each source on the
/// channel the contract puts it on and nothing about it decided by when it was made.
/// </summary>
/// <remarks>
/// Every recording here is written the way a capture writes one — fabricated packets through
/// <see cref="SpoolWriter"/> — and then read back through the path a person's recovery takes. No
/// device is opened, so what these say is true on a machine with no sound card at all.
/// </remarks>
public sealed class MeetingAudioTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// Past the half minute after which the recording goes on without a source that has said
    /// nothing, which is the bound a source held back has to cross before anything goes wrong.
    /// </summary>
    private const double LongEnoughToBeGivenUp = 35;

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public MeetingAudioTests() => folder.Create();

    [Fact]
    public void Two_spools_become_one_recording_of_the_interchange_format()
    {
        Record(Fabricated.Bursts(0.5), Fabricated.Bursts(0.5), seconds: 3);

        var recording = MeetingAudio.Materialise(folder);

        recording.File.Name.ShouldBe("audio.wav");
        recording.Timeline.Sources.Count.ShouldBe(CapturedAudio.ChannelCount);
        recording.Length.Milliseconds.ShouldBeInRange(2_950, 3_050);

        using var played = new WaveFileReader(recording.File.FullName);
        StreamFormat.Of(played.WaveFormat).ShouldBe(MeetingAudio.Interchange);
        played.Length.ShouldBe(recording.Frames * CapturedAudio.ChannelCount * sizeof(short));
    }

    /// <summary>
    /// ISC-74. Only one source is ever loud, so a recording that put it on the other channel is one
    /// where every word of the meeting is attributed to the wrong side of it. Both directions,
    /// because a build that swapped the two everywhere would pass either one on its own.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_recording_never_carries_the_microphone_on_channel_0(bool loopbackSpeaks)
    {
        Record(
            loopbackSpeaks ? Fabricated.Bursts(0.25) : Fabricated.Quiet,
            loopbackSpeaks ? Fabricated.Quiet : Fabricated.Bursts(0.25),
            seconds: 2);

        var recording = MeetingAudio.Materialise(folder);

        recording.Loudest(AudioChannel.Loopback).IsSilent.ShouldBe(!loopbackSpeaks);
        recording.Loudest(AudioChannel.Microphone).IsSilent.ShouldBe(loopbackSpeaks);

        // Read off the file by the position in the frame, spelled out rather than looked up: that
        // position is the channel number the provider reports back, and a build that moved both the
        // contract and the recording together would still be putting somebody's microphone on
        // channel 0.
        var frames = Read(recording.File);
        if (loopbackSpeaks)
        {
            Peak(frames, position: 0).ShouldBeGreaterThan(Loudness.Loud);
            Peak(frames, position: 1).ShouldBe(0f);
        }
        else
        {
            Peak(frames, position: 0).ShouldBe(0f);
            Peak(frames, position: 1).ShouldBeGreaterThan(Loudness.Loud);
        }
    }

    /// <summary>
    /// ISC-76. The recording is read out of the spools and holds nothing they do not, so making it
    /// again is the same file — which is what lets somebody who was interrupted run the recovery a
    /// second time instead of being asked whether the first one counted.
    /// </summary>
    [Fact]
    public void Finishing_the_same_recording_twice_produces_the_same_file()
    {
        Record(Fabricated.Bursts(0.5), Fabricated.Bursts(0.3), seconds: 4);

        var first = MeetingAudio.Materialise(folder);
        var bytes = File.ReadAllBytes(first.File.FullName);

        var again = MeetingAudio.Materialise(folder);

        again.File.FullName.ShouldBe(first.File.FullName);
        again.Frames.ShouldBe(first.Frames);
        again.Length.ShouldBe(first.Length);
        again.Peaks.ShouldBe(first.Peaks);
        File.ReadAllBytes(again.File.FullName).ShouldBe(bytes);
    }

    /// <summary>
    /// A recording the machine was cut off in the middle of is still a recording. What the cut costs
    /// is that source's last block, said as audio it never delivered — and not the length of the
    /// meeting, which the other source still covers.
    /// </summary>
    [Fact]
    public void A_spool_that_was_cut_off_still_becomes_a_recording_of_what_landed()
    {
        Record(Fabricated.Bursts(0.5), Fabricated.Bursts(0.5), seconds: 3);
        var whole = MeetingAudio.Materialise(folder);

        CutOffMidBlock(AudioChannel.Microphone);
        var recording = MeetingAudio.Materialise(folder);

        recording.Frames.ShouldBe(whole.Frames);
        recording.Timeline.On(AudioChannel.Microphone).Missing.Milliseconds
            .ShouldBeGreaterThan(whole.Timeline.On(AudioChannel.Microphone).Missing.Milliseconds);
        recording.Timeline.On(AudioChannel.Loopback).Missing.ShouldBe(
            whole.Timeline.On(AudioChannel.Loopback).Missing);
    }

    /// <summary>
    /// A device that cannot say when it read one packet costs that packet's instant and nothing
    /// else. Ordering the two spools by an instant the device disowned would hold its source back
    /// past the half minute after which the recording goes on without it, and then refuse
    /// everything it had left to hand over — losing a meeting over a number nothing was going to
    /// read.
    /// </summary>
    /// <remarks>
    /// Both spools run past that half minute, because that is the bound the failure needs. The two
    /// runs differ only in the instant on the unvouched packet, which nothing measures against
    /// either way, so the recording has to come back sample for sample the same.
    /// </remarks>
    [Fact]
    public void A_packet_the_device_would_not_vouch_for_does_not_hold_its_source_back()
    {
        var wandered = Materialised(Unvouched(at: 40, aheadSeconds: 3_600));
        var beside = Materialised(Unvouched(at: 40, aheadSeconds: 0));

        wandered.Length.ShouldBe(beside.Length);
        wandered.Bytes.ShouldBe(beside.Bytes);
    }

    /// <summary>
    /// The name says which file to open and the header says which source it holds. A pair somebody
    /// swapped while recovering a meeting would otherwise decode each device's bytes as the other
    /// one's — and come back as a recording rather than as a refusal.
    /// </summary>
    [Fact]
    public void A_spool_under_the_other_sources_name_is_refused_rather_than_decoded_as_it()
    {
        Record(Fabricated.Bursts(0.5), Fabricated.Bursts(0.5), seconds: 1);
        Swap();

        Should.Throw<AudioCaptureException>(() => MeetingAudio.Materialise(folder))
            .Message.ShouldContain("says it holds the");

        MeetingAudio.In(folder).Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The file is put under its own name only once it has been read back, so a recording that could
    /// not be made leaves that name free rather than a file everything downstream would take for the
    /// meeting — and leaves nothing half written beside it either.
    /// </summary>
    [Fact]
    public void A_recording_that_could_not_be_made_leaves_no_file_pretending_to_be_one()
    {
        Write(
            folder,
            AudioChannel.Loopback,
            StereoFloat,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet));
        Write(folder, AudioChannel.Microphone, CheapMicrophone, CountersDisagree());

        Should.Throw<AudioCaptureException>(() => MeetingAudio.Materialise(folder))
            .Message.ShouldContain("44100 Hz");

        MeetingAudio.In(folder).Exists.ShouldBeFalse();
        folder.EnumerateFiles("*.partial").ShouldBeEmpty();
    }

    [Fact]
    public void Two_spools_that_never_delivered_a_block_are_not_a_recording()
    {
        foreach (var channel in new[] { AudioChannel.Loopback, AudioChannel.Microphone })
        {
            using (SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, StereoFloat))
            {
            }
        }

        Should.Throw<AudioCaptureException>(() => MeetingAudio.Materialise(folder))
            .Message.ShouldContain("no recording in it to make");

        MeetingAudio.In(folder).Exists.ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    /// <summary>The loudest one position of an interleaved frame gets, full scale at one.</summary>
    private static float Peak(short[] interleaved, int position)
    {
        var peak = 0f;
        for (var at = position; at < interleaved.Length; at += CapturedAudio.ChannelCount)
        {
            peak = MathF.Max(peak, MathF.Abs(Loudness.Of(interleaved[at])));
        }

        return peak;
    }

    /// <summary>Every sample of a finished recording, interleaved as the file holds them.</summary>
    private static short[] Read(FileInfo wav)
    {
        using var played = new WaveFileReader(wav.FullName);
        var bytes = new byte[played.Length];
        played.ReadExactly(bytes);

        return MemoryMarshal.Cast<byte, short>(bytes).ToArray();
    }

    /// <summary>
    /// A source whose two counters describe two different devices, which the timeline refuses
    /// outright — and therefore the way a recording is made to fail after it has already begun
    /// being written.
    /// </summary>
    /// <remarks>
    /// It used to be the frame counter moved back on itself, and that is no longer a refusal: a
    /// counter going backwards is the reading a microphone counting in its own rate produces, and
    /// nothing can tell the two apart, so what is given up on is the counter and not the meeting.
    /// A rate half the device's label survives as the refusal because it is measured against the
    /// clock, which is the one number nothing here replaces.
    /// </remarks>
    private static List<CapturePacket> CountersDisagree() =>
    [
        .. Fabricated.Packets(
            AudioChannel.Microphone, CheapMicrophone, realRate: 22_050, 0, 8, Fabricated.Quiet),
    ];

    /// <summary>
    /// The microphone's packets for a recording long enough for a source to be given up in, with
    /// the one at <paramref name="at"/> carrying an instant the device would not vouch for — its
    /// own, or one that many seconds ahead of where the device really was.
    /// </summary>
    private static List<CapturePacket> Unvouched(int at, double aheadSeconds)
    {
        var packets = Fabricated
            .Packets(AudioChannel.Microphone, CheapMicrophone, 44_100, 0, LongEnoughToBeGivenUp, Fabricated.Bursts(5))
            .ToList();

        packets[at] = packets[at] with
        {
            CapturedAt = MonotonicInstant.FromTicks(
                packets[at].CapturedAt.Ticks + (long)(aheadSeconds * MonotonicInstant.TicksPerSecond)),
            TimingIsSound = false,
        };

        return packets;
    }

    /// <summary>
    /// How long the recording those microphone packets make is, and the bytes it came out as. Each
    /// run gets a folder of its own, so neither can read what the other left.
    /// </summary>
    private (Duration Length, byte[] Bytes) Materialised(IEnumerable<CapturePacket> microphone)
    {
        var into = folder.CreateSubdirectory(Guid.NewGuid().ToString("n"));

        Write(
            into,
            AudioChannel.Loopback,
            StereoFloat,
            Fabricated.Packets(
                AudioChannel.Loopback, StereoFloat, 48_000, 0, LongEnoughToBeGivenUp, Fabricated.Bursts(5)));
        Write(into, AudioChannel.Microphone, CheapMicrophone, microphone);

        var recording = MeetingAudio.Materialise(into);
        return (recording.Length, File.ReadAllBytes(recording.File.FullName));
    }

    /// <summary>Both spools of one recording, each source hearing what it is given.</summary>
    private void Record(Func<double, float> loopback, Func<double, float> microphone, double seconds)
    {
        Write(
            folder,
            AudioChannel.Loopback,
            StereoFloat,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, seconds, loopback));
        Write(
            folder,
            AudioChannel.Microphone,
            CheapMicrophone,
            Fabricated.Packets(AudioChannel.Microphone, CheapMicrophone, 44_100, 0, seconds, microphone));
    }

    /// <summary>Puts each source's blocks under the other one's name.</summary>
    private void Swap()
    {
        var aside = Path.Combine(folder.FullName, "aside");
        var loopback = BlockSpool.FileFor(folder, AudioChannel.Loopback).FullName;
        var microphone = BlockSpool.FileFor(folder, AudioChannel.Microphone).FullName;

        File.Move(loopback, aside);
        File.Move(microphone, loopback);
        File.Move(aside, microphone);
    }

    private void Write(
        DirectoryInfo into,
        AudioChannel channel,
        StreamFormat format,
        IEnumerable<CapturePacket> packets)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(into, channel), channel, format);
        foreach (var packet in packets)
        {
            writer.Write(packet);
        }
    }

    /// <summary>Takes the tail off the way a process being killed mid write takes it off.</summary>
    private void CutOffMidBlock(AudioChannel channel)
    {
        var file = BlockSpool.FileFor(folder, channel);
        using var stream = file.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(file.Length - 32);
    }
}
