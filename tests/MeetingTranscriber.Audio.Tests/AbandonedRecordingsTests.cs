using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a start after a crash finds waiting, and the three things somebody may do about it.
/// </summary>
/// <remarks>
/// The recordings are written the way a capture writes them and then left, which is what killing
/// the process leaves. No device is opened: what is being probed is the decision, and a decision
/// that needed hardware to test would be one nobody could hold to.
/// </remarks>
public sealed class AbandonedRecordingsTests : IDisposable
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public AbandonedRecordingsTests() => root.Create();

    /// <summary>
    /// ISC-123. Every one of them, each saying which meeting it is and what each source holds —
    /// which is what somebody decides on, and the reason the blocks themselves are not read here.
    /// </summary>
    [Fact]
    public void Every_recording_nobody_stopped_is_found_again_with_what_it_is_and_what_is_in_it()
    {
        var meeting = Recorded("2026-08-15-daily", both: true);
        Recorded("2026-08-15-uno", both: false);
        root.CreateSubdirectory("not-a-recording");

        var waiting = AbandonedRecordings.In(root);

        waiting.Select(recording => recording.Folder.Name)
            .ShouldBe(["2026-08-15-daily", "2026-08-15-uno"]);

        var daily = waiting[0];
        daily.Card.ShouldNotBeNull().MeetingId.ShouldBe(meeting);
        daily.Sources.Select(source => source.Channel)
            .ShouldBe([AudioChannel.Loopback, AudioChannel.Microphone]);
        daily.Sources.ShouldAllBe(source => source.Bytes > BlockSpool.HeaderBytes);

        waiting[1].Sources.Select(source => source.Channel).ShouldBe([AudioChannel.Loopback]);
    }

    /// <summary>
    /// A recording whose card never landed is still a recording: each spool declares its own
    /// format, so passing over it for want of a card would be the silent discard this whole path
    /// exists to make impossible.
    /// </summary>
    [Fact]
    public void A_recording_with_no_card_is_offered_all_the_same()
    {
        Recorded("nameless", both: true, card: false);

        var waiting = AbandonedRecordings.In(root);

        waiting.Count.ShouldBe(1);
        waiting[0].Card.ShouldBeNull();
        waiting[0].Sources.Count.ShouldBe(2);
    }

    [Fact]
    public void A_machine_that_has_never_recorded_has_nothing_waiting()
    {
        AbandonedRecordings.In(new DirectoryInfo(Path.Combine(root.FullName, "nowhere"))).ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-124, the first of the three. Keeping a recording reads it through and says what it is
    /// worth; what it must not do is change any of it, because the blocks already are the meeting.
    /// </summary>
    [Fact]
    public void A_recording_that_is_kept_says_what_survived_and_is_still_there_afterwards()
    {
        var written = Recorded("daily", both: true);
        var recording = AbandonedRecordings.At(Folder("daily"));

        var survived = recording.Keep();

        survived.Select(source => source.Channel)
            .ShouldBe([AudioChannel.Loopback, AudioChannel.Microphone]);
        survived[0].Format.ShouldBe(StereoFloat);
        survived[0].Blocks.ShouldBeGreaterThan(0);
        survived[0].Covers.Milliseconds.ShouldBeGreaterThan(0);
        survived.ShouldAllBe(source => source.Discarded == 0);

        SpoolManifest.Find(Folder("daily")).ShouldNotBeNull().MeetingId.ShouldBe(written);
        Folder("daily").EnumerateFiles("*.wav").ShouldBeEmpty();
        recording.Sources.ShouldAllBe(source => source.Blocks.Exists);
    }

    /// <summary>
    /// A recording the machine died in the middle of is worth every block that landed, and keeping
    /// it says what the last one cost rather than reporting the meeting as whole.
    /// </summary>
    [Fact]
    public void A_recording_that_was_cut_off_is_kept_saying_what_the_cut_cost()
    {
        Recorded("daily", both: true);
        CutOffMidBlock(BlockSpool.FileFor(Folder("daily"), AudioChannel.Microphone));

        var survived = AbandonedRecordings.At(Folder("daily")).Keep();

        survived[0].Discarded.ShouldBe(0);
        survived[1].Discarded.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// ISC-124, the second. Taking the audio out puts a file of each source where somebody asked
    /// for it, and leaves the recording where it is.
    /// </summary>
    [Fact]
    public void A_recording_whose_audio_is_taken_out_lands_where_it_was_asked_for_and_stays_put()
    {
        Recorded("daily", both: true);
        var into = new DirectoryInfo(Path.Combine(root.FullName, "somewhere else"));

        var exported = AbandonedRecordings.At(Folder("daily")).Export(into);

        exported.Select(source => source.Wav.Name).ShouldBe(["loopback.wav", "microphone.wav"]);
        exported.ShouldAllBe(source => source.Wav.Exists && source.Blocks > 0);
        into.EnumerateFiles("*.blocks").ShouldBeEmpty();

        Folder("daily").EnumerateFiles("*.blocks").Count().ShouldBe(2);
        SpoolManifest.Find(Folder("daily")).ShouldNotBeNull();
    }

    /// <summary>
    /// Half of a recording somebody asked for is worse than being refused, so every destination is
    /// looked at before the first one is written.
    /// </summary>
    [Fact]
    public void Audio_taken_out_is_never_written_over_audio_already_there()
    {
        Recorded("daily", both: true);
        var into = root.CreateSubdirectory("taken out");
        File.WriteAllBytes(Path.Combine(into.FullName, "microphone.wav"), [1, 2, 3]);

        Should.Throw<AudioCaptureException>(() => AbandonedRecordings.At(Folder("daily")).Export(into))
            .Message.ShouldContain("microphone.wav");

        into.EnumerateFiles("loopback.wav").ShouldBeEmpty();
    }

    /// <summary>ISC-124, the third, and the only one of them that takes anything away.</summary>
    [Fact]
    public void A_recording_that_is_thrown_away_is_gone_with_everything_in_it()
    {
        Recorded("daily", both: true);

        AbandonedRecordings.At(Folder("daily")).Discard();

        Folder("daily").Exists.ShouldBeFalse();
        AbandonedRecordings.In(root).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting still being recorded is not a recording nobody stopped. Throwing one away would
    /// be the worst of the three outcomes landing on the one recording somebody is in the middle
    /// of, so it is refused where the refusal can still name what is happening.
    /// </summary>
    [Fact]
    public void A_recording_still_being_written_is_not_thrown_away()
    {
        Recorded("daily", both: true);
        var recording = AbandonedRecordings.At(Folder("daily"));

        // The handle a capture holds on its own spool: writing it, and letting nothing else write.
        using var writing = new FileStream(
            BlockSpool.FileFor(Folder("daily"), AudioChannel.Loopback).FullName,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read);

        Should.Throw<AudioCaptureException>(recording.Discard).Message.ShouldContain("still running");
        Folder("daily").Exists.ShouldBeTrue();
    }

    /// <summary>
    /// What somebody typed is then a folder rather than a recording, and acting on it as one is
    /// how the wrong directory gets thrown away.
    /// </summary>
    [Fact]
    public void A_folder_holding_no_recording_is_refused_rather_than_decided_about()
    {
        var empty = root.CreateSubdirectory("empty");

        Should.Throw<AudioCaptureException>(() => AbandonedRecordings.At(empty))
            .Message.ShouldContain("no spool");
        Should.Throw<AudioCaptureException>(() => AbandonedRecordings.At(Folder("nowhere")))
            .Message.ShouldContain("no folder");
    }

    /// <summary>
    /// ISC-125. A recording's folder is removed in one place, reachable only from a choice about
    /// one recording. The rule is not held by the places that obey it today — it is held by a
    /// fifth one costing a red test on the day it is written.
    /// </summary>
    [Fact]
    public void Nothing_but_a_decision_about_one_recording_removes_a_folder()
    {
        var allowed = Path.Combine("MeetingTranscriber.Audio", "AbandonedRecordings.cs");

        var offenders = Sources()
            .Where(file => File.ReadAllText(file.FullName) is var text
                && (text.Contains("Directory.Delete", StringComparison.Ordinal)
                    || text.Contains("Delete(recursive", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(Tree().FullName, file.FullName))
            .Where(path => !path.EndsWith(allowed, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "These remove a folder, and a recording's folder is one. Throwing a recording away is "
            + "somebody's decision about one recording, and there is one place it happens.");
    }

    /// <summary>
    /// The same rule one level down: inside the audio engine, a file is only ever removed by the
    /// thing that just made it and found it could not go on — never a spool holding blocks.
    /// </summary>
    [Fact]
    public void Nothing_in_the_audio_engine_removes_a_file_it_did_not_just_create()
    {
        string[] allowed =
        [
            // The spool of a source whose stream would not open: an empty file standing exactly
            // where the next attempt wants to write.
            "SpoolWriter.cs",

            // The same, for a source a session gave up on before the recording existed.
            "CaptureSource.cs",

            // A card whose write was cut off, which would otherwise refuse the folder forever for
            // something that never said anything.
            "SpoolManifest.cs",

            // The one decision that removes a recording.
            "AbandonedRecordings.cs",
        ];

        var offenders = Sources()
            .Where(file => file.Directory?.Name == "MeetingTranscriber.Audio")
            .Where(file => File.ReadAllText(file.FullName).Contains("Delete(", StringComparison.Ordinal))
            .Select(file => file.Name)
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "These remove a file inside the audio engine. A spool may be the only copy of a "
            + "meeting that happened, so what removes one is named here with the reason.");
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

    /// <summary>Every source file of the product, which is what the two sweeps are over.</summary>
    private static IEnumerable<FileInfo> Sources() => Tree()
        .EnumerateFiles("*.cs", SearchOption.AllDirectories)
        .Where(file => !file.FullName.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// The product's source tree, from where this file was compiled rather than from the working
    /// directory.
    /// </summary>
    private static DirectoryInfo Tree() => new(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFile())!, "..", "..", "src")));

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    /// <summary>Cuts the last block short, the way killing a process mid write cuts one short.</summary>
    private static void CutOffMidBlock(FileInfo blocks)
    {
        using var stream = blocks.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(stream.Length - 16);
    }

    private DirectoryInfo Folder(string name) => new(Path.Combine(root.FullName, name));

    /// <summary>A recording left exactly as killing the process during one leaves it.</summary>
    private Guid Recorded(string name, bool both, bool card = true)
    {
        var folder = Folder(name);
        folder.Create();
        var meeting = Guid.NewGuid();

        Spool(folder, AudioChannel.Loopback, StereoFloat);
        if (both)
        {
            Spool(folder, AudioChannel.Microphone, CheapMicrophone);
        }

        if (card)
        {
            SpoolManifest.Write(folder, new SpoolCard(
                meeting,
                Guid.NewGuid(),
                UtcTimestamp.Parse("2026-08-15T09:41:07.250Z"),
                SourceProfile.Multichannel,
                [
                    new SpooledSource(AudioChannel.Loopback, "Speakers (Realtek)", "{0.0.0.0}.speakers"),
                    new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.0}.jabra"),
                ],
                null));
        }

        return meeting;
    }

    private void Spool(DirectoryInfo folder, AudioChannel channel, StreamFormat format)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, format);
        foreach (var packet in Fabricated.Packets(
            channel, format, format.SampleRate, 0, 0.5, Fabricated.Bursts(0.25)))
        {
            writer.Write(packet);
        }
    }
}
