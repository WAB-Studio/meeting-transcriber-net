using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

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
public sealed class UnfinishedRecordingsTests : IDisposable
{
    /// <summary>
    /// How long the handle is held once a discard has started, in the one test that asserts the
    /// waiting.
    /// </summary>
    /// <remarks>
    /// Set against <c>UnfinishedRecordings.RemovalPatienceMilliseconds</c>, which is 250 and is
    /// internal with no <c>InternalsVisibleTo</c> anywhere in this repository — so it cannot be
    /// divided down from here and this number carries the relationship instead. Twenty against two
    /// hundred and fifty is a twelfth of the budget: long enough that the first rename cannot find
    /// the folder already free, which the probe on the line above it proves outright, and short
    /// enough that a scheduling stall would have to run most of a quarter second to turn it red. If
    /// that constant is ever lowered below about a hundred, this number moves with it.
    /// </remarks>
    private const int HoldForMilliseconds = 20;

    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);
    private static readonly StreamFormat CheapMicrophone = new(44_100, 1, 16, SampleEncoding.Pcm);

    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public UnfinishedRecordingsTests() => root.Create();

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

        var waiting = UnfinishedRecordings.In(root);

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
    /// ISC-77. The card says what channel 0 opened on and never changes; a recording whose channel
    /// 0 was moved to the whole machine mid-meeting is found again saying both, so somebody
    /// deciding what to do with the folder knows what is really in the second half of it.
    /// </summary>
    [Fact]
    public void A_recording_whose_channel_was_moved_is_found_again_saying_so()
    {
        var folder = Folder("moved");
        Recorded("moved", both: true);
        SpoolChanges.Append(folder, new SourceChanged(
            UtcTimestamp.Parse("2026-08-15T09:41:31.500Z"),
            AudioChannel.Loopback,
            "everything this machine plays",
            "teams (pid 8124)"));

        var moved = UnfinishedRecordings.In(root).ShouldHaveSingleItem();

        moved.Unreadable.ShouldBeNull();
        moved.Card.ShouldNotBeNull().On(AudioChannel.Loopback).Heard
            .ShouldBe("everything this machine plays");
        moved.Changed.ShouldHaveSingleItem().WasHearing.ShouldBe("teams (pid 8124)");
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

        var waiting = UnfinishedRecordings.In(root);

        waiting.Count.ShouldBe(1);
        waiting[0].Card.ShouldBeNull();
        waiting[0].Sources.Count.ShouldBe(2);
    }

    /// <summary>
    /// A card torn in half is the crash this whole path is for, arriving in the one file that was
    /// meant to explain it. The recording is still offered, saying why it cannot name itself — and
    /// so is every other recording beside it, because one damaged folder taking the list down with
    /// it would be the crash winning twice.
    /// </summary>
    [Fact]
    public void A_recording_whose_card_was_torn_in_half_is_offered_and_takes_no_other_one_with_it()
    {
        Recorded("torn", both: true);
        var whole = Recorded("whole", both: true);
        var card = SpoolManifest.In(Folder("torn"));
        using (var cut = card.Open(FileMode.Open, FileAccess.Write))
        {
            cut.SetLength(card.Length / 2);
        }

        var waiting = UnfinishedRecordings.In(root);

        waiting.Select(recording => recording.Folder.Name).ShouldBe(["torn", "whole"]);
        waiting[0].Card.ShouldBeNull();
        waiting[0].Unreadable.ShouldNotBeNull().ShouldContain(SpoolManifest.FileName);
        waiting[0].Sources.Count.ShouldBe(2);
        waiting[1].Card.ShouldNotBeNull().MeetingId.ShouldBe(whole);
        waiting[1].Unreadable.ShouldBeNull();
    }

    /// <summary>
    /// The same recording, named directly rather than found: a card that will not read is not a
    /// reason to refuse a decision about the blocks beside it.
    /// </summary>
    [Fact]
    public void A_recording_whose_card_was_torn_in_half_can_still_be_decided_about()
    {
        Recorded("torn", both: true);
        File.WriteAllText(SpoolManifest.In(Folder("torn")).FullName, "{ \"meeting\": ");

        UnfinishedRecordings.At(Folder("torn")).Keep().Count.ShouldBe(2);
        Should.NotThrow(() => UnfinishedRecordings.At(Folder("torn")).Discard());
        Folder("torn").Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_machine_that_has_never_recorded_has_nothing_waiting()
    {
        UnfinishedRecordings.In(new DirectoryInfo(Path.Combine(root.FullName, "nowhere"))).ShouldBeEmpty();
    }

    /// <summary>
    /// ISC-124, the first of the three. Keeping a recording reads it through and says what it is
    /// worth; what it must not do is change any of it, because the blocks already are the meeting.
    /// </summary>
    [Fact]
    public void A_recording_that_is_kept_says_what_survived_and_is_still_there_afterwards()
    {
        var written = Recorded("daily", both: true);
        var recording = UnfinishedRecordings.At(Folder("daily"));

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

        var survived = UnfinishedRecordings.At(Folder("daily")).Keep();

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

        var exported = UnfinishedRecordings.At(Folder("daily")).Export(into);

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

        Should.Throw<AudioCaptureException>(() => UnfinishedRecordings.At(Folder("daily")).Export(into))
            .Message.ShouldContain("microphone.wav");

        into.EnumerateFiles("loopback.wav").ShouldBeEmpty();
    }

    /// <summary>
    /// Half of a recording somebody asked for is worse than a refusal, and worse still because the
    /// half that landed is what makes the second attempt refuse the folder. So a source that
    /// cannot be read takes back what the sources before it wrote, and asking again is a thing
    /// somebody can do.
    /// </summary>
    [Fact]
    public void Audio_taken_out_leaves_nothing_behind_when_a_later_source_cannot_be_read()
    {
        Recorded("daily", both: true);
        Corrupt(BlockSpool.FileFor(Folder("daily"), AudioChannel.Microphone), at: 16);
        var into = Folder("taken out");

        Should.Throw<AudioCaptureException>(() => UnfinishedRecordings.At(Folder("daily")).Export(into));

        into.EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>ISC-124, the third, and the only one of them that takes anything away.</summary>
    [Fact]
    public void A_recording_that_is_thrown_away_is_gone_with_everything_in_it()
    {
        Recorded("daily", both: true);

        UnfinishedRecordings.At(Folder("daily")).Discard();

        Folder("daily").Exists.ShouldBeFalse();
        UnfinishedRecordings.In(root).ShouldBeEmpty();

        // Gone with everything in it includes nothing left beside it: the folder the removal moves
        // the recording into is removed too, rather than left holding a meeting's blocks under a
        // name nothing in the product ever looks in.
        root.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <summary>
    /// The card this file exists to answer. A second reader — another window keeping the same
    /// recording, a prompt exporting it — holds a block file open, and a discard pressed while it
    /// does used to empty the folder as far as that file: the card and the changes were already
    /// gone by the time the refusal landed. What is left has to be the recording or nothing.
    /// </summary>
    /// <remarks>
    /// The handle is a <em>reader</em>, which is the whole point. Nothing in this engine can see it
    /// — <c>BlockSpool.IsStillBeingWritten</c> asks about writers — so the recording reaches the
    /// removal with <c>Running</c> false, <c>BeingSaved</c> false and <c>EnsureRemovable</c>
    /// content, which is exactly what a keep or an export in another window looks like from here.
    /// NTFS still refuses to rename the folder over it.
    /// </remarks>
    [Fact]
    public void A_recording_a_second_reader_is_holding_is_left_whole_rather_than_half_emptied()
    {
        Recorded("daily", both: true);
        SpoolChanges.Append(Folder("daily"), new SourceChanged(
            UtcTimestamp.Parse("2026-08-15T10:11:00.000Z"),
            AudioChannel.Loopback,
            "everything this machine plays",
            "teams (pid 8124)"));

        using var reading = Reading(AudioChannel.Microphone);

        var refused = Should.Throw<AudioCaptureException>(
            () => UnfinishedRecordings.At(Folder("daily")).Discard());

        refused.Message.ShouldContain(Folder("daily").FullName);
        refused.Message.ShouldContain("Nothing was removed");
        refused.Message.ShouldNotContain(".blocks");

        Folder("daily").EnumerateFiles().Select(file => file.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["changes.jsonl", "loopback.blocks", "manifest.json", "microphone.blocks"]);

        var still = UnfinishedRecordings.In(root).ShouldHaveSingleItem();
        still.Sources.Count.ShouldBe(2);
        still.Card.ShouldNotBeNull();

        // And nothing beside it: the folder the refused move made was taken back.
        root.EnumerateDirectories().Select(one => one.Name).ShouldBe(["daily"]);
    }

    /// <summary>
    /// That the waiting is a wait and not a pause before the same refusal. Whatever opens a file
    /// the instant it is closed — a real-time scanner is the usual explanation — is the likeliest
    /// holder of all, because <c>EnsureRemovable</c> closes every spool a millisecond before the
    /// rename is attempted. A removal that gave up on the first refusal would fail intermittently
    /// on real machines and never on a build agent.
    /// </summary>
    [Fact]
    public void A_recording_somebody_lets_go_of_is_thrown_away_once_they_do()
    {
        Recorded("daily", both: true);

        // Disposed from two places on purpose: the thread below is the one under test, and the
        // using is what guarantees the handle is gone when the test leaves however it leaves,
        // including the path where Discard throws and every line after it is skipped.
        using var reading = Reading(AudioChannel.Microphone);

        // That the handle really does refuse the rename right now, so what follows is the waiting
        // working rather than a folder nothing was ever holding.
        Should.Throw<IOException>(
            () => Directory.Move(Folder("daily").FullName, Folder("never").FullName));

        // Guarded because this thread is the process's and not xunit's: an exception out of it is
        // unhandled and takes the test host down, and the run then says the host died rather than
        // saying which test did.
        Exception? lettingGoFailed = null;
        var letting = new Thread(() =>
        {
            Thread.Sleep(HoldForMilliseconds);

            try
            {
                reading.Dispose();
            }
            catch (Exception failed)
            {
                lettingGoFailed = failed;
            }
        });

        var clock = Stopwatch.StartNew();
        letting.Start();

        try
        {
            UnfinishedRecordings.At(Folder("daily")).Discard();
        }
        finally
        {
            letting.Join();
        }

        lettingGoFailed.ShouldBeNull();

        // The assertion this test exists for, and the one a green run cannot give on its own.
        // Thread.Sleep never returns early, so the handle cannot have closed before that much
        // clock time; a Discard that came back sooner is one whose first rename walked straight
        // through, which proves nothing. It is what FoldersTests buys with its refusal count,
        // bought without a return value Discard's signature could carry.
        clock.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(HoldForMilliseconds);

        Folder("daily").Refresh();
        Folder("daily").Exists.ShouldBeFalse();
        root.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <summary>
    /// The other end of the same race: this window is the one that lost. A recording read a moment
    /// ago and thrown away by somebody else since is a sentence about the meeting, not Windows
    /// saying it could not find part of a path ending in <c>loopback.blocks</c>.
    /// </summary>
    /// <remarks>
    /// Reachable from two windows, or a window and a prompt, over one recording — the same pair the
    /// rest of this file is about. Nothing is at risk on this path, because the recording both of
    /// them were told to throw away is gone; what is at risk is the sentence, which is the whole of
    /// what <c>EnsureRemovable</c> is still for.
    /// </remarks>
    [Fact]
    public void A_recording_somebody_else_threw_away_first_is_said_to_be_gone_rather_than_unfindable()
    {
        Recorded("daily", both: true);
        var recording = UnfinishedRecordings.At(Folder("daily"));

        // What the other window did between the read above and the decision below.
        Folder("daily").Delete(recursive: true);

        var refused = Should.Throw<AudioCaptureException>(recording.Discard);

        refused.Message.ShouldContain(Folder("daily").FullName);
        refused.Message.ShouldContain("Nothing was removed");
        refused.Message.ShouldNotContain(".blocks");
    }

    /// <summary>
    /// A machine that died between the move and the delete leaves a recording under the removal's
    /// own name, and nothing offers it any more. Finishing that removal here would be a second
    /// half-emptied one at a path nobody was told about, which is the defect this whole shape
    /// exists to end — so it is refused, loudly, with both folders untouched.
    /// </summary>
    [Fact]
    public void A_folder_left_over_from_a_removal_that_was_cut_off_is_refused_rather_than_destroyed()
    {
        Recorded("daily", both: true);

        // Shaped exactly as a machine dying between the move and the delete leaves it: a directory
        // and no files at the top level.
        var leftOver = Folder(".removing-daily");
        var inside = leftOver.CreateSubdirectory("daily");
        File.WriteAllText(Path.Combine(inside.FullName, SpoolManifest.FileName), "{}");

        var refused = Should.Throw<AudioCaptureException>(
            () => UnfinishedRecordings.At(Folder("daily")).Discard());

        refused.Message.ShouldContain(leftOver.FullName);
        refused.Message.ShouldContain("by hand");

        Folder("daily").EnumerateFiles().Select(file => file.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["loopback.blocks", "manifest.json", "microphone.blocks"]);

        leftOver.EnumerateDirectories().Select(one => one.Name).ShouldBe(["daily"]);
        inside.EnumerateFiles().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The other side of the same line, and the only leftover actually reachable: a move that was
    /// refused and whose own cleanup could not run either. Refusing on the name existing rather
    /// than on it holding something would leave the retry a person makes next refused for good,
    /// with no way out but a file manager.
    /// </summary>
    [Fact]
    public void An_empty_folder_a_refused_removal_left_does_not_stop_the_next_one()
    {
        Recorded("daily", both: true);
        Folder(".removing-daily").Create();

        UnfinishedRecordings.At(Folder("daily")).Discard();

        root.EnumerateDirectories().ShouldBeEmpty();
    }

    /// <summary>
    /// The card's proof, at the engine, with two real processes: one standing over a folder it is
    /// reading and one discarding it, and the folder survives with everything in it.
    /// </summary>
    /// <remarks>
    /// Two processes because that is the situation — one window at a prompt, another typing the
    /// discard — and because nothing inside one process reaches it: the reading mark is shared, so
    /// a hold taken here would be joined rather than met. The kill runs in a <c>finally</c> for the
    /// reason <c>SavingMarkTests</c> gives.
    /// </remarks>
    [Fact]
    public void A_recording_somebody_else_is_reading_is_not_thrown_away_under_them()
    {
        Recorded("daily", both: true);

        // The other process opens with `Open` and never `Create`, so the mark has to be on disk
        // before it starts — and on both sides of the snapshot below, so this is not what the
        // comparison catches.
        ReadingMark.Take(Folder("daily")).Dispose();

        var mark = new FileInfo(
            Path.Combine(Folder("daily").FullName, ReadingMark.FileName));
        var before = Snapshot(Folder("daily"));

        using var holder = AnotherProcess.Holding(mark, FileShare.ReadWrite);
        try
        {
            AnotherProcess.HasTakenIt(holder);
            ReadingMark.IsHeldIn(Folder("daily")).ShouldBeTrue();

            var refused = Should.Throw<AudioCaptureException>(
                () => UnfinishedRecordings.At(Folder("daily")).Discard());

            refused.Message.ShouldContain("reading the recording");
            refused.Message.ShouldContain(Folder("daily").FullName);
            refused.Message.ShouldNotContain(".blocks");

            // Every byte of it, and nothing left beside it either.
            Snapshot(Folder("daily")).ShouldBe(before);
            root.EnumerateDirectories().Select(one => one.Name).ShouldBe(["daily"]);
        }
        finally
        {
            try
            {
                if (!holder.HasExited)
                {
                    holder.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ending) when (
                ending is InvalidOperationException or Win32Exception or AggregateException)
            {
                // Every way a kill can refuse: it ended between the question and the kill, which is
                // what the kill was for; Windows would not have it; a child of it would not go. A
                // throw from a finally would bury the sentence saying what really failed, and what
                // is left behind either way ends itself when its own sleep runs out.
            }

            holder.WaitForExit();
        }

        // Said in full because the answer is also the answer a broken ReadingMark gives, and the
        // mark the dead process left is still on disk holding nothing.
        ReadingMark.IsHeldIn(Folder("daily")).ShouldBeFalse(
            "the mark still reads as held after the only process holding it is gone. Either the "
            + "mark means its existence rather than a handle, which is what this refuses, or "
            + "something else on this machine had the file open for the one instant this was "
            + "asked — IsHeldIn answers 'held' to any IOException, deliberately.");

        UnfinishedRecordings.At(Folder("daily")).Discard();
        Folder("daily").Refresh();
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The same fact in one process, which is what makes it cheap enough to be the one that stays
    /// sharp: a recording being read is refused a discard, and is still keepable and exportable
    /// while it is read.
    /// </summary>
    [Fact]
    public void A_recording_being_read_is_not_thrown_away_under_the_reader()
    {
        Recorded("daily", both: true);
        using var reading = ReadingMark.Take(Folder("daily"));
        var recording = UnfinishedRecordings.At(Folder("daily"));

        // A read does not make a recording undecidable, and this is what stops somebody later
        // folding it into that property: only the discard waits, and it waits when it is pressed.
        recording.NothingToDecideYet.ShouldBeNull();
        recording.Keep().Count.ShouldBe(2);
        recording.Export(Folder("out")).Count.ShouldBe(2);

        var refused = Should.Throw<AudioCaptureException>(recording.Discard);
        refused.Message.ShouldContain("reading the recording");
        refused.Message.ShouldContain(Folder("daily").FullName);
        refused.Message.ShouldNotContain(".blocks");

        Folder("daily").EnumerateFiles("*.blocks").Count().ShouldBe(2);

        // The card is asserted by name because it is what no reader ever holds, and so what a
        // rename losing this race would have taken first.
        SpoolManifest.Find(Folder("daily")).ShouldNotBeNull();

        // And the refused move took its own folder back, leaving only this read's destination.
        root.EnumerateDirectories().Select(one => one.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["daily", "out"]);

        reading.Dispose();
        recording.Discard();
        Folder("daily").Refresh();
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The deterministic half of taking the mark and releasing it: both reads take it before they
    /// touch a source and let it go on the way out, including the way out through a refusal.
    /// </summary>
    [Fact]
    public void A_read_that_ends_leaves_its_mark_behind_holding_nothing()
    {
        Recorded("daily", both: true);
        MarkedAsRead(Folder("daily")).ShouldBeFalse();

        UnfinishedRecordings.At(Folder("daily")).Keep();

        MarkedAsRead(Folder("daily")).ShouldBeTrue();
        ReadingMark.IsHeldIn(Folder("daily")).ShouldBeFalse();

        // Each of the two is held to it on its own rather than one covering for the other.
        Recorded("weekly", both: true);
        MarkedAsRead(Folder("weekly")).ShouldBeFalse();

        UnfinishedRecordings.At(Folder("weekly")).Export(Folder("out"));

        MarkedAsRead(Folder("weekly")).ShouldBeTrue();
        ReadingMark.IsHeldIn(Folder("weekly")).ShouldBeFalse();

        // And the way out through a refusal: the destination already holds one of the two names,
        // so the export is refused after the mark was taken and before any audio was poured.
        Recorded("monthly", both: true);

        Should.Throw<AudioCaptureException>(
            () => UnfinishedRecordings.At(Folder("monthly")).Export(Folder("out")));

        MarkedAsRead(Folder("monthly")).ShouldBeTrue();
        ReadingMark.IsHeldIn(Folder("monthly")).ShouldBeFalse();
    }

    /// <summary>
    /// ISC-126.2's shape arriving through the new mark. A read that died left its mark lying in
    /// the folder, and every one of the three is open over it again.
    /// </summary>
    [Fact]
    public void A_mark_left_by_a_read_that_died_leaves_the_recording_decidable()
    {
        Recorded("daily", both: true);
        ReadingMark.Take(Folder("daily")).Dispose();
        MarkedAsRead(Folder("daily"))
            .ShouldBeTrue("the folder has to be carrying the mark for this to be about one");

        var recording = UnfinishedRecordings.At(Folder("daily"));

        recording.NothingToDecideYet.ShouldBeNull();
        recording.Keep().Count.ShouldBe(2);
        recording.Export(Folder("out")).Count.ShouldBe(2);

        // And the stale mark goes with the folder through the rename and the delete.
        recording.Discard();
        Folder("daily").Refresh();
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// A recording in a folder that will not take the mark is read through exactly as it was before
    /// the mark existed, and all three outcomes stay open over it.
    /// </summary>
    /// <remarks>
    /// This is the one that would go red on a build that made the mark a precondition for reading.
    /// A spool folder needed only read access until this file existed, and the drawer's background
    /// pass reads every waiting recording through — so a claim that threw would put a meeting whose
    /// blocks are all there in front of somebody as <em>the blocks of this one would not read</em>,
    /// with throwing it away as the only thing offered. The folder is made unwritable at the one
    /// name that matters by putting a directory where the mark goes, which stands in for the real
    /// ways it happens: no room on the disk, an access this process does not have.
    /// </remarks>
    [Fact]
    public void A_recording_whose_folder_will_not_take_the_mark_is_still_read_and_still_decidable()
    {
        Recorded("daily", both: true);
        Directory.CreateDirectory(
            Path.Combine(Folder("daily").FullName, ReadingMark.FileName));

        var recording = UnfinishedRecordings.At(Folder("daily"));

        recording.NothingToDecideYet.ShouldBeNull();
        recording.Keep().Count.ShouldBe(2);
        recording.Export(Folder("out")).Count.ShouldBe(2);

        // Nothing is held, so nothing refuses the discard either — which is what there was to lose
        // in this folder, and it is what there was to lose before this mark existed.
        ReadingMark.IsHeldIn(Folder("daily")).ShouldBeFalse();
        recording.Discard();
        Folder("daily").Refresh();
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// The other thing the mark is allowed to refuse, and the reason it is: a folder thrown away
    /// between the listing and the read is a sentence about the recording rather than the one the
    /// first block file would throw — and a refused export leaves no destination folder behind,
    /// which is what taking the mark before <c>into.Create()</c> is for.
    /// </summary>
    [Fact]
    public void A_recording_thrown_away_under_a_reader_refuses_by_naming_the_folder()
    {
        Recorded("daily", both: true);
        var recording = UnfinishedRecordings.At(Folder("daily"));

        // Exactly the race a removal exists to survive: another window discarded it while this one
        // was being decided about.
        Folder("daily").Delete(recursive: true);

        var refused = Should.Throw<AudioCaptureException>(() => recording.Export(Folder("out")));

        refused.Message.ShouldContain(Folder("daily").FullName);
        refused.Message.ShouldNotContain(".blocks");
        Folder("out").Refresh();
        Folder("out").Exists.ShouldBeFalse();

        Should.Throw<AudioCaptureException>(() => recording.Keep())
            .Message.ShouldContain(Folder("daily").FullName);
    }

    /// <summary>
    /// A folder a read marked and nothing was ever recorded into is still swept away. One list
    /// feeds the question and the delete, so teaching it the third mark covers both.
    /// </summary>
    /// <remarks>
    /// The invariant and not a state the product reaches: a folder with no spool in it was never
    /// read, so nothing puts a mark in one — this drives <c>NamesAPressLeaves</c> past the sweep's
    /// own entry point on purpose, the way the leftover-removal probe does. What it holds is that
    /// the question and the delete cannot come to disagree, which is the property that stops the
    /// question naming a file the delete would then choke on.
    /// </remarks>
    [Fact]
    public void A_folder_a_read_marked_and_nothing_recorded_into_is_still_swept()
    {
        var folder = Folder("daily");
        folder.Create();

        SpoolWriter.Create(
            BlockSpool.FileFor(folder, AudioChannel.Loopback),
            AudioChannel.Loopback,
            StereoFloat).Dispose();

        ReadingMark.Take(folder).Dispose();

        UnfinishedRecordings.WhatSaysARecordingHappenedIn(folder).ShouldBeNull();

        UnfinishedRecordings.EraseWhereNothingWasRecorded(folder);

        folder.Refresh();
        folder.Exists.ShouldBeFalse();
    }

    /// <summary>
    /// What the sweep of folders nothing was recorded into sees when a discard did not finish, and
    /// the engine half of what <c>docs/corpus.md</c> promises about that folder: it is named, and
    /// nothing takes it away.
    /// </summary>
    /// <remarks>
    /// The sweep asks these two and nothing else about a folder under <c>spool/</c>, so pinning
    /// them here is pinning what the sweep does. A page saying a recording somebody threw away sits
    /// safely on disk until a person deletes it, with nothing holding the engine to that, is a page
    /// that goes quietly wrong the day one of these answers changes.
    /// </remarks>
    [Fact]
    public void A_removal_that_did_not_finish_is_named_by_the_sweep_and_taken_away_by_nothing()
    {
        var leftOver = Folder(".removing-daily");
        var inside = leftOver.CreateSubdirectory("daily");
        Spool(inside, AudioChannel.Loopback, StereoFloat);

        UnfinishedRecordings.WhatSaysARecordingHappenedIn(leftOver)
            .ShouldNotBeNull()
            .ShouldContain("daily");

        Should.Throw<AudioCaptureException>(
            () => UnfinishedRecordings.EraseWhereNothingWasRecorded(leftOver));

        leftOver.EnumerateDirectories().ShouldHaveSingleItem();
        inside.EnumerateFiles().ShouldHaveSingleItem();
    }

    /// <summary>
    /// ISC-126. A meeting somebody is in the middle of is the last thing to leave off a list, and
    /// the last thing to offer as one to decide about — two of the three outcomes would read a
    /// file that is still growing and the third would throw away a meeting that is still
    /// happening. So it is said, and it is said before anything can be chosen.
    /// </summary>
    [Fact]
    public void A_meeting_still_being_recorded_is_said_to_be_rather_than_offered_as_one_to_decide_about()
    {
        Recorded("daily", both: true);

        using (var writing = Recording(AudioChannel.Loopback))
        {
            UnfinishedRecordings.In(root).ShouldHaveSingleItem().Running.ShouldBeTrue();
            UnfinishedRecordings.At(Folder("daily")).Running.ShouldBeTrue();
        }

        // The same folder the moment nothing holds it: what was running is now waiting.
        UnfinishedRecordings.In(root).ShouldHaveSingleItem().Running.ShouldBeFalse();
    }

    /// <summary>
    /// The other half of ISC-126: saying so is not enough on its own, because what is offered is
    /// what somebody acts on. All three outcomes refuse a meeting that is still being recorded.
    /// </summary>
    [Fact]
    public void None_of_the_three_outcomes_lands_on_a_meeting_that_is_still_being_recorded()
    {
        Recorded("daily", both: true);
        var recording = UnfinishedRecordings.At(Folder("daily"));
        using var writing = Recording(AudioChannel.Loopback);

        Should.Throw<AudioCaptureException>(() => recording.Keep()).Message.ShouldContain("still running");
        Should.Throw<AudioCaptureException>(() => recording.Export(Folder("out")))
            .Message.ShouldContain("still running");
        Should.Throw<AudioCaptureException>(recording.Discard).Message.ShouldContain("still running");

        Folder("daily").Exists.ShouldBeTrue();
        Folder("out").EnumerateFiles().ShouldBeEmpty();
    }

    /// <summary>
    /// The same three, refused on what the recording is rather than on what a handle did. The
    /// difference from the test above is the order: this one is found while the capture is already
    /// writing, which is what a start after a crash finds, so the refusal is reached before any
    /// file is opened and says what a person is deciding about — a meeting that has not stopped.
    /// </summary>
    [Fact]
    public void A_recording_found_while_it_is_being_written_refuses_the_three_by_naming_the_meeting()
    {
        Recorded("daily", both: true);

        using var writing = Recording(AudioChannel.Loopback);
        var recording = UnfinishedRecordings.At(Folder("daily"));

        recording.NothingToDecideYet.ShouldNotBeNull().ShouldContain("still being recorded");

        Refuses(() => recording.Keep());
        Refuses(() => recording.Export(Folder("out")));
        Refuses(recording.Discard);

        Folder("daily").Exists.ShouldBeTrue();

        // Not even the folder somebody named to take it out into: a refusal that had already made
        // one would leave an empty directory behind as the only trace of the attempt.
        Folder("out").Exists.ShouldBeFalse();

        void Refuses(Action decision)
        {
            var thrown = Should.Throw<AudioCaptureException>(decision);

            thrown.Message.ShouldContain("still being recorded");
            thrown.Message.ShouldContain(Folder("daily").FullName);
            thrown.Message.ShouldNotContain(".blocks");
        }
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
        var recording = UnfinishedRecordings.At(Folder("daily"));

        // Taken after the recording was read, which is what leaves the snapshot saying nothing is
        // writing while something is. The refusal comes from the file system for that reason.
        using var writing = Recording(AudioChannel.Loopback);

        Should.Throw<AudioCaptureException>(recording.Discard).Message.ShouldContain("still running");
        Folder("daily").Exists.ShouldBeTrue();
    }

    /// <summary>
    /// ISC-126.1 at the engine. A recording whose save is running says so and takes none of the
    /// three: the blocks are being read into a meeting at that moment, and throwing the folder away
    /// would take the recording out from under the read that is making it.
    /// </summary>
    /// <remarks>
    /// It is refused in the save's own words and not the capture's. Nobody is speaking into this
    /// meeting — it is over — and what a person is waiting for is a save ending rather than a
    /// meeting ending, which is a different sentence and a different length of wait.
    /// </remarks>
    [Fact]
    public void None_of_the_three_outcomes_lands_on_a_recording_whose_save_is_running()
    {
        Recorded("daily", both: true);

        using var saving = SavingMark.Take(Folder("daily"));
        var recording = UnfinishedRecordings.At(Folder("daily"));

        recording.BeingSaved.ShouldBeTrue();
        recording.NothingToDecideYet.ShouldNotBeNull().ShouldContain("its save is running");

        Refuses(() => recording.Keep());
        Refuses(() => recording.Export(Folder("out")));
        Refuses(recording.Discard);

        Folder("daily").Exists.ShouldBeTrue();
        Folder("daily").EnumerateFiles("*.blocks").Count().ShouldBe(2);
        Folder("out").Exists.ShouldBeFalse();

        void Refuses(Action decision)
        {
            var thrown = Should.Throw<AudioCaptureException>(decision);

            thrown.Message.ShouldContain("save");
            thrown.Message.ShouldContain(Folder("daily").FullName);
            thrown.Message.ShouldNotContain(".blocks");
        }
    }

    /// <summary>
    /// ISC-126.2 at the engine. A save that never ended leaves its mark lying in the folder, and
    /// that folder is one all three outcomes are open to again — the recording is not held out of
    /// reach by a file nothing is holding.
    /// </summary>
    /// <remarks>
    /// The mark is really there and is asserted to be before anything is asked, so an engine that
    /// read the mark's existence rather than its handle would fail here rather than pass quietly.
    /// Throwing the recording away is the one of the three that has to survive it: it removes the
    /// folder the stale mark is in, so a delete that could not take the mark with it would leave a
    /// recording nobody could ever be rid of.
    /// </remarks>
    [Fact]
    public void A_mark_left_by_a_save_that_died_leaves_the_recording_decidable()
    {
        Recorded("daily", both: true);
        SavingMark.Take(Folder("daily")).Dispose();

        Marked(Folder("daily"))
            .ShouldBeTrue("the folder has to be carrying the mark for this to be about one");

        var recording = UnfinishedRecordings.At(Folder("daily"));

        recording.BeingSaved.ShouldBeFalse();
        recording.NothingToDecideYet.ShouldBeNull();
        recording.Keep().Count.ShouldBe(2);
        recording.Export(Folder("out")).Count.ShouldBe(2);

        recording.Discard();
        Folder("daily").Exists.ShouldBeFalse();
    }

    /// <summary>
    /// What somebody typed is then a folder rather than a recording, and acting on it as one is
    /// how the wrong directory gets thrown away.
    /// </summary>
    [Fact]
    public void A_folder_holding_no_recording_is_refused_rather_than_decided_about()
    {
        var empty = root.CreateSubdirectory("empty");

        Should.Throw<AudioCaptureException>(() => UnfinishedRecordings.At(empty))
            .Message.ShouldContain("no spool");
        Should.Throw<AudioCaptureException>(() => UnfinishedRecordings.At(Folder("nowhere")))
            .Message.ShouldContain("no folder");
    }

    /// <summary>
    /// ISC-125. A recording's folder is removed in one place, reachable only from a choice about
    /// one recording. The rule is not held by the places that obey it today — it is held by a
    /// fifth one costing a red test on the day it is written.
    /// </summary>
    /// <remarks>
    /// Still one entry after the sweep of folders nothing was recorded into landed, and that is the
    /// point of where it was put: both ways a folder under <c>spool/</c> goes are in this file, so
    /// the rule stays one place rather than a list that grows. <c>Discard</c> takes away a recording
    /// because somebody said to; <c>EraseWhereNothingWasRecorded</c> takes away a folder that never
    /// held one, and refuses on anything that says otherwise. A second entry here is a folder
    /// removal somebody has to argue for.
    /// </remarks>
    [Fact]
    public void Nothing_but_a_decision_about_one_recording_removes_a_folder()
    {
        string[] allowed =
        [
            // The one decision that removes a recording, and the only place either spelling of a
            // folder rename belongs.
            Path.Combine("MeetingTranscriber.Audio", "UnfinishedRecordings.cs"),

            // Not a folder rename at all: `CaptureSource.MoveTo` moves a capture from one device to
            // another. This sweep reads text and cannot see what a receiver's type is, so the one
            // collision is named here rather than the pattern being narrowed until it misses the
            // spelling somebody would actually reach for.
            Path.Combine("MeetingTranscriber.Audio", "CaptureSession.cs"),
        ];

        var offenders = Sources()
            .Where(file => File.ReadAllText(file.FullName) is var text
                && (text.Contains("Directory.Delete", StringComparison.Ordinal)
                    || text.Contains("Delete(recursive", StringComparison.Ordinal)
                    || text.Contains("Directory.Move", StringComparison.Ordinal)
                    || text.Contains(".MoveTo(", StringComparison.Ordinal)))
            .Select(file => Path.GetRelativePath(Tree().FullName, file.FullName))
            .Where(path => !Array.Exists(allowed, one => path.EndsWith(one, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "These remove a folder, and a recording's folder is one. Throwing a recording away is "
            + "somebody's decision about one recording, and there is one place it happens. Both "
            + "spellings of a rename are on this list because a recording's folder moved to a name "
            + "nothing looks in has disappeared as surely as one deleted, and the rename is now "
            + "half of how a removal happens. The ban is deliberately wider than the rule: it "
            + "catches every directory rename in src/ rather than only a recording's, because a "
            + "sweep over text cannot tell which folder a path is, and a rename that is not a "
            + "recording's is cheap to argue for here.");
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
            // The one place a file this product made is taken back when it never became anything:
            // a spool whose stream would not open, a card whose write was cut off, an export that
            // could not be finished. One copy of that rule, so the fifth caller cannot write its
            // own.
            "BlockSpool.cs",

            // The one decision that removes a recording.
            "UnfinishedRecordings.cs",
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

    /// <summary>Changes one byte, the way a disk that did not keep what it was given would.</summary>
    private static void Corrupt(FileInfo file, long at)
    {
        using var stream = file.Open(FileMode.Open, FileAccess.ReadWrite);
        stream.Position = at;
        var was = stream.ReadByte();
        stream.Position = at;
        stream.WriteByte((byte)(was ^ 0xFF));
    }

    /// <summary>Cuts the last block short, the way killing a process mid write cuts one short.</summary>
    private static void CutOffMidBlock(FileInfo blocks)
    {
        using var stream = blocks.Open(FileMode.Open, FileAccess.Write);
        stream.SetLength(stream.Length - 16);
    }

    private DirectoryInfo Folder(string name) => new(Path.Combine(root.FullName, name));

    /// <summary>
    /// The handle a capture holds on its own spool while a meeting is being recorded: writing it,
    /// and letting nothing else write.
    /// </summary>
    private FileStream Recording(AudioChannel channel) => new(
        BlockSpool.FileFor(Folder("daily"), channel).FullName,
        FileMode.Open,
        FileAccess.Write,
        FileShare.Read);

    /// <summary>
    /// The handle a second window keeps on a recording it is reading: another one keeping it, a
    /// prompt exporting it, the same folder open twice.
    /// </summary>
    /// <remarks>
    /// Opened the way <c>SpoolReader.Open</c> opens a spool and the way
    /// <c>BlockSpool.IsStillBeingWritten</c> asks about one, so nothing in this engine can see it —
    /// which is the point, because the removal has to survive a holder it was never told about.
    /// NTFS still refuses to rename the folder over it. Do not reuse
    /// <see cref="Recording(AudioChannel)"/> for this: that one is a writer, and a writer is caught
    /// by <c>EnsureThereIsSomethingToDecide</c> long before anything is removed.
    /// </remarks>
    private FileStream Reading(AudioChannel channel) => new(
        BlockSpool.FileFor(Folder("daily"), channel).FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read);

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
                CaptureMode.FullLoopback,
                [
                    new SpooledSource(AudioChannel.Loopback, "everything this machine plays", null),
                    new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.0}.jabra"),
                ]));
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

    /// <summary>
    /// Whether the mark a save writes is lying in this folder. Built here rather than asked of
    /// <see cref="SavingMark"/>, which deliberately answers nothing about the file being there.
    /// </summary>
    private static bool Marked(DirectoryInfo folder) =>
        File.Exists(Path.Combine(folder.FullName, SavingMark.FileName));

    /// <summary>
    /// Whether the mark a read writes is lying in this folder. Built here rather than asked of
    /// <see cref="ReadingMark"/>, which deliberately answers nothing about the file being there.
    /// </summary>
    private static bool MarkedAsRead(DirectoryInfo folder) =>
        File.Exists(Path.Combine(folder.FullName, ReadingMark.FileName));

    /// <summary>
    /// Every file in the folder by name and by content, which is what "the folder survives with
    /// everything in it" means. A length would pass over a file emptied and refilled.
    /// </summary>
    /// <remarks>
    /// Opened the way a backup opens a file rather than the way <c>File.ReadAllBytes</c> does: one
    /// of these files is a mark something is holding for writing, and a read sharing less than that
    /// would be refused by the very holder the comparison is here to survive.
    /// </remarks>
    private static string[] Snapshot(DirectoryInfo folder) =>
    [
        .. folder.GetFiles()
            .Select(file => $"{file.Name} {Convert.ToHexString(Hashed(file))}")
            .Order(StringComparer.Ordinal),
    ];

    private static byte[] Hashed(FileInfo file)
    {
        using var content = file.Open(
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return SHA256.HashData(content);
    }

}
