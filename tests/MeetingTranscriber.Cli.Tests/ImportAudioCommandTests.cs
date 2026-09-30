using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// Bringing audio in from a prompt: what a person types, what comes back, and what they cannot
/// type.
/// </summary>
/// <remarks>
/// The last of those is the point of this suite existing beside the intake's own. What the audio
/// is gets decided from the file, so the command line is where somebody would reach for a way to
/// say otherwise — and the test that it is not there is a test of the command line and of nothing
/// underneath it.
/// </remarks>
public sealed class ImportAudioCommandTests : IDisposable
{
    private const string StartedAt = "2026-05-04T14:00:00Z";

    private readonly DirectoryInfo elsewhere = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public ImportAudioCommandTests() => elsewhere.Create();

    /// <summary>
    /// ISC-83 and ISC-151, from a prompt: a stereo file nothing says this application recorded
    /// becomes a meeting, as one track, and the corpus counts it.
    /// </summary>
    [Fact]
    public void A_stereo_file_from_somewhere_else_becomes_a_meeting_as_one_track()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", Wav("call.wav", channels: 2).FullName,
            "--corpus", root,
            "--started-at", StartedAt,
            "--language", "es",
            "--title", "Kickoff");

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("profile").ShouldBe("diarize (mixed down to one track)");
        run.Value("length").ShouldBe("0:00:01");

        var meeting = run.Value("meeting");
        Guid.TryParse(meeting, out _).ShouldBeTrue(meeting);
        run.Value("audio").ShouldBe($"meetings/{meeting}/{MeetingAudio.FileName}");

        var audio = new FileInfo(Path.Combine(root, run.Value("audio")));
        audio.Exists.ShouldBeTrue(audio.FullName);
        AudioFiles.Read(audio).Format.Channels.ShouldBe(AudioFiles.OneTrack);

        var status = CommandLine.Of("status", "--corpus", root);
        status.Code.ShouldBe(Cli.Ok, status.Error);
        status.Value("meetings").ShouldBe("1 active");

        // And it is sound afterwards, hashes included: the audio the corpus records is the audio
        // on the disk, which is the one thing a mix down on the way in could have broken.
        var sound = CommandLine.Of("check", "--corpus", root, "--verify-contents");
        sound.Code.ShouldBe(Cli.Ok, sound.Error);
    }

    /// <summary>A file that is already one track goes in as it is.</summary>
    [Fact]
    public void A_single_track_goes_in_without_being_mixed_down()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", Wav("phone.wav", channels: 1).FullName,
            "--corpus", root,
            "--started-at", StartedAt);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Value("profile").ShouldBe("diarize");
    }

    /// <summary>
    /// There is no way to say what a channel carries, and this is the test that says so. A flag
    /// nothing reads is refused as one no command takes, so a build that grew one would fail here
    /// rather than start filing somebody's stereo file as two sources.
    /// </summary>
    [Fact]
    public void Nobody_is_asked_what_a_channel_carries()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", Wav("call.wav", channels: 2).FullName,
            "--corpus", root,
            "--started-at", StartedAt,
            "--profile", "multichannel");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--profile");

        var status = CommandLine.Of("status", "--corpus", root);
        status.Value("meetings").ShouldBe("none");
    }

    /// <summary>
    /// ISC-34, from a prompt. The same file handed over twice is the meeting that is already here,
    /// and the report says so rather than leaving somebody to notice they have two.
    /// </summary>
    [Fact]
    public void Bringing_the_same_audio_in_twice_is_one_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);
        var file = Wav("call.wav", channels: 2).FullName;

        var first = CommandLine.Of(
            "import-audio", file, "--corpus", root, "--started-at", StartedAt);
        var again = CommandLine.Of(
            "import-audio", file, "--corpus", root, "--started-at", StartedAt);

        first.Code.ShouldBe(Cli.Ok, first.Error);
        again.Code.ShouldBe(Cli.Ok, again.Error);
        again.Value("meeting").ShouldBe($"{first.Value("meeting")} (this audio was already here)");

        CommandLine.Of("status", "--corpus", root).Value("meetings").ShouldBe("1 active");
        again.Values("put back").ShouldBeEmpty();
    }

    /// <summary>
    /// And when the corpus had the row and had lost the file, the report names the file that came
    /// back instead of answering "already here" over a write nobody was told about.
    /// </summary>
    [Fact]
    public void Audio_that_puts_a_lost_file_back_says_which_file()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);
        var file = Wav("call.wav", channels: 2).FullName;

        var first = CommandLine.Of(
            "import-audio", file, "--corpus", root, "--started-at", StartedAt);
        first.Code.ShouldBe(Cli.Ok, first.Error);

        var audio = first.Value("audio");
        File.Delete(Path.Combine(root, audio));

        var again = CommandLine.Of(
            "import-audio", file, "--corpus", root, "--started-at", StartedAt);

        again.Code.ShouldBe(Cli.Ok, again.Error);
        again.Value("put back").ShouldBe(audio);

        // And the corpus is sound afterwards, which is what the file coming back was for.
        var sound = CommandLine.Of("check", "--corpus", root, "--verify-contents");
        sound.Code.ShouldBe(Cli.Ok, sound.Error);
    }

    /// <summary>
    /// A flag that carries nothing readable is a misuse and says which flag, rather than reaching
    /// the domain and coming back as an argument name.
    /// </summary>
    [Fact]
    public void A_language_that_says_nothing_is_a_misuse()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", Wav("call.wav", channels: 2).FullName,
            "--corpus", root,
            "--started-at", StartedAt,
            "--language", " ");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--language");
    }

    /// <summary>
    /// ISC-195.1, from a prompt. A folder this application wrote is brought in with nothing typed,
    /// and the meeting is the one the card describes.
    /// </summary>
    [Fact]
    public void A_folder_this_application_wrote_is_brought_in_with_nothing_typed()
    {
        using var first = new TemporaryCorpus();
        using var second = new TemporaryCorpus();
        var folder = WroteAFolder(first, "--started-at", StartedAt, "--language", "en", "--title", "Kickoff");

        CommandLine.Of("migrate", "--corpus", second.Root.FullName).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", MeetingAudio.In(folder).FullName, "--corpus", second.Root.FullName);

        run.Code.ShouldBe(Cli.Ok, run.Error);

        using var context = second.Open();
        var meeting = context.Meetings.Single();
        meeting.StartedAt.ShouldBe(UtcTimestamp.Parse("2026-05-04T14:00:00.000Z"));
        meeting.Language.ShouldBe("en");
        meeting.Title.ShouldBe("Kickoff");
    }

    /// <summary>
    /// ISC-195.2, from a prompt. A start the folder contradicts is a refusal naming both instants,
    /// and the corpus is left with no meeting.
    /// </summary>
    [Fact]
    public void A_start_the_folder_contradicts_is_refused_rather_than_stored()
    {
        using var first = new TemporaryCorpus();
        using var second = new TemporaryCorpus();
        var folder = WroteAFolder(first, "--started-at", StartedAt, "--language", "en");

        CommandLine.Of("migrate", "--corpus", second.Root.FullName).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of(
            "import-audio", MeetingAudio.In(folder).FullName,
            "--corpus", second.Root.FullName,
            "--started-at", "2026-06-01T09:00:00Z");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("2026-05-04T14:00:00.000Z");
        run.Error.ShouldContain("2026-06-01T09:00:00.000Z");
        CommandLine.Of("status", "--corpus", second.Root.FullName).Value("meetings").ShouldBe("none");
    }

    /// <summary>
    /// Audio nothing vouches for and no start typed is a refusal, not a misuse: it is known only
    /// after the folder has been read.
    /// </summary>
    [Fact]
    public void Audio_nothing_vouches_for_without_a_start_is_refused()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var run = CommandLine.Of("import-audio", Wav("call.wav", channels: 2).FullName, "--corpus", root);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("says when the meeting started");
        CommandLine.Of("status", "--corpus", root).Value("meetings").ShouldBe("none");
    }

    /// <summary>
    /// Audio this build cannot open is an answer rather than a crash, and it comes back under the
    /// exit code a script can act on.
    /// </summary>
    [Fact]
    public void Audio_this_build_cannot_open_is_refused_rather_than_thrown()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var file = new FileInfo(Path.Combine(elsewhere.FullName, "meeting.m4a"));
        File.WriteAllBytes(file.FullName, [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]);

        var run = CommandLine.Of(
            "import-audio", file.FullName, "--corpus", root, "--started-at", StartedAt);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("meeting.m4a");
    }

    /// <summary>
    /// A meeting folder this application wrote: a mono file brought into <paramref name="from"/>
    /// with <paramref name="flags"/>, its folder copied out beside no database.
    /// </summary>
    private DirectoryInfo WroteAFolder(TemporaryCorpus from, params string[] flags)
    {
        var root = from.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root).Code.ShouldBe(Cli.Ok);

        var made = CommandLine.Of(
            ["import-audio", Wav("phone.wav", channels: 1).FullName, "--corpus", root, .. flags]);
        made.Code.ShouldBe(Cli.Ok, made.Error);

        var filed = new DirectoryInfo(Path.Combine(root, "meetings", made.Value("meeting")));
        var copied = new DirectoryInfo(Path.Combine(elsewhere.FullName, "restored"));
        copied.Create();
        foreach (var file in filed.GetFiles())
        {
            file.CopyTo(Path.Combine(copied.FullName, file.Name), overwrite: true);
        }

        return copied;
    }

    public void Dispose()
    {
        try
        {
            elsewhere.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a green test over.
        }
    }

    /// <summary>
    /// A second of audio somebody else made, at a level on each channel of its own and at a rate no
    /// recording of this application runs at — which is what makes it somebody else's.
    /// </summary>
    private FileInfo Wav(string name, int channels)
    {
        var levels = Enumerable.Range(0, channels).Select(index => 0.2f + (0.1f * index)).ToArray();

        return ForeignWav.Steady(
            new FileInfo(Path.Combine(elsewhere.FullName, name)), 44_100, 44_100, levels);
    }
}
