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

    /// <summary>
    /// The card's Delivers. A write that landed in part leaves bytes no line break follows, and
    /// the retry that its failure provokes settles them instead of landing its JSON on their back,
    /// so what is on disk is never the one shape the reader throws a whole folder away over — a
    /// complete line that will not read with whole lines behind it.
    /// </summary>
    [Fact]
    public void An_append_behind_a_torn_write_does_not_glue_itself_onto_it()
    {
        SpoolChanges.Append(folder, Moving());
        File.AppendAllText(SpoolChanges.In(folder).FullName, "{\"at\":\"2026-08-15T10:1");

        SpoolChanges.Append(folder, Moving() with
        {
            At = Moved + Duration.FromSeconds(90),
            WasHearing = "msedge (pid 1000)",
        });

        var read = SpoolChanges.Find(folder);
        read.Count.ShouldBe(2);
        read[0].WasHearing.ShouldBe("teams (pid 8124)");
        read[1].WasHearing.ShouldBe("msedge (pid 1000)");
    }

    /// <summary>
    /// A tail that lost only its line break is dropped as readily as one cut through the middle,
    /// because the same thing put both there: an append that threw. The write goes before the
    /// channel hands over, so its failure is a move that did not happen — and terminating what it
    /// left would make the folder name a move nobody made, permanently, with the retry's line
    /// underneath saying the channel was still on the source the line above claims it had left.
    /// </summary>
    [Fact]
    public void A_torn_write_is_dropped_even_when_all_but_its_line_break_landed()
    {
        SpoolChanges.Append(folder, Moving());
        using (var trimming = SpoolChanges.In(folder).Open(FileMode.Open, FileAccess.Write))
        {
            trimming.SetLength(trimming.Length - Environment.NewLine.Length);
        }

        SpoolChanges.Append(folder, Moving() with
        {
            At = Moved + Duration.FromSeconds(90),
            WasHearing = "msedge (pid 1000)",
        });

        SpoolChanges.Find(folder).ShouldHaveSingleItem()
            .WasHearing.ShouldBe("msedge (pid 1000)");
    }

    /// <summary>
    /// ISC-122 under the handle this needed. Opening in append mode was Windows refusing the seek;
    /// what refuses it now is arithmetic, so this pins the arithmetic: with two changes already
    /// down and a torn write behind them, settling the tail leaves every byte up to the last line
    /// break exactly as it was. It is also what a search from the front of the file instead of the
    /// back turns red — that mutation deletes every change but the first and every other fact here
    /// stays green.
    /// </summary>
    [Fact]
    public void Settling_a_tail_leaves_every_line_already_written_byte_for_byte()
    {
        SpoolChanges.Append(folder, Moving());
        SpoolChanges.Append(folder, Moving() with
        {
            At = Moved + Duration.FromSeconds(90),
            WasHearing = "msedge (pid 1000)",
        });

        var already = File.ReadAllBytes(SpoolChanges.In(folder).FullName);
        File.AppendAllText(SpoolChanges.In(folder).FullName, "{\"at\":\"2026-08-15T10:1");

        SpoolChanges.Append(folder, Moving() with
        {
            At = Moved + Duration.FromSeconds(180),
            WasHearing = "zoom (pid 4400)",
        });

        var settled = File.ReadAllBytes(SpoolChanges.In(folder).FullName);
        settled.Length.ShouldBeGreaterThan(already.Length);
        settled[..already.Length].ShouldBe(already);

        var read = SpoolChanges.Find(folder);
        read.Count.ShouldBe(3);
        read[0].WasHearing.ShouldBe("teams (pid 8124)");
        read[1].WasHearing.ShouldBe("msedge (pid 1000)");
        read[2].WasHearing.ShouldBe("zoom (pid 4400)");
    }

    /// <summary>
    /// The likeliest tear of all, since most folders only ever hold one line: the first change was
    /// the one cut in half, so there is no line break anywhere and the whole file is the fragment.
    /// It is dropped and the next change starts the file again — the branch that truncates to
    /// nothing, which a guard on "no line break found" would skip straight past.
    /// </summary>
    [Fact]
    public void A_folder_whose_only_line_never_landed_starts_again_from_the_next_change()
    {
        File.WriteAllText(SpoolChanges.In(folder).FullName, "{\"at\":\"2026-08-15T10:1");

        SpoolChanges.Append(folder, Moving());

        SpoolChanges.Find(folder).ShouldHaveSingleItem()
            .WasHearing.ShouldBe("teams (pid 8124)");
    }

    /// <summary>
    /// Guarding the shape of the file rather than proving new behaviour. Settling the tail must
    /// cost nothing when there is no tail to settle: a terminator written unconditionally would
    /// leave a blank line between every pair of changes, in a file whose second job is being read
    /// by a person holding nothing but the folder. It is also the only thing that says the
    /// serializer never puts a line break inside a record, which both the writer and the reader
    /// are built on and neither of them checks.
    /// </summary>
    [Fact]
    public void An_ordinary_append_adds_one_line_and_nothing_else()
    {
        SpoolChanges.Append(folder, Moving());
        SpoolChanges.Append(folder, Moving() with { At = Moved + Duration.FromSeconds(90) });

        File.ReadAllLines(SpoolChanges.In(folder).FullName).Length.ShouldBe(2);
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
