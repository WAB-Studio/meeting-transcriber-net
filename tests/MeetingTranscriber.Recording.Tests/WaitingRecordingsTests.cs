using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using NAudio.Wave;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// What a start finds waiting after the application was killed in the middle of a meeting, and
/// what each of the three choices does about it.
/// </summary>
/// <remarks>
/// Killing the process is fabricated the way it actually lands on disk: the spools are written the
/// way a capture writes them and then cut off inside the block that was being written, which is
/// the file a machine that died leaves. Nothing here opens a device, so all of it runs on a build
/// agent with no sound card — what still needs a machine is the hand probe in the card's evidence.
/// </remarks>
public sealed class WaitingRecordingsTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp recordedAt = UtcTimestamp.Parse("2026-08-18T09:30:00.000Z");
    private readonly UtcTimestamp openedAgainAt = UtcTimestamp.Parse("2026-08-18T11:05:00.000Z");

    /// <summary>
    /// ISC-79. The whole path, in the order it happens to somebody: a meeting is being recorded,
    /// the process dies inside a block, and the next start finds it, is told what it is, and turns
    /// it into a meeting that plays.
    /// </summary>
    [Fact]
    public void A_recording_the_process_was_killed_in_the_middle_of_becomes_a_meeting_that_plays()
    {
        Guid recorded;
        using (var recording = corpus.OpenMigrated())
        {
            recorded = Killed(recording, seconds: 3).MeetingId;
        }

        // A different connection entirely: what a start after a crash reads is the corpus on disk,
        // not a context that watched the recording happen.
        using var started = corpus.Open();

        var waiting = WaitingRecordings.In(started);
        waiting.Count.ShouldBe(1);
        waiting[0].MeetingId.ShouldBe(recorded);
        waiting[0].Meeting.ShouldNotBeNull().Duration.ShouldBeNull();
        waiting[0].Running.ShouldBeFalse();
        waiting[0].Bytes.ShouldBeGreaterThan(0);
        waiting[0].Unrecoverable.ShouldBeNull();

        // What survived, read on the one recording somebody asked about. The microphone really was
        // cut off inside a block, so a build that read the tail as audio rather than dropping it
        // would report nothing discarded here — and would put invented samples in the meeting.
        var survived = waiting[0].Spooled.Keep();
        survived.Count.ShouldBe(CapturedAudio.ChannelCount);
        survived.Single(source => source.Channel == AudioChannel.Microphone).Discarded.ShouldBeGreaterThan(0);
        survived.Single(source => source.Channel == AudioChannel.Loopback).Discarded.ShouldBe(0);
        survived.ShouldAllBe(source => source.Blocks > 0);

        var finished = WaitingRecordings.Recover(started, waiting[0], openedAgainAt);

        // Three seconds of meeting, minus at most the one block nobody finished writing.
        finished.MeetingId.ShouldBe(recorded);
        finished.Length.Milliseconds.ShouldBeInRange(2_900, 3_050);

        var audio = CorpusFiles.Locate(corpus.Root, finished.Audio.RelativePath);
        finished.Audio.RelativePath.ShouldBe($"meetings/{recorded}/audio.wav");
        audio.Exists.ShouldBeTrue();
        finished.Audio.Sha256.ShouldBe(CorpusFiles.Sha256Of(audio));

        // Playable, and not merely present: the bytes open as a WAV of the format every meeting is
        // in, and hold as many frames as the meeting was long.
        using var played = new WaveFileReader(audio.FullName);
        StreamFormat.Of(played.WaveFormat).ShouldBe(MeetingAudio.Interchange);
        played.TotalTime.TotalMilliseconds.ShouldBeInRange(2_900, 3_050);

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Duration.ShouldBe(finished.Length);

        // The run says it came back from a spool rather than having been stopped, and is closed
        // off at the moment somebody recovered it.
        var run = reopened.CaptureRuns.Single();
        run.Recovered.ShouldBeTrue();
        run.FinishedAt.ShouldBe(openedAgainAt);

        // And it is not waiting any more: the meeting is made, so the next start offers nothing.
        WaitingRecordings.In(reopened).ShouldBeEmpty();
    }

    /// <summary>
    /// Killed before the row describing the run was committed. The card beside the blocks is the
    /// only account of which devices caught this meeting, and recovering writes the row from it —
    /// which is what the card carries a run id for.
    /// </summary>
    [Fact]
    public void A_run_the_corpus_never_got_is_written_again_from_the_card()
    {
        Guid recorded;
        Guid ran;
        using (var recording = corpus.OpenMigrated())
        {
            var killed = Killed(recording, seconds: 2, tellTheCorpusItBegan: false);
            recorded = killed.MeetingId;
            ran = killed.CaptureRunId;
            recording.CaptureRuns.ShouldBeEmpty();
        }

        using var started = corpus.Open();
        WaitingRecordings.Recover(started, WaitingRecordings.In(started).Single(), openedAgainAt);

        using var reopened = corpus.Open();
        var run = reopened.CaptureRuns.Single();

        run.Id.ShouldBe(ran);
        run.MeetingId.ShouldBe(recorded);
        run.StartedAt.ShouldBe(recordedAt);
        run.MeDeviceName.ShouldBe("Headset");
        run.OthersDeviceName.ShouldBe("Speakers");
        run.Recovered.ShouldBeTrue();
        run.FinishedAt.ShouldBe(openedAgainAt);
    }

    /// <summary>
    /// ISC-149. Two recordings sitting undecided, and a new meeting recorded from start to finish
    /// over the top of them — with both still there, byte for byte, afterwards.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Nothing refuses to record while something waits, and nothing recording
    /// touches what waits: a build that cleared the way by tidying them up would pass the first
    /// assertion and lose two meetings.
    /// </remarks>
    [Fact]
    public void A_recording_waiting_to_be_decided_about_never_keeps_a_new_meeting_from_being_recorded()
    {
        using var context = corpus.OpenMigrated();

        var first = Killed(context, seconds: 2).MeetingId;
        var second = Killed(context, seconds: 3).MeetingId;

        var untouched = OnDisk(first, second);
        WaitingRecordings.In(context).Select(recording => recording.MeetingId).ShouldBe([first, second], ignoreOrder: true);

        // A whole meeting, start to finish, while both of those are waiting for somebody.
        var fresh = MeetingRecordings.Open(context, "es", openedAgainAt);
        Fabricated.Spools(fresh.Spool, seconds: 2);
        var finished = MeetingRecordings.Finish(context, fresh.MeetingId, openedAgainAt);

        finished.Length.Milliseconds.ShouldBeInRange(1_950, 2_050);
        CorpusFiles.Locate(corpus.Root, finished.Audio.RelativePath).Exists.ShouldBeTrue();

        // Neither was decided about, and neither was touched — not by the listing, not by the
        // recording, and not by there having been a start in between.
        OnDisk(first, second).ShouldBe(untouched);
        WaitingRecordings.In(context).Select(recording => recording.MeetingId).ShouldBe([first, second], ignoreOrder: true);
    }

    /// <summary>
    /// A meeting that was stopped is not waiting for anybody. Its blocks are still on disk — the
    /// corpus is the only thing that can tell that folder from one nobody got to stop, and a list
    /// built from the folders alone would offer somebody every meeting they ever recorded.
    /// </summary>
    [Fact]
    public void A_meeting_that_was_stopped_is_not_offered_as_something_to_decide_about()
    {
        using var context = corpus.OpenMigrated();

        var stopped = MeetingRecordings.Open(context, "es", recordedAt);
        Fabricated.Spools(stopped.Spool, seconds: 2);
        MeetingRecordings.Finish(context, stopped.MeetingId, recordedAt);

        var killed = Killed(context, seconds: 2).MeetingId;

        // The blocks of the stopped one are exactly where they were: this is a list that leaves it
        // out, never a start that cleaned it up.
        BlockSpool.FileFor(stopped.Spool, AudioChannel.Loopback).Exists.ShouldBeTrue();

        WaitingRecordings.In(context).Select(recording => recording.MeetingId).ShouldBe([killed]);
    }

    /// <summary>
    /// A meeting still being recorded is on the list and says why none of the three is open to it.
    /// Leaving it out would hide the meeting somebody is in the middle of; offering to recover it
    /// would read a file that is still growing.
    /// </summary>
    [Fact]
    public void A_recording_still_going_on_is_listed_and_refuses_to_be_decided_about()
    {
        using var context = corpus.OpenMigrated();
        var prepared = MeetingRecordings.Open(context, "es", recordedAt);
        Fabricated.Spools(prepared.Spool, seconds: 1);

        // A capture holds its spools open for as long as the meeting lasts, which is what says on
        // this machine that a meeting is in progress.
        using var held = BlockSpool.FileFor(prepared.Spool, AudioChannel.Microphone)
            .Open(FileMode.Open, FileAccess.Write, FileShare.None);

        var waiting = WaitingRecordings.In(context).Single();

        waiting.MeetingId.ShouldBe(prepared.MeetingId);
        waiting.Running.ShouldBeTrue();
        waiting.Unrecoverable.ShouldNotBeNull().ShouldContain("still being recorded");

        Should.Throw<RecordingException>(() => WaitingRecordings.Recover(context, waiting, openedAgainAt));
    }

    /// <summary>
    /// A recording of a meeting this corpus never heard of — the database restored from a backup
    /// older than the meeting, or replaced — is still listed, and still has two of the three
    /// choices. What it cannot be is filed against a meeting that does not exist.
    /// </summary>
    [Fact]
    public void A_recording_of_a_meeting_the_corpus_has_no_row_for_is_still_offered()
    {
        Guid recorded;
        using (var recording = corpus.OpenMigrated())
        {
            recorded = Killed(recording, seconds: 2).MeetingId;
            recording.Meetings.RemoveRange(recording.Meetings);
            recording.SaveChanges();
        }

        using var started = corpus.Open();
        var waiting = WaitingRecordings.In(started).Single();

        waiting.MeetingId.ShouldBe(recorded);
        waiting.Meeting.ShouldBeNull();
        waiting.Unrecoverable.ShouldNotBeNull().ShouldContain(recorded.ToString());
        Should.Throw<RecordingException>(() => WaitingRecordings.Recover(started, waiting, openedAgainAt));

        // The audio is still somebody's to take out, which is the whole reason it is on the list.
        var taken = waiting.Spooled.Export(new DirectoryInfo(Path.Combine(corpus.Root.FullName, "taken-out")));
        taken.Count.ShouldBe(CapturedAudio.ChannelCount);
        taken.ShouldAllBe(source => source.Wav.Exists && source.Blocks > 0);
    }

    /// <summary>
    /// Throwing one away is the only thing that removes it, and it removes that one and nothing
    /// else. What is being guarded is a discard that reached past the folder it was given.
    /// </summary>
    [Fact]
    public void Throwing_one_away_removes_that_recording_and_leaves_the_others()
    {
        using var context = corpus.OpenMigrated();

        var thrown = Killed(context, seconds: 2).MeetingId;
        var kept = Killed(context, seconds: 2).MeetingId;

        var waiting = WaitingRecordings.In(context).Single(recording => recording.MeetingId == thrown);
        waiting.Spooled.Discard();

        WaitingRecordings.In(context).Select(recording => recording.MeetingId).ShouldBe([kept]);

        // The row stays. It is the only record that the meeting was attempted at all, and throwing
        // a recording away is a decision about the blocks and not about the meeting.
        context.Meetings.Count().ShouldBe(2);
    }

    /// <summary>
    /// A finish that was cut off between filing the audio and writing the meeting's length. The
    /// audio row is there and the length is not, so the recording is still waiting — and finishing
    /// it again completes it rather than being refused for rewriting audio that is never rewritten.
    /// </summary>
    [Fact]
    public void A_finish_that_was_cut_off_after_filing_the_audio_is_still_waiting_and_completes()
    {
        Guid recorded;
        using (var recording = corpus.OpenMigrated())
        {
            recorded = Killed(recording, seconds: 2).MeetingId;
            WaitingRecordings.Recover(recording, WaitingRecordings.In(recording).Single(), openedAgainAt);

            // The corpus as a machine that died in the window would have left it: the audio filed,
            // the length never written, the run never closed.
            var meeting = recording.Meetings.Single();
            var run = recording.CaptureRuns.Single();
            meeting.Duration = null;
            run.FinishedAt = null;
            run.Recovered = false;
            recording.SaveChanges();
        }

        using var started = corpus.Open();
        var waiting = WaitingRecordings.In(started);

        // Judged on the length and not on the audio row, so the meeting nothing would ever come
        // back to is the one thing this list must not hide.
        waiting.Count.ShouldBe(1);
        waiting[0].MeetingId.ShouldBe(recorded);
        waiting[0].Unrecoverable.ShouldBeNull();

        var finished = WaitingRecordings.Recover(started, waiting[0], openedAgainAt);
        finished.Length.Milliseconds.ShouldBeInRange(1_900, 2_050);

        using var reopened = corpus.Open();
        reopened.Meetings.Single().Duration.ShouldBe(finished.Length);
        reopened.CaptureRuns.Single().FinishedAt.ShouldBe(openedAgainAt);

        // One audio artifact, not two, and it is the one that was already filed.
        reopened.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.Audio).ShouldBe(1);
        WaitingRecordings.In(reopened).ShouldBeEmpty();
    }

    /// <summary>
    /// A folder whose card was torn in half cannot become a meeting — the finish reads that card
    /// again — so the list says so rather than offering a choice that throws.
    /// </summary>
    [Fact]
    public void A_recording_whose_card_cannot_be_read_says_so_instead_of_offering_to_be_kept()
    {
        using var context = corpus.OpenMigrated();
        var recorded = Killed(context, seconds: 2).MeetingId;

        // Half a sentence of JSON, which is what a card written at the instant a machine dies is.
        var card = SpoolManifest.In(CorpusFiles.SpoolFolderFor(corpus.Root, recorded));
        File.WriteAllText(card.FullName, "{ \"meeting\": \"" + recorded);

        var waiting = WaitingRecordings.In(context).Single();

        waiting.MeetingId.ShouldBe(recorded);
        waiting.Unrecoverable.ShouldNotBeNull().ShouldContain("cannot be read");
        Should.Throw<RecordingException>(() => WaitingRecordings.Recover(context, waiting, openedAgainAt));

        // And it is still somebody's to take out or throw away, which is why it is on the list.
        waiting.Spooled.Export(new DirectoryInfo(Path.Combine(corpus.Root.FullName, "taken-out")))
            .Count.ShouldBe(CapturedAudio.ChannelCount);
    }

    /// <summary>
    /// The recording is read through for what somebody decides on: how long it is, and what each
    /// source held down to the block the machine died inside.
    /// </summary>
    [Fact]
    public void A_recording_says_how_long_it_is_and_what_survived_in_each_source()
    {
        using var context = corpus.OpenMigrated();
        Killed(context, seconds: 3);

        var survived = WaitingRecordings.In(context).Single().Read();

        survived.Length.Milliseconds.ShouldBeInRange(2_900, 3_050);
        survived.Sources.Count.ShouldBe(CapturedAudio.ChannelCount);
        survived.Sources.ShouldAllBe(source => source.Blocks > 0);
        survived.Sources.Single(source => source.Channel == AudioChannel.Microphone)
            .Discarded.ShouldBeGreaterThan(0);
        survived.Sources.Single(source => source.Channel == AudioChannel.Loopback)
            .Discarded.ShouldBe(0);
    }

    /// <summary>
    /// A recovery that could not read the blocks leaves the run saying nothing came back from a
    /// spool, because nothing did. A run marked recovered over a recording still sitting there
    /// would be the corpus claiming a meeting somebody has yet to get.
    /// </summary>
    [Fact]
    public void A_recovery_that_failed_leaves_the_run_saying_it_was_never_recovered()
    {
        using var context = corpus.OpenMigrated();
        var recorded = Killed(context, seconds: 2).MeetingId;
        var waiting = WaitingRecordings.In(context).Single();

        // One source's file is no longer a spool, which is a folder the recording cannot be made
        // out of — and the failure arrives after the run row has already been found.
        Fabricated.NoLongerASpool(BlockSpool.FileFor(waiting.Folder, AudioChannel.Loopback));

        Should.Throw<AudioCaptureException>(
            () => WaitingRecordings.Recover(context, waiting, openedAgainAt));

        using var reopened = corpus.Open();
        reopened.CaptureRuns.Single().Recovered.ShouldBeFalse();
        reopened.Meetings.Single(meeting => meeting.Id == recorded).Duration.ShouldBeNull();
        WaitingRecordings.In(reopened).Count.ShouldBe(1);
    }

    public void Dispose() => corpus.Dispose();

    /// <summary>
    /// A meeting recorded up to the moment the machine died: the row, the folder, the card, the
    /// row describing the run, whole blocks, and a last one cut off inside itself.
    /// </summary>
    private SpoolCard Killed(CorpusDbContext context, double seconds, bool tellTheCorpusItBegan = true)
    {
        var prepared = MeetingRecordings.Open(context, "es", recordedAt);
        var card = Fabricated.CardFor(prepared.MeetingId, recordedAt);

        SpoolManifest.Write(prepared.Spool, card);
        if (tellTheCorpusItBegan)
        {
            MeetingRecordings.Began(context, card);
        }

        Fabricated.Spools(prepared.Spool, seconds);
        Fabricated.KilledMidBlock(BlockSpool.FileFor(prepared.Spool, AudioChannel.Microphone), inside: 700);

        return card;
    }

    /// <summary>
    /// Every file of these recordings and what it holds, as something two moments apart can be
    /// compared on. A name and a length would miss a rewrite of the same size.
    /// </summary>
    private string[] OnDisk(params Guid[] meetings) =>
    [
        .. meetings
            .Select(meeting => CorpusFiles.SpoolFolderFor(corpus.Root, meeting))
            .SelectMany(folder => folder.GetFiles())
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .Select(file => $"{file.FullName} {CorpusFiles.Sha256Of(file)}"),
    ];
}
