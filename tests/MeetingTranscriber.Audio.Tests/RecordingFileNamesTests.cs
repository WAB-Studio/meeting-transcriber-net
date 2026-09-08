using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The tripwire between what a recording really writes and what the corpus can name.
/// </summary>
/// <remarks>
/// The division of labour with the reconciler's own suite: this one drives the real engine and pins
/// the set of names a recording leaves behind, so a name the engine gains and
/// <see cref="RecordingFiles"/> does not carry shows up here. The reconciler's tests build files
/// with those names and no bytes, because <c>corpus check</c> never opens one — what it says is
/// decided by the name alone. That gap is how <c>saving.mark</c> and then <c>capture.mark</c> each
/// arrived unnamed and were reported to somebody as recordings to recover.
/// </remarks>
public sealed class RecordingFileNamesTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public RecordingFileNamesTests() => folder.Create();

    [Fact]
    public void A_folder_a_recording_wrote_holds_only_files_the_corpus_can_name()
    {
        Fabricated.Spool(
            folder,
            AudioChannel.Loopback,
            StereoFloat,
            Fabricated.Packets(AudioChannel.Loopback, StereoFloat, 48_000, 0, 3, Fabricated.Bursts(0.5)));
        Fabricated.Spool(
            folder,
            AudioChannel.Microphone,
            CheapMicrophone,
            Fabricated.Packets(AudioChannel.Microphone, CheapMicrophone, 44_100, 0, 3, Fabricated.Bursts(0.5)));

        SpoolManifest.Write(folder, Card());
        SpoolChanges.Append(folder, Moving());

        BlockSpool.ToWav(BlockSpool.FileFor(folder, AudioChannel.Loopback));
        BlockSpool.ToWav(BlockSpool.FileFor(folder, AudioChannel.Microphone));

        MeetingAudio.Materialise(folder);

        using var capture = CaptureMark.Take(folder);
        using var reading = ReadingMark.Take(folder);
        using var saving = SavingMark.Take(folder);

        var written = folder.EnumerateFiles().Select(file => file.Name).ToArray();

        written.ShouldBe(
            [
                "manifest.json",
                "changes.jsonl",
                "loopback.blocks",
                "microphone.blocks",
                "loopback.wav",
                "microphone.wav",
                "audio.wav",
                "capture.mark",
                "reading.mark",
                "saving.mark",
            ],
            ignoreOrder: true);

        foreach (var name in written)
        {
            RecordingFiles.WhatIsInASpoolFolder(name).ShouldNotBe(SpoolFile.Unknown, name);
        }
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

    private static SpoolCard Card() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 7, 13, 47, 0, TimeSpan.Zero)),
        SourceProfile.Multichannel,
        CaptureMode.FullLoopback,
        [
            new SpooledSource(AudioChannel.Loopback, "everything this machine plays", null),
            new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.00000000}.jabra"),
        ]);

    private static SourceChanged Moving() => new(
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 7, 14, 2, 0, TimeSpan.Zero)),
        AudioChannel.Loopback,
        "everything this machine plays",
        "teams (pid 8124)");
}
