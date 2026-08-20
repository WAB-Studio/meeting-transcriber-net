using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a folder says about the one thing that can change while it is being recorded: the channel
/// somebody moved off the program it was following and onto the whole machine's audio.
/// </summary>
/// <remarks>
/// No device. The card is written once and never rewritten, so this is where a change made an hour
/// into a meeting goes — and what is being proved here is that it survives being read back by
/// somebody holding nothing but the folder.
/// </remarks>
public sealed class SpoolChangesTests : IDisposable
{
    private static readonly UtcTimestamp Moved = UtcTimestamp.Parse("2026-08-15T10:14:52.125Z");

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public SpoolChangesTests() => folder.Create();

    /// <summary>
    /// ISC-77. A recording that was moved to the whole machine says so, says when, and says what it
    /// was listening to before — which is what somebody holding the folder needs to know that
    /// their notifications are in the file from that moment on.
    /// </summary>
    [Fact]
    public void A_channel_moved_to_the_whole_machine_says_so_beside_the_card()
    {
        SpoolChanges.Append(folder, Moving());

        var read = SpoolChanges.Find(folder).ShouldHaveSingleItem();
        read.At.ShouldBe(Moved);
        read.Channel.ShouldBe(AudioChannel.Loopback);
        read.Heard.ShouldBe("everything this machine plays");
        read.WasHearing.ShouldBe("teams (pid 8124)");
    }

    /// <summary>
    /// A channel whose device was taken away comes to name two devices over one meeting.
    /// The card says the one it opened on and this says the one that fed the rest of it, by the id
    /// that reopens it and not only by a name two identical headsets would share. No claim is
    /// cited: what the folder promises for a channel that named two devices is the open question
    /// on 86ak3ynkc.
    /// </summary>
    [Fact]
    public void A_channel_whose_device_changed_says_which_device_fed_the_rest_of_the_meeting()
    {
        SpoolChanges.Append(folder, Replaced());

        var read = SpoolChanges.Find(folder).ShouldHaveSingleItem();
        read.At.ShouldBe(Moved);
        read.Channel.ShouldBe(AudioChannel.Microphone);
        read.Heard.ShouldBe("Realtek Microphone Array");
        read.WasHearing.ShouldBe("Jabra Evolve 65");
        read.DeviceId.ShouldBe("{0.0.1.00000000}.{9c1a-realtek}");
    }

    /// <summary>
    /// Anti, on the same open question: neither way of obtaining channel 0 is an endpoint, so a
    /// line saying it moved onto one describes a recording this application cannot have made. It
    /// is refused where it is written and where it is read, because a folder can be edited by hand
    /// between the two.
    /// </summary>
    [Fact]
    public void A_channel_zero_is_never_said_to_have_moved_onto_a_device()
    {
        Should.Throw<AudioCaptureException>(() => SpoolChanges.Append(
                folder, Moving() with { DeviceId = "{0.0.1.00000000}.{9c1a-realtek}" }))
            .Message.ShouldContain("no device ever feeds it");

        SpoolChanges.In(folder).Exists.ShouldBeFalse("nothing moved, so nothing is written down");

        File.WriteAllText(
            SpoolChanges.In(folder).FullName,
            "{\"at\":\"2026-08-15T10:14:00.000Z\",\"channel\":0,\"heard\":\"everything this "
            + "machine plays\",\"was_hearing\":\"teams (pid 8124)\",\"device\":\"{0.0.1.0}.x\"}"
            + Environment.NewLine);

        Should.Throw<AudioCaptureException>(() => SpoolChanges.Find(folder))
            .Message.ShouldContain("no device ever feeds it");
    }

    /// <summary>
    /// A channel 0 that moved names no device, which is not a field left out: what it moved to is
    /// everything the machine plays, and nothing reopens that by an id.
    /// </summary>
    [Fact]
    public void A_channel_zero_that_moved_names_no_device()
    {
        SpoolChanges.Append(folder, Moving());

        SpoolChanges.Find(folder).ShouldHaveSingleItem().DeviceId.ShouldBeNull();
    }

    /// <summary>Almost every recording changes nothing, and that reads as nothing rather than as a failure.</summary>
    [Fact]
    public void A_recording_nobody_moved_says_nothing()
    {
        SpoolChanges.Find(folder).ShouldBeEmpty();
        SpoolChanges.In(folder).Exists.ShouldBeFalse();
    }

