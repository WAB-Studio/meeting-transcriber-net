using System.Text;

using MeetingTranscriber.Processing.Deepgram;

using Shouldly;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// ISC-232: what goes up decodes to exactly the samples recorded, each on its own channel.
/// </summary>
public sealed class FlacEncoderTests
{
    private const int Rate = 16_000;

    /// <summary>
    /// Two channels holding different things — speech-shaped noise on one, a tone, silence and the
    /// extremes on the other — over a length that is not a whole number of frames. Red the day a
    /// predictor, a Rice code or the last frame's size is wrong, and red the day one channel's bits
    /// land in the other's.
    /// </summary>
    [Fact]
    public void Two_channels_come_back_sample_for_sample_each_on_its_own_channel()
    {
        var length = (4096 * 3) + 1234;
        var loopback = Noise(length, seed: 1, amplitude: 9000);
        var microphone = new int[length];
        for (var at = 0; at < length; at++)
        {
            microphone[at] = at switch
            {
                < 4096 => 0,
                < 6000 => (int)(12000 * Math.Sin(at * 0.07)),
                < 6010 => short.MinValue,
                < 6020 => short.MaxValue,
                _ => at % 2 == 0 ? short.MaxValue : short.MinValue,
            };
        }

        var decoded = RoundTrip(Wav(fmtBytes: 16, loopback, microphone));

        decoded.SampleRate.ShouldBe(Rate);
        decoded.DeclaredSamples.ShouldBe(length);
        decoded.Channels.Length.ShouldBe(2);
        decoded.Channels[0].ShouldBe(loopback);
        decoded.Channels[1].ShouldBe(microphone);
    }

    /// <summary>
    /// A channel that heard nothing comes back as nothing, while the other was loud. Red the day the
    /// channels are coded together in a way that leaks one into the other.
    /// </summary>
    [Fact]
    public void A_silent_channel_beside_a_loud_one_stays_silent()
    {
        var loud = Noise(20_000, seed: 2, amplitude: 30000);
        var silent = new int[20_000];

        var decoded = RoundTrip(Wav(fmtBytes: 16, loud, silent));

        decoded.Channels[1].ShouldAllBe(sample => sample == 0);
        decoded.Channels[0].ShouldBe(loud);
    }

    /// <summary>
    /// This application's own recordings carry an 18-byte <c>fmt </c> chunk and a chunk before
    /// <c>data</c>, so the audio does not start at byte 44. Red the day the header is assumed.
    /// </summary>
    [Fact]
    public void The_audio_is_found_by_its_chunks_and_not_at_byte_44()
    {
        var one = Noise(5000, seed: 3, amplitude: 5000);
        var two = Noise(5000, seed: 4, amplitude: 5000);

        var decoded = RoundTrip(Wav(fmtBytes: 18, one, two, chunkBeforeData: true));

        decoded.Channels[0].ShouldBe(one);
        decoded.Channels[1].ShouldBe(two);
    }

    /// <summary>
    /// More frames than one byte of frame number holds, which is every meeting over 33 seconds.
    /// Red the day the frame number's multi-byte coding is wrong.
    /// </summary>
    [Fact]
    public void A_meeting_of_more_than_a_hundred_and_twenty_eight_frames_comes_back_whole()
    {
        var track = Noise((4096 * 130) + 7, seed: 8, amplitude: 4000);

        RoundTrip(Wav(fmtBytes: 16, track)).Channels.ShouldHaveSingleItem().ShouldBe(track);
    }

    /// <summary>A single track, which is what a diarized meeting is sent as.</summary>
    [Fact]
    public void One_track_comes_back_sample_for_sample()
    {
        var track = Noise(9000, seed: 5, amplitude: 20000);

        RoundTrip(Wav(fmtBytes: 16, track)).Channels.ShouldHaveSingleItem().ShouldBe(track);
    }

    /// <summary>
    /// A data chunk that declares more than the file holds ends where the file does, on the last
    /// whole frame — what a copy cut off half way leaves.
    /// </summary>
    [Fact]
    public void A_data_chunk_that_outruns_the_file_ends_on_its_last_whole_frame()
    {
        var one = Noise(5000, seed: 6, amplitude: 5000);
        var two = Noise(5000, seed: 7, amplitude: 5000);
        var wav = Wav(fmtBytes: 16, one, two);
        var cut = wav[..^3];
        BitConverter.GetBytes(int.MaxValue).CopyTo(cut, 40);

        var decoded = RoundTrip(cut);

        decoded.Channels[0].ShouldBe(one[..4999]);
        decoded.Channels[1].ShouldBe(two[..4999]);
    }

    /// <summary>
    /// A width this does not encode is left for the caller to send as it is, and nothing is
    /// written for it.
    /// </summary>
    [Fact]
    public void A_wav_that_is_not_16_bit_is_not_encoded()
    {
        var wav = Wav(fmtBytes: 16, new int[100]);
        BitConverter.GetBytes((short)24).CopyTo(wav, 34);
        using var into = new MemoryStream();

        FlacEncoder.TryEncode(new MemoryStream(wav), into, TestContext.Current.CancellationToken).ShouldBeFalse();

        into.Length.ShouldBe(0);
    }

    private static FlacReader.Decoded RoundTrip(byte[] wav)
    {
        using var into = new MemoryStream();
        FlacEncoder.TryEncode(new MemoryStream(wav), into, TestContext.Current.CancellationToken).ShouldBeTrue();
        return FlacReader.Read(into.ToArray());
    }

    /// <summary>Noise smoothed into something a predictor can bite on, the way speech is.</summary>
    private static int[] Noise(int length, int seed, int amplitude)
    {
        var random = new Random(seed);
        var samples = new int[length];
        double level = 0;
        for (var at = 0; at < length; at++)
        {
            level = (0.9 * level) + (0.1 * ((random.NextDouble() * 2) - 1));
            samples[at] = Math.Clamp((int)(level * amplitude * 3), short.MinValue, short.MaxValue);
        }

        return samples;
    }

    /// <summary>A 16-bit PCM WAV of the channels given, written byte by byte.</summary>
    private static byte[] Wav(int fmtBytes, params int[][] channels) =>
        Wav(fmtBytes, channels, chunkBeforeData: false);

    private static byte[] Wav(int fmtBytes, int[] one, int[] two, bool chunkBeforeData) =>
        Wav(fmtBytes, [one, two], chunkBeforeData);

    private static byte[] Wav(int fmtBytes, int[][] channels, bool chunkBeforeData)
    {
        var frames = channels[0].Length;
        var blockAlign = channels.Length * 2;
        using var stream = new MemoryStream();
        using (var wav = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            wav.Write("RIFF"u8);
            wav.Write(0);
            wav.Write("WAVE"u8);
            wav.Write("fmt "u8);
            wav.Write(fmtBytes);
            wav.Write((short)1);
            wav.Write((short)channels.Length);
            wav.Write(Rate);
            wav.Write(Rate * blockAlign);
            wav.Write((short)blockAlign);
            wav.Write((short)16);
            for (var extra = 16; extra < fmtBytes; extra++)
            {
                wav.Write((byte)0);
            }

            if (chunkBeforeData)
            {
                wav.Write("LIST"u8);
                wav.Write(5);
                wav.Write("abcde"u8);
                wav.Write((byte)0);
            }

            wav.Write("data"u8);
            wav.Write(frames * blockAlign);
            for (var at = 0; at < frames; at++)
            {
                foreach (var channel in channels)
                {
                    wav.Write((short)channel[at]);
                }
            }
        }

        return stream.ToArray();
    }
}
