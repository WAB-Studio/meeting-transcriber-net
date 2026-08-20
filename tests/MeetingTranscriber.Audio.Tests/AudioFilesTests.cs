using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Testing;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Audio this engine did not write: what it says it is, how much of it there really is, and what
/// is left of it once it has been made into the single track a diarized meeting is transcribed as.
/// </summary>
/// <remarks>
/// Every fixture here is written byte by byte by <see cref="ForeignWav"/> and never by the audio
/// engine's own writer. That is the difference between a suite about foreign audio and a suite
/// about this build reading its own dependency back: a writer produces well-formed output, so the
/// files worth testing — a chunk that declares more than the file holds, a length that is not
/// whole frames, a rate of nothing — cannot be made with one.
/// </remarks>
public sealed class AudioFilesTests : IDisposable
{
    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public AudioFilesTests() => folder.Create();

    /// <summary>A file with one track says so, and says how long it is off its own bytes.</summary>
    [Fact]
    public void A_single_track_says_what_it_is()
    {
        var file = Steady("phone.wav", 44_100, frames: 44_100, 0.5f);

        AudioFiles.FormatOf(file).ShouldBe(new StreamFormat(44_100, 1, 16, SampleEncoding.Pcm));

        var read = AudioFiles.Read(file);
        read.Frames.ShouldBe(44_100);
        read.Length.Milliseconds.ShouldBe(1_000);
        AudioFiles.IsOneTrack(read.Format).ShouldBeTrue();
    }

    /// <summary>And so does one with two, which is the case the whole card turns on.</summary>
    [Fact]
    public void A_pair_of_channels_says_what_it_is()
    {
        var file = Steady("call.wav", 48_000, frames: 24_000, 0.5f, 0.25f);

        var read = AudioFiles.Read(file);

        read.Format.ShouldBe(new StreamFormat(48_000, 2, 16, SampleEncoding.Pcm));
        AudioFiles.IsOneTrack(read.Format).ShouldBeFalse();
        read.Frames.ShouldBe(24_000);
        read.Length.Milliseconds.ShouldBe(500);
    }

    /// <summary>
    /// How long a file is comes off its bytes and never off the length its header declares. A copy
    /// that stopped half way declares an hour and holds a minute, and the declared number is what
    /// every citation into the meeting would be checked against.
    /// </summary>
    [Fact]
    public void How_long_a_file_is_is_counted_and_never_believed()
    {
        // A second of mono at 16 kHz is what this says it is, and 12.5 ms is what it holds.
        var file = ForeignWav.Truncated(
            At("cut-off.wav"), rate: 16_000, channels: 1, declared: 32_000, present: 400);

        var read = AudioFiles.Read(file);

        read.Frames.ShouldBe(200);
        read.Length.Milliseconds.ShouldBe(13);
    }

    /// <summary>
    /// A file that ends inside a frame costs that frame and not the meeting in it, whether it is
    /// being counted or poured through the mix down — and the two agree about how much there was.
    /// </summary>
    [Fact]
    public void A_file_that_ends_inside_a_frame_costs_that_frame_and_nothing_else()
    {
        // 402 bytes of stereo is 100 whole frames and half of one, under a header claiming 16000.
        var file = ForeignWav.Truncated(
            At("cut-off.wav"), rate: 16_000, channels: 2, declared: 16_000, present: 402);
        var into = At("one-track.wav");

        AudioFiles.Read(file).Frames.ShouldBe(100);
        AudioFiles.MixDownToOneTrack(file, into).Frames.ShouldBe(100);
        AudioFiles.Read(into).Frames.ShouldBe(100);
    }

    /// <summary>And so does a chunk length that was never a whole number of frames.</summary>
    [Fact]
    public void A_chunk_length_that_is_not_whole_frames_is_read_to_its_last_whole_one()
    {
        var file = ForeignWav.Truncated(
            At("odd.wav"), rate: 16_000, channels: 2, declared: 401, present: 401);

        AudioFiles.Read(file).Frames.ShouldBe(100);
        AudioFiles.MixDownToOneTrack(file, At("one-track.wav")).Frames.ShouldBe(100);
    }

    /// <summary>
    /// The mix down averages the channels rather than taking one of them, and it keeps the rate the
    /// file already ran at.
    /// </summary>
    /// <remarks>
    /// One side is silent in two of these, and that is the case that matters: a build that took
    /// channel 0 would pass on the level in the first row and file the second as silence — which
    /// is what a recorder with one microphone plugged into it produces.
    /// </remarks>
    [Theory]
    [InlineData(0.5f, 0.25f, 0.375f)]
    [InlineData(0f, 0.8f, 0.4f)]
    [InlineData(0.8f, 0f, 0.4f)]
    public void Two_channels_become_one_by_averaging_them(float left, float right, float expected)
    {
        var file = Steady("call.wav", 32_000, frames: 1_600, left, right);
        var into = At("one-track.wav");

        var mixed = AudioFiles.MixDownToOneTrack(file, into);

        mixed.Format.ShouldBe(new StreamFormat(32_000, 1, 16, SampleEncoding.Pcm));
        mixed.Frames.ShouldBe(1_600);
        mixed.Length.Milliseconds.ShouldBe(50);

        // Read back off the disk, so what is asserted is the file and not what the writer believed
        // it wrote.
        AudioFiles.Read(into).ShouldBe(mixed);
        TrackOf(into).ShouldAllBe(sample => Math.Abs(sample - expected) < 0.001f);
    }

