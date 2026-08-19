using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Recording;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// The start the application performs after a crash, driven from a prompt: what is waiting in a
/// corpus, and what each of the three choices does about one of them.
/// </summary>
/// <remarks>
/// <para>
/// The sibling suite drives <c>recordings</c> and <c>recover</c>, which are the audio engine's own
/// view — a folder, its blocks, and a file beside them. This one is the product's: a corpus, the
/// meetings the folders belong to, and recovering meaning the meeting rather than a wav.
/// </para>
/// <para>
/// Nothing here opens a device. The corpus and the crash are fabricated through the same calls a
/// recording makes, and every assertion is read back off what the command printed.
/// </para>
/// </remarks>
public sealed class CorpusRecoveryCommandTests : IDisposable
{
    private static readonly StreamFormat Format = new(48_000, 1, 16, SampleEncoding.Pcm);

    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp startedAt = UtcTimestamp.Parse("2026-08-18T09:41:07.250Z");

    /// <summary>
    /// The card's own words: listed with their length and what survived. A count of sources and a
    /// size in megabytes are not either of those — three hours caught sparsely and three minutes
    /// caught whole would read the same.
    /// </summary>
    [Fact]
    public void What_is_waiting_says_which_meeting_it_is_how_long_it_is_and_what_survived()
    {
        var meeting = Killed();

        var run = CommandLine.Of("recovery", "--corpus", Root);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("meeting").ShouldBe(meeting.ToString());
        run.Value("started").ShouldBe("2026-08-18T09:41:07.250Z");
        run.Value("length").ShouldBe("0:00:00");
        run.Value("others survived").ShouldStartWith("10 blocks, 0:00:00, 0 ms lost");
        run.Value("others survived").ShouldNotContain("cut off");

        // The microphone is the one the process was killed inside, and what that cost is the last
        // block rather than the meeting.
        run.Value("me survived").ShouldStartWith("9 blocks,");
        run.Value("me survived").ShouldContain("bytes cut off the end");
        run.Value("choices").ShouldBe("keep, export or discard");
    }

    [Fact]
    public void A_corpus_with_nothing_waiting_says_so_rather_than_saying_nothing()
    {
        Migrated();

        var run = CommandLine.Of("recovery", "--corpus", Root);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("waiting").ShouldBe("none");
    }

    /// <summary>
    /// ISC-79, at the surface a person meets it. Keeping the recording is the meeting: filed,
    /// hashed and as long as it turned out to be, not a file left beside the blocks.
    /// </summary>
    [Fact]
    public void Keeping_a_recording_leaves_a_meeting_with_its_audio_in_the_corpus()
    {
        var meeting = Killed();

        var run = CommandLine.Of("recovery", "--corpus", Root, "--meeting", meeting.ToString(), "--keep");

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("meeting").ShouldBe(meeting.ToString());
        run.Value("audio").ShouldBe($"meetings/{meeting}/{MeetingAudio.FileName}");
        run.Value("sha256").Length.ShouldBe(64);

        var audio = CorpusFiles.Locate(corpus.Root, run.Value("audio"));
        audio.Exists.ShouldBeTrue(audio.FullName);
        CorpusFiles.Sha256Of(audio).ShouldBe(run.Value("sha256"));

        // And it is not waiting any more, which is the difference between a meeting made and a
        // file written: the next start offers nothing.
        CommandLine.Of("recovery", "--corpus", Root).Value("waiting").ShouldBe("none");
    }

    /// <summary>
    /// The listing is a listing. It reads the card and the size of each spool and touches nothing,
    /// so a start after a crash costs the same whether somebody decides anything or not.
    /// </summary>
    [Fact]
    public void Listing_what_is_waiting_decides_nothing_and_removes_nothing()
    {
        var meeting = Killed();
        var folder = CorpusFiles.SpoolFolderFor(corpus.Root, meeting);
        var before = folder.GetFiles().Select(file => $"{file.Name} {file.Length}").Order().ToArray();

        CommandLine.Of("recovery", "--corpus", Root).Code.ShouldBe(Cli.Ok);
        CommandLine.Of("recovery", "--corpus", Root).Code.ShouldBe(Cli.Ok);

        folder.Refresh();
        folder.GetFiles().Select(file => $"{file.Name} {file.Length}").Order().ShouldBe(before);
        MeetingAudio.In(folder).Exists.ShouldBeFalse();
    }

    [Fact]
    public void Taking_the_audio_out_leaves_the_recording_waiting()
    {
        var meeting = Killed();
        var into = Path.Combine(Root, "taken out");

        var run = CommandLine.Of(
            "recovery", "--corpus", Root, "--meeting", meeting.ToString(), "--export", into);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        new FileInfo(Path.Combine(into, "loopback.wav")).Exists.ShouldBeTrue();
        new FileInfo(Path.Combine(into, "microphone.wav")).Exists.ShouldBeTrue();

        // Taking it out is not deciding about it: it is still there for somebody to keep.
        CommandLine.Of("recovery", "--corpus", Root).Value("meeting").ShouldBe(meeting.ToString());
    }

