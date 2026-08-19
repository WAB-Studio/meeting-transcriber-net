using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// The start that follows a crash, without a window over it: what is waiting, and what somebody
/// decides happens to it.
/// </summary>
/// <remarks>
/// This one does record, in the sense that matters here: no device is opened, but the blocks are
/// written the way a capture writes them and one of them is cut off the way a killed process cuts
/// one off. That is the whole path a person's recovery goes through, and it is deterministic —
/// unlike the run on real hardware, which is a probe somebody watches, recorded in the ISA.
/// </remarks>
public sealed class RecoveryCommandTests : IDisposable
{
    private static readonly StreamFormat Format = new(48_000, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public RecoveryCommandTests() => root.Create();

    /// <summary>ISC-123, through the surface a person actually meets it at.</summary>
    [Fact]
    public void What_is_waiting_says_which_meeting_it_is_and_what_each_source_holds()
    {
        var meeting = Recorded("daily", both: true);

        var run = CommandLine.Of("recordings", "--spool", root.FullName);

        run.Code.ShouldBe(Cli.Ok);
        run.Value("recording").ShouldBe("daily");
        run.Value("meeting").ShouldBe(meeting.ToString());
        run.Value("started").ShouldBe("2026-08-15T09:41:07.250Z");
        run.Value("profile").ShouldBe("multichannel");
        run.Value("ch0 heard").ShouldBe("Speakers (Realtek)");
        run.Value("ch1 heard").ShouldBe("Jabra Evolve 65");
        run.Value("ch0 holds").ShouldStartWith("loopback.blocks,");
        run.Value("ch1 holds").ShouldStartWith("microphone.blocks,");
    }

    [Fact]
    public void A_machine_with_nothing_waiting_says_so_rather_than_saying_nothing()
    {
        var run = CommandLine.Of("recordings", "--spool", root.FullName);

        run.Code.ShouldBe(Cli.Ok);
        run.Value("waiting").ShouldBe("none");
    }

    /// <summary>
    /// ISC-124. Nothing happens to a recording until somebody says which of the three it is, so a
    /// command with no decision on it is a misuse rather than a default.
    /// </summary>
    [Fact]
    public void Deciding_nothing_about_a_recording_is_a_misuse()
    {
        Recorded("daily", both: true);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--discard");
        BlockSpool.FileFor(Folder("daily"), AudioChannel.Loopback).Exists.ShouldBeTrue();
    }

    /// <summary>
    /// Two decisions is no decision: which of them ran would be decided by the order they are read
    /// in, and one of the three throws the recording away.
    /// </summary>
    [Fact]
    public void Deciding_two_things_about_a_recording_is_a_misuse()
    {
        Recorded("daily", both: true);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--keep", "--discard");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("only one");
        Folder("daily").Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A recording the machine died in the middle of is worth every block that landed, and keeping
    /// it says what the last one cost — and leaves the meeting those blocks are, which is the whole
    /// of what a person came to the folder for.
    /// </summary>
    [Fact]
    public void A_recording_that_was_cut_off_is_kept_back_to_its_last_whole_block()
    {
        Recorded("daily", both: true);
        CutOffMidBlock(AudioChannel.Microphone);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--keep");

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch0 kept").ShouldStartWith("10 blocks");
        run.Value("ch0 kept").ShouldNotContain("discarded");
        run.Value("ch1 kept").ShouldStartWith("9 blocks");
        run.Value("ch1 kept").ShouldContain("discarded");
        run.Value("ch1 format").ShouldBe(Format.ToString());
        run.Value("recording").ShouldStartWith($"{MeetingAudio.FileName}, ");

        // The blocks are all still there, and the one file beside them is the recording itself:
        // keeping a recording is not the moment to write a diagnostic per source.
        Folder("daily").EnumerateFiles("*.blocks").Count().ShouldBe(2);
        Folder("daily").EnumerateFiles("*.wav").Select(wav => wav.Name)
            .ShouldBe([MeetingAudio.FileName]);
    }

    /// <summary>
    /// A recording with one source is still a recording somebody wants, and keeping it must not
    /// decide anything about the source that is missing — least of all by calling half a meeting
    /// the meeting.
    /// </summary>
    [Fact]
    public void One_source_on_its_own_is_kept_and_left_where_it_is()
    {
        Recorded("uno", both: false);

        var run = CommandLine.Of("recover", "--in", Folder("uno").FullName, "--keep");

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch0 kept").ShouldStartWith("10 blocks");
        run.Value("recording").ShouldContain("needs both sources");
        BlockSpool.FileFor(Folder("uno"), AudioChannel.Loopback).Exists.ShouldBeTrue();
        MeetingAudio.In(Folder("uno")).Exists.ShouldBeFalse();
    }

    [Fact]
    public void Audio_taken_out_lands_where_it_was_asked_for_and_the_recording_stays()
    {
        Recorded("daily", both: true);
        var into = Path.Combine(root.FullName, "taken out");

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--export", into);

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch0 taken out").ShouldContain("loopback.wav");
        run.Value("ch1 taken out").ShouldContain("microphone.wav");
        new FileInfo(Path.Combine(into, "loopback.wav")).Exists.ShouldBeTrue();
        new FileInfo(Path.Combine(into, "microphone.wav")).Exists.ShouldBeTrue();
        Folder("daily").EnumerateFiles("*.blocks").Count().ShouldBe(2);

        // Taking the sources out is not finishing the recording: what lands is one file per device,
        // where somebody asked for it, and the meeting is still a folder nobody has decided about.
        MeetingAudio.In(Folder("daily")).Exists.ShouldBeFalse();
    }

    /// <summary>
    /// ISC-125, at the surface: the one command that removes a recording, and it removes it only
    /// because somebody typed the word.
    /// </summary>
    [Fact]
    public void A_recording_is_thrown_away_only_by_somebody_saying_so()
    {
        Recorded("daily", both: true);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--discard");

        run.Code.ShouldBe(Cli.Ok);
        run.Value("thrown away").ShouldBe(Folder("daily").FullName);
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// Everything a recording says about itself is reported before it goes, so the last thing on
    /// the screen names what was thrown away rather than only that something was.
    /// </summary>
    [Fact]
    public void What_is_thrown_away_is_said_before_it_goes()
    {
        var meeting = Recorded("daily", both: true);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--discard");

        run.Value("meeting").ShouldBe(meeting.ToString());
        run.Value("ch0 holds").ShouldStartWith("loopback.blocks,");
    }

    /// <summary>
    /// ISC-133 at the one surface a person reads a rate on. A source whose counter was given up on
    /// is placed by the very clock it is then measured against, so it reports the rate it was
    /// opened at however fast it really ran — and the word that would have somebody diagnosing two
    /// channels drifting apart take that number for a measurement must not be beside it.
    /// </summary>
    [Fact]
    public void A_rate_a_counter_was_given_up_on_is_never_reported_as_measured()
    {
        Recorded("daily", both: true, microphoneCountsBy: 160);

        var run = CommandLine.Of("recover", "--in", Folder("daily").FullName, "--keep");

        run.Code.ShouldBe(Cli.Ok);
        run.Value("ch1 recorded").ShouldContain("counter given up on");
        run.Value("ch1 recorded").ShouldNotContain("measured");

        // The source beside it in the same recording counted in the frames it handed over, so it
        // says nothing about a counter — and its rate is not called measured either, because it is
        // the label until a recording is long enough to say otherwise.
        run.Value("ch0 recorded").ShouldNotContain("given up");
        run.Value("ch0 recorded").ShouldNotContain("measured");
    }

    [Fact]
    public void A_folder_holding_no_recording_is_refused()
    {
        root.CreateSubdirectory("empty");

        var run = CommandLine.Of("recover", "--in", Folder("empty").FullName, "--keep");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("no spool");
    }

    [Fact]
    public void A_folder_that_is_not_there_is_refused()
    {
        var run = CommandLine.Of("recover", "--in", Folder("nowhere").FullName, "--keep");

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("nowhere");
    }

    [Fact]
    public void Deciding_about_a_recording_without_saying_which_is_a_misuse()
    {
        var run = CommandLine.Of("recover", "--keep");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--in");
    }

    [Fact]
    public void A_flag_this_command_does_not_read_is_a_misuse()
    {
        var run = CommandLine.Of("recordings", "--spool", root.FullName, "--corpus", ".");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--corpus");
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

    /// <summary>
    /// A recording left exactly as killing the process during one leaves it. Every source counts
    /// its position in the frames it hands over unless <paramref name="microphoneCountsBy"/> says
    /// the microphone's counter runs in its own unit, the way a shared-mode webcam's does.
    /// </summary>
    private Guid Recorded(string name, bool both, int microphoneCountsBy = 480)
    {
        var folder = Folder(name);
        folder.Create();
        var meeting = Guid.NewGuid();

        Spool(folder, AudioChannel.Loopback, 480);
        if (both)
        {
            Spool(folder, AudioChannel.Microphone, microphoneCountsBy);
        }

        SpoolManifest.Write(folder, new SpoolCard(
            meeting,
            Guid.NewGuid(),
            UtcTimestamp.Parse("2026-08-15T09:41:07.250Z"),
            SourceProfile.Multichannel,
            [
                new SpooledSource(AudioChannel.Loopback, "Speakers (Realtek)", "{0.0.0.0}.speakers"),
                new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.0}.jabra"),
            ]));

        return meeting;
    }

    private void Spool(DirectoryInfo folder, AudioChannel channel, int countsBy)
    {
        using var writer = SpoolWriter.Create(BlockSpool.FileFor(folder, channel), channel, Format);
        for (var block = 0; block < 10; block++)
        {
            // Always 480 frames handed over; the counter beside them advances by whatever this
            // device counts in, which is the whole of what a webcam microphone does differently.
            writer.Write(new CapturePacket(
                channel,
                block * (long)countsBy,
                MonotonicInstant.FromMilliseconds(block * 10d),
                new byte[480 * Format.BytesPerSample]));
        }
    }

    /// <summary>Takes the tail off the way a process being killed mid write takes it off.</summary>
    private void CutOffMidBlock(AudioChannel channel)
    {
        var file = BlockSpool.FileFor(Folder("daily"), channel);
        using var stream = file.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(file.Length - 32);
    }
}
