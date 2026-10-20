using MeetingTranscriber.Audio;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Testing;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The two shapes of audio this application sends are encoded, and not quietly sent as WAV.
/// </summary>
/// <remarks>
/// Here and not beside <c>FlacEncoderTests</c>, because these files come out of the audio engine's
/// own writer and that suite cannot reach it. A change to what that writer puts in a header — the
/// 18-byte <c>fmt </c> chunk it writes today — would otherwise send every meeting up as WAV again,
/// and the first sign would be a long meeting refused for taking too long to upload.
/// </remarks>
public sealed class FlacEncoderOnRecordingsTests : IDisposable
{
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory("flac-on-recordings-");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public void A_recording_this_application_made_is_encoded()
    {
        var spools = folder.CreateSubdirectory("spools");
        Fabricated.Spools(spools, seconds: 2);
        MeetingAudio.Materialise(spools);

        Encodes(MeetingAudio.In(spools)).ShouldBeTrue();
    }

    [Fact]
    public void A_file_mixed_down_to_one_track_is_encoded()
    {
        var stereo = ForeignWav.Steady(
            new FileInfo(Path.Combine(folder.FullName, "stereo.wav")), rate: 48_000, frames: 4800, 0.5f, 0.25f);
        var track = new FileInfo(Path.Combine(folder.FullName, "track.wav"));
        AudioFiles.MixDownToOneTrack(stereo, track);

        Encodes(track).ShouldBeTrue();
    }

    private static bool Encodes(FileInfo wav)
    {
        using var read = wav.OpenRead();
        using var into = new MemoryStream();
        return FlacEncoder.TryEncode(read, into, TestContext.Current.CancellationToken);
    }
}
