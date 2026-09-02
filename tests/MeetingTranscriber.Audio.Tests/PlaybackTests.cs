using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Time;

using NAudio.Wave;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The half of playing a recording back that has no device in it: where a seek somebody asked for
/// actually lands, and what the two channels are folded into before an endpoint hears them.
/// </summary>
/// <remarks>
/// The rest of <see cref="Playback"/> is one call to an endpoint each and there is none on a build
/// agent, which is the same reason nothing here opens a microphone. What a probe reaches instead
/// is a person pressing play on a packaged build, and that is recorded in the ISA like every other
/// answer this suite cannot give. The fold is on this side of that line because it is arithmetic
/// over samples: what it does to a pair of them is the same whether an endpoint is listening or
/// nobody is.
/// </remarks>
public class PlaybackTests
{
    private static readonly Duration Meeting = Duration.FromMilliseconds(1_360_000);

    [Fact]
    public void A_seek_inside_the_recording_lands_where_it_was_asked_for()
    {
        var wanted = Duration.FromMilliseconds(287_000);

        Playback.Within(wanted, Meeting).ShouldBe(wanted);
    }

    [Fact]
    public void Dragging_to_the_very_end_means_the_end_rather_than_a_refusal()
    {
        // A track is a strip of pixels and the last one is reachable by ordinary use, so this is
        // somebody dragging to the end and not a caller getting it wrong.
        Playback.Within(Meeting + Duration.FromMilliseconds(1), Meeting).ShouldBe(Meeting);
        Playback.Within(Duration.FromMilliseconds(long.MaxValue / 2), Meeting).ShouldBe(Meeting);
    }

    [Fact]
    public void There_is_no_seek_before_the_start_to_be_had()
    {
        // Not a clamp this has to do: a Duration refuses to be negative where it is made, so the
        // only way to ask for a seek before the start is to fail to build the value at all.
        Should.Throw<ArgumentOutOfRangeException>(
            () => Duration.Zero - Duration.FromMilliseconds(5_000));
    }

    [Fact]
    public void The_two_ends_of_a_recording_are_themselves_reachable()
    {
        Playback.Within(Duration.Zero, Meeting).ShouldBe(Duration.Zero);
        Playback.Within(Meeting, Meeting).ShouldBe(Meeting);
    }

    [Fact]
    public void A_recording_that_is_not_there_is_refused_naming_the_file()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.wav"));

        // The file before the device, which is what makes the two failures tell each other apart:
        // reversed, a corpus with a missing recording would report a sound problem.
        Should.Throw<AudioCaptureException>(() => Playback.Of(missing))
            .Message.ShouldContain(missing.FullName);
    }

    [Fact]
    public void A_file_that_is_not_a_wav_is_refused_naming_the_file_and_not_the_machine()
    {
        var notAWav = new FileInfo(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():n}.wav"));
        File.WriteAllText(notAWav.FullName, "this is not a RIFF header");

        try
        {
            // The same sentence a read of it gives, because it is the same door. A recording this
            // application cannot open is a fact about the corpus, and reporting it as a sound
            // problem would send somebody to their speakers over a file that is not audio.
            Should.Throw<AudioCaptureException>(() => Playback.Of(notAWav))
                .Message.ShouldContain(notAWav.FullName);
        }
        finally
        {
            notAWav.Delete();
        }
    }

    [Fact]
    public void The_two_sides_of_a_meeting_are_folded_half_each()
    {
        // One side at full scale and the other silent, which is the whole of what a fold can get
        // wrong: half is the average, one is the fold taking a side, and anything else is a
        // weighting nobody wrote down. Read through the provider Playback actually builds, so a
        // package that moved a default underneath it lands here rather than in somebody's earbud.
        var folded = Playback.BothSidesInBothEars(new Fabricated(channels: 2, [1f, 0f, 0f, 1f]));

        folded.WaveFormat.Channels.ShouldBe(1);

        var heard = new float[2];
        folded.Read(heard, 0, heard.Length).ShouldBe(2);
        heard.ShouldBe([0.5f, 0.5f]);
    }

    [Fact]
    public void A_recording_that_is_already_one_track_is_handed_over_untouched()
    {
        // Audio brought in from outside is one track by the time it is a meeting's, and a fold
        // over it would do nothing but stand between the file and the device.
        var one = new Fabricated(channels: 1, [0.25f, 0.5f]);

        Playback.BothSidesInBothEars(one).ShouldBeSameAs(one);
    }

    /// <summary>Samples that never came off a device, so the fold can be read without one.</summary>
    private sealed class Fabricated(int channels, float[] samples) : ISampleProvider
    {
        private int _read;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(16_000, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            var giving = Math.Min(count, samples.Length - _read);
            Array.Copy(samples, _read, buffer, offset, giving);
            _read += giving;

            return giving;
        }
    }
}