    [Fact]
    public void Throwing_one_away_removes_that_recording_and_leaves_the_others()
    {
        var thrown = Killed();
        var kept = Killed();

        var run = CommandLine.Of(
            "recovery", "--corpus", Root, "--meeting", thrown.ToString(), "--discard");

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("thrown away").ShouldBe(CorpusFiles.SpoolFolderFor(corpus.Root, thrown).FullName);
        CorpusFiles.SpoolFolderFor(corpus.Root, thrown).Exists.ShouldBeFalse();
        CommandLine.Of("recovery", "--corpus", Root).Value("meeting").ShouldBe(kept.ToString());
    }

    /// <summary>
    /// Nothing happens to a recording until somebody says which of the three it is. A command that
    /// named one and decided nothing is a line half typed, and one of the three throws it away.
    /// </summary>
    [Fact]
    public void Naming_a_recording_and_deciding_nothing_is_a_misuse()
    {
        var meeting = Killed();

        var run = CommandLine.Of("recovery", "--corpus", Root, "--meeting", meeting.ToString());

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--discard");
        CorpusFiles.SpoolFolderFor(corpus.Root, meeting).Exists.ShouldBeTrue();
    }

    [Fact]
    public void Deciding_two_things_about_a_recording_is_a_misuse()
    {
        var meeting = Killed();

        var run = CommandLine.Of(
            "recovery", "--corpus", Root, "--meeting", meeting.ToString(), "--keep", "--discard");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("only one");
        CorpusFiles.SpoolFolderFor(corpus.Root, meeting).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A decision with nothing to apply it to. Answering it against whatever happens to be waiting
    /// would throw away a recording nobody named.
    /// </summary>
    [Fact]
    public void Deciding_without_saying_which_recording_is_a_misuse()
    {
        var meeting = Killed();

        var run = CommandLine.Of("recovery", "--corpus", Root, "--discard");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--meeting");
        CorpusFiles.SpoolFolderFor(corpus.Root, meeting).Exists.ShouldBeTrue();
    }

    [Fact]
    public void A_meeting_nothing_is_waiting_for_is_refused()
    {
        Migrated();

        var run = CommandLine.Of(
            "recovery", "--corpus", Root, "--meeting", Guid.NewGuid().ToString(), "--keep");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("waiting");
    }

    [Fact]
    public void Something_that_is_not_a_meeting_id_is_a_misuse()
    {
        Migrated();

        var run = CommandLine.Of("recovery", "--corpus", Root, "--meeting", "yesterday", "--keep");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("yesterday");
    }

    [Fact]
    public void A_flag_this_command_does_not_read_is_a_misuse()
    {
        Migrated();

        var run = CommandLine.Of("recovery", "--corpus", Root, "--spool", Root);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--spool");
    }

    public void Dispose() => corpus.Dispose();

    private string Root => corpus.Root.FullName;

    private void Migrated() => corpus.OpenMigrated().Dispose();

    /// <summary>
    /// A meeting recorded up to the moment the machine died: the row, the folder, the card, the
    /// row describing the run, whole blocks, and a last one cut off inside itself.
    /// </summary>
    private Guid Killed()
    {
        using var context = corpus.OpenMigrated();

        var prepared = MeetingRecordings.Open(context, "es", startedAt);
        var card = new SpoolCard(
            prepared.MeetingId,
            Guid.NewGuid(),
            startedAt,
            CapturedAudio.Profile,
            [
                new SpooledSource(AudioChannel.Loopback, "Speakers (Realtek)", "{0.0.0.0}.speakers"),
                new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.0}.jabra"),
            ]);

        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        Spool(prepared.Spool, AudioChannel.Loopback);
        Spool(prepared.Spool, AudioChannel.Microphone);
        CutOffMidBlock(BlockSpool.FileFor(prepared.Spool, AudioChannel.Microphone));

        return prepared.MeetingId;
    }

    private static void Spool(DirectoryInfo folder, AudioChannel channel)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, Format);
        for (var block = 0; block < 10; block++)
        {
            writer.Write(new CapturePacket(
                channel,
                block * 480L,
                MonotonicInstant.FromMilliseconds(block * 10d),
                new byte[480 * Format.BytesPerSample]));
        }
    }

    /// <summary>Takes the tail off the way a process being killed mid write takes it off.</summary>
    private static void CutOffMidBlock(FileInfo blocks)
    {
        using var stream = blocks.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(blocks.Length - 32);
    }
}
