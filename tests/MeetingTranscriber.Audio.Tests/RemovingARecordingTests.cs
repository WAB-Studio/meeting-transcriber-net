using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// <see cref="UnfinishedRecordings.RemoveNowTheCorpusHoldsIt"/> — the second way this engine
/// removes a recording, for a caller that has already decided rather than one still asking.
/// </summary>
/// <remarks>
/// A file of its own rather than facts added to <see cref="UnfinishedRecordingsTests"/>, which is
/// <c>jobs</c>'s this batch — see <c>UnfinishedRecordings.Remove</c>'s own remarks for who the
/// second caller is and what stands in front of it.
/// </remarks>
public sealed class RemovingARecordingTests : IDisposable
{
    private static readonly StreamFormat Format = new(48_000, 2, 32, SampleEncoding.IeeeFloat);

    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public RemovingARecordingTests() => root.Create();

    /// <summary>
    /// The whole difference from every other way in: this asks nothing about whether the meeting
    /// is still being recorded, because a caller that has already filed the corpus's own copy of
    /// it is not asking — it is telling.
    /// </summary>
    /// <remarks>
    /// Built by handing the method a value that says the meeting is still being recorded while the
    /// files themselves are held by nothing, rather than by holding a spool open: a capture that
    /// really is still writing would also refuse <see cref="UnfinishedRecordings.EnsureRemovable"/>
    /// on the file itself, which proves nothing about which question was asked. What is proved here
    /// is that this method never reads <see cref="UnfinishedRecording.Running"/> at all — only
    /// <see cref="EnsureRemovable"/>'s own live checks, over the folder and its marks, which is
    /// what a finish's own call is never wrong about, because it calls
    /// <see cref="UnfinishedRecordings.At"/> fresh once its own write is over.
    /// </remarks>
    [Fact]
    public void A_recording_the_corpus_holds_is_removed_without_being_asked_whether_it_is_decided()
    {
        Recorded("daily");
        var actual = UnfinishedRecordings.At(Folder("daily"));
        var claimsStillRecording = actual with { Running = true };

        Should.Throw<AudioCaptureException>(claimsStillRecording.EnsureThereIsSomethingToDecide);

        UnfinishedRecordings.RemoveNowTheCorpusHoldsIt(claimsStillRecording);

        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// <see cref="UnfinishedRecordings.EnsureRemovable"/> still stands in front of this door: a
    /// save reading these blocks into the meeting, or something else reading them, is not
    /// something this removes out from under.
    /// </summary>
    [Fact]
    public void A_recording_something_is_holding_is_not_removed()
    {
        Recorded("daily");
        var recording = UnfinishedRecordings.At(Folder("daily"));

        using (SavingMark.Take(Folder("daily")))
        {
            var refused = Should.Throw<AudioCaptureException>(
                () => UnfinishedRecordings.RemoveNowTheCorpusHoldsIt(recording));
            refused.Message.ShouldContain("A save of the recording");
        }

        Folder("daily").Exists.ShouldBeTrue();

        using (ReadingMark.Take(Folder("daily")))
        {
            var refused = Should.Throw<AudioCaptureException>(
                () => UnfinishedRecordings.RemoveNowTheCorpusHoldsIt(recording));
            refused.Message.ShouldContain("is reading the recording");
        }

        Folder("daily").Exists.ShouldBeTrue();
    }

    public void Dispose()
    {
        try
        {
            root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private DirectoryInfo Folder(string name) => new(Path.Combine(root.FullName, name));

    /// <summary>A recording left exactly as stopping — and not killing the process — leaves it.</summary>
    private void Recorded(string name)
    {
        var folder = Folder(name);
        folder.Create();
        var meeting = Guid.NewGuid();

        foreach (var channel in new[] { AudioChannel.Loopback, AudioChannel.Microphone })
        {
            using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, Format);
            foreach (var packet in Fabricated.Packets(
                channel, Format, Format.SampleRate, 0, 0.5, Fabricated.Bursts(0.25)))
            {
                writer.Write(packet);
            }
        }

        SpoolManifest.Write(folder, new SpoolCard(
            meeting,
            Guid.NewGuid(),
            UtcTimestamp.Parse("2026-08-15T09:41:07.250Z"),
            SourceProfile.Multichannel,
            CaptureMode.WholeMachine,
            [
                new SpooledSource(AudioChannel.Loopback, "everything this machine plays", null),
                new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.0}.jabra"),
            ]));
    }
}
