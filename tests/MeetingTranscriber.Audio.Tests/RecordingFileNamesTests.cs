using System.Reflection;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The tripwire between what a recording really writes and what the corpus can name.
/// </summary>
/// <remarks>
/// <para>
/// Two tests and two different holds. The first reads every name the engine <em>declares</em>
/// straight off the assembly, so a name added to it and not to <see cref="RecordingFiles"/> fails
/// here whether or not anybody remembered to call the thing that writes it — which is the gap
/// <c>saving.mark</c> and then <c>capture.mark</c> each came through, a day apart. The second runs a
/// real recording and reads the folder back, which is the only thing that can catch a name no
/// constant declares at all: a temporary a write forgot to move, an index something started
/// keeping.
/// </para>
/// <para>
/// The reconciler's own tests build files with these names and no bytes, because <c>corpus
/// check</c> never opens one — what it says is decided by the name alone, and the names are what
/// this suite is for.
/// </para>
/// </remarks>
public sealed class RecordingFileNamesTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public RecordingFileNamesTests() => folder.Create();

    /// <summary>
    /// Every file name the audio engine publishes as a constant, asked of the corpus's own answer.
    /// Read off the assembly rather than listed, because a list is the thing that goes stale: the
    /// two defects this suite exists for were both a name added to the engine and to nothing else.
    /// </summary>
    [Fact]
    public void Every_name_the_engine_declares_is_one_the_corpus_can_place()
    {
        var declared = typeof(BlockSpool).Assembly.GetTypes()
            .Where(type => type.IsPublic)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field is { IsLiteral: true, FieldType: { } kind } && kind == typeof(string))
            .Where(field => field.Name is "FileName" or "Extension")
            .Select(field => (Owner: $"{field.DeclaringType!.Name}.{field.Name}", Name: (string)field.GetRawConstantValue()!))
            .ToArray();

        // A guard on the guard: a rename of either field would silently empty the list above and
        // leave this test green over nothing.
        declared.Length.ShouldBeGreaterThanOrEqualTo(7);

        foreach (var (owner, name) in declared)
        {
            // An extension is not a file name, so it is asked as one a recording would really write.
            var asWritten = name.StartsWith('.') ? $"loopback{name}" : name;
            RecordingFiles.WhatIsInASpoolFolder(asWritten).ShouldNotBe(SpoolFile.Unknown, owner);
        }
    }

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