    /// <summary>
    /// ISC-122, read from this side: the card is never rewritten, so more than one change is more
    /// than one line and the ones already written are never touched again.
    /// </summary>
    [Fact]
    public void What_was_already_written_down_is_never_written_over()
    {
        SpoolChanges.Append(folder, Moving());
        SpoolChanges.Append(folder, Moving() with
        {
            At = Moved + Duration.FromSeconds(90),
            WasHearing = "msedge (pid 1000)",
        });

        var read = SpoolChanges.Find(folder);
        read.Count.ShouldBe(2);
        read[0].WasHearing.ShouldBe("teams (pid 8124)");
        read[0].At.ShouldBe(Moved);
        read[1].WasHearing.ShouldBe("msedge (pid 1000)");
        read[1].At.ShouldBe(Moved + Duration.FromSeconds(90));
    }

    /// <summary>
    /// The write that was underway when the machine died. What it costs is the account of that one
    /// change, the way a torn block costs one packet — and never the changes above it.
    /// </summary>
    [Fact]
    public void A_last_line_that_never_finished_landing_costs_only_itself()
    {
        SpoolChanges.Append(folder, Moving());
        File.AppendAllText(SpoolChanges.In(folder).FullName, "{\"at\":\"2026-08-15T10:1");

        SpoolChanges.Find(folder).ShouldHaveSingleItem()
            .WasHearing.ShouldBe("teams (pid 8124)");
    }

    /// <summary>
    /// A line that will not read with whole lines behind it is not a torn write — it is a file that
    /// has stopped being what it says it is, and reading four of five changes out of one would be
    /// worse than saying so.
    /// </summary>
    [Fact]
    public void A_line_that_will_not_read_anywhere_else_is_refused()
    {
        File.WriteAllLines(
            SpoolChanges.In(folder).FullName,
            ["{\"at\":\"2026-08-15T10:1", "{\"at\":\"2026-08-15T10:14:52.125Z\",\"channel\":0}"]);

        Should.Throw<AudioCaptureException>(() => SpoolChanges.Find(folder))
            .Message.ShouldContain(SpoolChanges.FileName);
    }

    /// <summary>
    /// A change naming no channel says nothing about the recording, and is refused rather than
    /// read as a change to channel 0 because that is the number a missing field comes back as.
    /// </summary>
    [Fact]
    public void A_change_that_names_no_channel_is_refused()
    {
        File.WriteAllText(
            SpoolChanges.In(folder).FullName,
            "{\"at\":\"2026-08-15T10:14:52.125Z\",\"heard\":\"Speakers\",\"was_hearing\":\"teams\"}\n");

        Should.Throw<AudioCaptureException>(() => SpoolChanges.Find(folder));
    }

    /// <summary>
    /// A change that does not say what the channel was on before it says nothing worth reading: the
    /// card names what the recording opened on, so a line that only names what it moved to leaves
    /// somebody unable to tell which stretch of the file is which.
    /// </summary>
    [Fact]
    public void A_change_that_does_not_say_what_the_channel_was_on_is_refused()
    {
        File.WriteAllText(
            SpoolChanges.In(folder).FullName,
            "{\"at\":\"2026-08-15T10:14:52.125Z\",\"channel\":0,"
            + "\"heard\":\"everything this machine plays\"}\n");

        Should.Throw<AudioCaptureException>(() => SpoolChanges.Find(folder))
            .Message.ShouldContain("was_hearing");
    }

    /// <summary>
    /// A last line that will not read is only a torn write if the file stops in the middle of it.
    /// One that was finished and still will not read is a file that has stopped being what it says
    /// it is, and reading it as "nothing was moved" would be this file failing in exactly the
    /// direction it exists to prevent.
    /// </summary>
    [Fact]
    public void A_finished_line_that_will_not_read_is_refused_rather_than_taken_for_a_torn_write()
    {
        File.WriteAllText(SpoolChanges.In(folder).FullName, "{\"at\":\"2026-08-15T10:1\n");

        Should.Throw<AudioCaptureException>(() => SpoolChanges.Find(folder));
    }

    /// <summary>
    /// A recording is never written over another one, and that covers what it says about itself as
    /// much as its blocks: a folder still holding one recording's changes is refused before a
    /// device is opened.
    /// </summary>
    [Fact]
    public void A_folder_still_holding_a_recordings_changes_is_not_recorded_into()
    {
        SpoolChanges.Append(folder, Moving());

        Should.Throw<AudioCaptureException>(() => BlockSpool.EnsureNothingRecordedIn(folder))
            .Message.ShouldContain(SpoolChanges.FileName);
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

    private static SourceChanged Moving() => new(
        Moved,
        AudioChannel.Loopback,
        "everything this machine plays",
        "teams (pid 8124)");

    private static SourceChanged Replaced() => new(
        Moved,
        AudioChannel.Microphone,
        "Realtek Microphone Array",
        "Jabra Evolve 65",
        "{0.0.1.00000000}.{9c1a-realtek}");
}
