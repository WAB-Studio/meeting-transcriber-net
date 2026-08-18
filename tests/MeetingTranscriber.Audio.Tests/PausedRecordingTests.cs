using System.Runtime.InteropServices;

using MeetingTranscriber.Domain.Audio;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What pausing a meeting does to the recording of it: nothing to its length, and everything to
/// what is in the stretch that was paused.
/// </summary>
/// <remarks>
/// The room is loud for the whole of every recording here, pause included. That is the point of
/// these: a test whose fabricated devices went quiet while the pause was on would pass against a
/// build that had never implemented one.
/// </remarks>
public sealed class PausedRecordingTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// Past the half minute after which the timeline goes on without a source that has said
    /// nothing. A pause has to survive being longer than this, and that is the whole reason a
    /// paused block is written as silence rather than not written at all.
    /// </summary>
    private const double LongerThanASourceIsGivenUpOn = 35;

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public PausedRecordingTests() => folder.Create();

    /// <summary>
    /// Every number a block carries says where it belongs, and a pause moves none of them: what it
    /// takes out is the audio and nothing else.
    /// </summary>
    [Fact]
    public void A_paused_block_keeps_where_it_belongs_and_loses_what_was_in_it()
    {
        var pause = new RecordingPause();
        var heard = Room(AudioChannel.Microphone);

        pause.Pause();
        var paused = pause.Reaching(heard);

        paused.Channel.ShouldBe(heard.Channel);
        paused.DevicePosition.ShouldBe(heard.DevicePosition);
        paused.CapturedAt.ShouldBe(heard.CapturedAt);
        paused.TimingIsSound.ShouldBe(heard.TimingIsSound);
        paused.Samples.Length.ShouldBe(heard.Samples.Length);
        paused.Samples.ToArray().ShouldAllBe(sample => sample == 0);

        // What the device handed over is left alone: the block is substituted on the way to the
        // spool, and a build that zeroed it in place would be erasing the caller's own buffer.
        heard.Samples.ToArray().ShouldAllBe(sample => sample == 0x7f);
    }

    /// <summary>
    /// A recording nobody paused is untouched, and one that was resumed is untouched again — the
    /// very same block, so the ordinary path is not quietly copying every packet of every meeting.
    /// </summary>
    [Fact]
    public void What_the_devices_hear_reaches_the_recording_until_somebody_pauses()
    {
        var pause = new RecordingPause();
        var heard = Room(AudioChannel.Microphone);

        pause.IsPaused.ShouldBeFalse();
        pause.Reaching(heard).ShouldBeSameAs(heard);

        pause.Pause();
        pause.IsPaused.ShouldBeTrue();
        pause.Reaching(heard).ShouldNotBeSameAs(heard);

        pause.Resume();
        pause.IsPaused.ShouldBeFalse();
        pause.Reaching(heard).ShouldBeSameAs(heard);
    }

    /// <summary>
    /// One pause for the recording and not one per source: both channels read the same answer at
    /// the same moment, so there is no window in which one of them is still recording the room
    /// after somebody pressed pause.
    /// </summary>
    [Fact]
    public void Both_channels_pause_on_the_one_answer()
    {
        var pause = new RecordingPause();
        var others = Room(AudioChannel.Loopback);
        var me = Room(AudioChannel.Microphone);

        pause.Pause();

        pause.Reaching(others).Samples.ToArray().ShouldAllBe(sample => sample == 0);
        pause.Reaching(me).Samples.ToArray().ShouldAllBe(sample => sample == 0);
    }

    /// <summary>
    /// A block bigger than the silence kept for the ordinary one is still silence, so a device
    /// handing over a quarter of a second at a time does not quietly go on recording the room.
    /// </summary>
    [Fact]
    public void A_block_larger_than_any_kept_silence_is_silenced_too()
    {
        var big = new byte[256 * 1024];
        Array.Fill(big, (byte)0x7f);

        var pause = new RecordingPause();
        pause.Pause();

        var paused = pause.Reaching(
            new CapturePacket(AudioChannel.Loopback, 0, MonotonicInstant.FromMilliseconds(0), big));

        paused.Samples.Length.ShouldBe(big.Length);
        paused.Samples.ToArray().ShouldAllBe(sample => sample == 0);
    }

    /// <summary>
    /// ISC-81, at the length that matters. A pause longer than the half minute after which the
    /// timeline gives up on a silent source is the case a pause built out of gaps loses the meeting
    /// to — the recording would come back as long as the stretch before the pause, and the minute
    /// after it would be at the wrong minute for the rest of the meeting.
    /// </summary>
    [Fact]
    public void A_meeting_paused_longer_than_a_source_is_given_up_on_is_as_long_as_the_clock_says()
    {
        const double before = 5;
        const double after = 5;
        var whole = before + LongerThanASourceIsGivenUpOn + after;

        Record(whole, pausedFrom: before, pausedUntil: before + LongerThanASourceIsGivenUpOn);

        var recording = MeetingAudio.Materialise(folder);

        // The meeting is as long as it took, pause and all.
        recording.Length.Milliseconds.ShouldBeInRange(
            (long)((whole * 1_000) - 100), (long)((whole * 1_000) + 100));

        var frames = Read(recording.File);

        // The room was loud throughout, so what is quiet is exactly what was paused.
        Loudest(frames, from: 1, until: before - 0.5).ShouldBeGreaterThan(Loudness.Loud);
        Loudest(frames, from: before + 0.5, until: before + LongerThanASourceIsGivenUpOn - 0.5)
            .ShouldBe(0f);
        Loudest(frames, from: before + LongerThanASourceIsGivenUpOn + 0.5, until: whole - 0.5)
            .ShouldBeGreaterThan(Loudness.Loud);
    }

    /// <summary>
    /// The minute after a pause is at the minute of the meeting it was said in, which is what the
    /// transcript's clock being the meeting's clock comes down to. Measured against a marker rather
    /// than against the file's length, because a recording can be the right length overall and
    /// still have moved everything after the pause forward.
    /// </summary>
    [Fact]
    public void What_was_said_after_a_pause_is_where_it_was_said_in_the_meeting()
    {
        const double pausedFrom = 2;
        const double pausedUntil = 5;
        const double spoke = 7;

        // Quiet everywhere except one burst well after the pause, so where that burst lands is the
        // whole answer.
        Record(
            seconds: 9,
            pausedFrom,
            pausedUntil,
            room: at => at >= spoke && at < spoke + 0.2 ? 0.9f : 0f);

        var frames = Read(MeetingAudio.Materialise(folder).File);

        Loudest(frames, from: spoke, until: spoke + 0.2).ShouldBeGreaterThan(Loudness.Loud);

        // And nowhere else: a build that closed the pause up would have put it three seconds early.
        Loudest(frames, from: 0, until: spoke - 0.1).ShouldBe(0f);
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    /// <summary>
    /// Both sources of one meeting, with the stretch between <paramref name="pausedFrom"/> and
    /// <paramref name="pausedUntil"/> going through the substitution a paused capture makes.
    /// </summary>
    private void Record(
        double seconds,
        double pausedFrom,
        double pausedUntil,
        Func<double, float>? room = null)
    {
        var heard = room ?? Fabricated.Bursts(0.25);

        // The one a recording has, shared by both sources exactly as a capture shares it - so what
        // these spools are written through is the production object rather than a stand-in for it.
        Write(
            new RecordingPause(),
            AudioChannel.Loopback,
            StereoFloat,
            48_000,
            seconds,
            heard,
            pausedFrom,
            pausedUntil);
        Write(
            new RecordingPause(),
            AudioChannel.Microphone,
            CheapMicrophone,
            44_100,
            seconds,
            heard,
            pausedFrom,
            pausedUntil);
    }

    /// <summary>
    /// One source's spool, with pause and resume pressed at the seconds they were asked for and
    /// every block going through <see cref="RecordingPause.Reaching"/> on its way to the writer -
    /// which is exactly what a capture does with the block its device hands it.
    /// </summary>
    private void Write(
        RecordingPause pause,
        AudioChannel channel,
        StreamFormat format,
        double realRate,
        double seconds,
        Func<double, float> room,
        double pausedFrom,
        double pausedUntil)
    {
        // The device's own position says when the block was, which is the only clock a paused
        // capture has: the pause is pressed by when the block belongs and never by counting them.
        var framesPerSecond = format.SampleRate;
        var pressed = false;
        var released = false;

        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, format);
        foreach (var packet in Fabricated.Packets(channel, format, realRate, 0, seconds, room))
        {
            var at = packet.DevicePosition / (double)framesPerSecond;

            if (!pressed && at >= pausedFrom)
            {
                pause.Pause();
                pressed = true;
            }

            if (!released && at >= pausedUntil)
            {
                pause.Resume();
                released = true;
            }

            writer.Write(pause.Reaching(packet));
        }
    }

    /// <summary>
    /// One block of a loud room, with numbers on it nothing could produce by accident — so a
    /// pause that moved any of them shows up as that number rather than as a rounding.
    /// </summary>
    private static CapturePacket Room(AudioChannel channel)
    {
        var heard = new byte[960];
        Array.Fill(heard, (byte)0x7f);

        return new CapturePacket(
            channel,
            DevicePosition: 96_000,
            MonotonicInstant.FromMilliseconds(1_234_567),
            heard,
            TimingIsSound: false);
    }

    /// <summary>The loudest either channel gets between those two seconds of the recording.</summary>
    private static float Loudest(short[] interleaved, double from, double until)
    {
        var first = (int)(from * CapturedAudio.SampleRate) * CapturedAudio.ChannelCount;
        var last = Math.Min(
            (int)(until * CapturedAudio.SampleRate) * CapturedAudio.ChannelCount, interleaved.Length);

        var peak = 0f;
        for (var at = Math.Max(first, 0); at < last; at++)
        {
            peak = MathF.Max(peak, MathF.Abs(Loudness.Of(interleaved[at])));
        }

        return peak;
    }

    private static short[] Read(FileInfo wav)
    {
        using var played = new WaveFileReader(wav.FullName);
        var bytes = new byte[played.Length];
        played.ReadExactly(bytes);

        return MemoryMarshal.Cast<byte, short>(bytes).ToArray();
    }
}