    /// <summary>
    /// However many channels arrive, they average the same way. A room's microphone array is not a
    /// harder question than a pair, and refusing one would be refusing a meeting over a number.
    /// </summary>
    [Fact]
    public void More_than_two_channels_average_the_same_way()
    {
        var file = Steady("room.wav", 16_000, frames: 800, 0.6f, 0f, 0.6f, 0f, 0.6f, 0f);
        var into = At("one-track.wav");

        var mixed = AudioFiles.MixDownToOneTrack(file, into);

        mixed.Format.ShouldBe(new StreamFormat(16_000, 1, 16, SampleEncoding.Pcm));
        mixed.Frames.ShouldBe(800);
        TrackOf(into).ShouldAllBe(sample => Math.Abs(sample - 0.3f) < 0.001f);
    }

    /// <summary>A file that is already one track is poured through, and comes out itself.</summary>
    [Fact]
    public void One_track_mixed_down_is_the_same_track()
    {
        var file = Steady("phone.wav", 16_000, frames: 800, 0.5f);

        AudioFiles.MixDownToOneTrack(file, At("one-track.wav"))
            .Format.ShouldBe(new StreamFormat(16_000, 1, 16, SampleEncoding.Pcm));
        TrackOf(At("one-track.wav")).ShouldAllBe(sample => Math.Abs(sample - 0.5f) < 0.001f);
    }

    /// <summary>
    /// Only the exact shape a recording of this application comes out as reads as one. It is the
    /// origin test's hard half, so a rate or a width one step off is not this application's.
    /// </summary>
    [Theory]
    [InlineData(16_000, 2, true)]
    [InlineData(16_000, 1, false)]
    [InlineData(48_000, 2, false)]
    [InlineData(44_100, 2, false)]
    public void Only_this_applications_own_shape_reads_as_its_own(int rate, int channels, bool ours)
    {
        var levels = Enumerable.Repeat(0.4f, channels).ToArray();
        var file = Steady("maybe-ours.wav", rate, frames: 160, levels);

        AudioFiles.IsWhatThisApplicationRecords(AudioFiles.FormatOf(file)).ShouldBe(ours);
    }

    /// <summary>
    /// Something that is not a WAV is refused by name, and so is a file with nothing in it. There
    /// is no FFmpeg behind this application, so the alternative is not a wider reader — it is a
    /// meeting made out of a file nothing read.
    /// </summary>
    [Theory]
    [InlineData("meeting.m4a", new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 })]
    [InlineData("empty.wav", new byte[0])]
    [InlineData("half-a-header.wav", new byte[] { 0x52, 0x49, 0x46, 0x46 })]
    public void What_is_not_a_wav_is_refused_saying_so(string name, byte[] contents)
    {
        var file = At(name);
        File.WriteAllBytes(file.FullName, contents);

        var refused = Should.Throw<AudioCaptureException>(() => AudioFiles.FormatOf(file));

        refused.Message.ShouldContain(name);
        refused.Message.ShouldContain("WAV");
    }

    /// <summary>And so is a file that is not there at all.</summary>
    [Fact]
    public void Audio_that_is_not_there_is_refused_saying_where_it_was_looked_for()
    {
        Should.Throw<AudioCaptureException>(() => AudioFiles.FormatOf(At("gone.wav")))
            .Message.ShouldContain("gone.wav");
    }

    /// <summary>
    /// A header saying the audio runs at no rate is refused rather than turned into a length
    /// nothing can be worked out from.
    /// </summary>
    [Fact]
    public void A_file_that_runs_at_no_rate_is_refused()
    {
        var file = ForeignWav.AtNoRate(At("stopped-clock.wav"), channels: 1, bytes: 400);

        Should.Throw<AudioCaptureException>(() => AudioFiles.Read(file))
            .Message.ShouldContain("no rate");
    }

    /// <summary>
    /// A width this build cannot decode stops at the mix down rather than producing a track of
    /// something, and leaves nothing behind under the name it was going to be. What the file
    /// <em>is</em> still reads, because saying so needs no sample decoded.
    /// </summary>
    [Fact]
    public void A_width_this_build_cannot_read_stops_at_the_mix_down()
    {
        var file = ForeignWav.Wide(At("studio.wav"), rate: 48_000, channels: 2, frames: 100);
        var into = At("one-track.wav");

        AudioFiles.FormatOf(file).BitsPerSample.ShouldBe(24);

        Should.Throw<AudioCaptureException>(() => AudioFiles.MixDownToOneTrack(file, into))
            .Message.ShouldContain("24 bit");

        into.Refresh();
        into.Exists.ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a green test over.
        }
    }

    private FileInfo At(string name) => new(Path.Combine(folder.FullName, name));

    private FileInfo Steady(string name, int rate, int frames, params float[] levels) =>
        ForeignWav.Steady(At(name), rate, frames, levels);

    /// <summary>Every sample of a single-track file, full scale at one.</summary>
    private static float[] TrackOf(FileInfo wav)
    {
        using var reader = new WaveFileReader(wav.FullName);
        var format = StreamFormat.Of(reader.WaveFormat);
        format.Channels.ShouldBe(AudioFiles.OneTrack);

        var bytes = new byte[reader.Length];
        reader.ReadExactly(bytes);

        var mono = new float[bytes.Length / format.BytesPerSample];
        Samples.ToMono(bytes, format, mono);
        return mono;
    }
}
