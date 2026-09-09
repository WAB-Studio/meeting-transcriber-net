using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// The spools this suite's recordings are made of, and the one format they are written at.
/// </summary>
/// <remarks>
/// Here rather than in each test class because both recovery prompts write the same folder and a
/// second copy of a fixture is how two suites come to write it differently. <c>Fabricated</c> is
/// the same fixture one project over and is <c>internal</c> to the audio suite, which is why this
/// exists rather than being taken from there.
/// </remarks>
internal static class Spools
{
    /// <summary>What every device in this suite hands over, unless a fixture says it changed.</summary>
    internal static readonly StreamFormat Format = new(48_000, 1, 16, SampleEncoding.Pcm);

    /// <summary>
    /// A source after the device feeding it was replaced by one handing over another format: one
    /// spool, two stretches, and no single file it can be poured into.
    /// </summary>
    /// <remarks>
    /// Only ever asked for channel 1. A channel 0 spool cannot hold two formats — both ways of
    /// opening it carry a sequence on, so <c>CaptureSource.ListenTo</c> opens no stretch there and
    /// refuses a device handing over another format outright.
    /// </remarks>
    internal static void ThatChangedFormat(DirectoryInfo folder, AudioChannel channel)
    {
        var tookOver = new StreamFormat(44_100, 1, 16, SampleEncoding.Pcm);

        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, Format);
        for (var block = 0; block < 10; block++)
        {
            writer.Write(new CapturePacket(
                channel,
                block * 480L,
                MonotonicInstant.FromMilliseconds(block * 10d),
                new byte[480 * Format.BytesPerSample]));
        }

        // The seam, said on the first packet of the second stretch and on no other — which is what
        // a device change is, and what makes the two halves two formats rather than one.
        for (var block = 0; block < 10; block++)
        {
            writer.Write(new CapturePacket(
                channel,
                block * 480L,
                MonotonicInstant.FromMilliseconds(100 + (block * 10d)),
                new byte[480 * tookOver.BytesPerSample],
                Opening: block == 0 ? tookOver : null));
        }
    }
}
