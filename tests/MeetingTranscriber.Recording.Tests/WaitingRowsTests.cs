using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-79.1, ISC-79.3 and ISC-126.1 on the half that decides them: which recordings a list offers,
/// in what order, and which of the two answers each of them takes.
/// </summary>
/// <remarks>
/// A screen is what a person sees this on, and no probe here opens one — a WinUI tree needs a UI
/// thread and a packaged host. What runs here is everything that decides what such a screen has to
/// draw, over corpora built on a machine with no sound card: the spools are written the way a
/// capture writes them and cut off inside the block that was being written, which is the folder a
/// machine that died leaves.
/// </remarks>
public sealed class WaitingRowsTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp recordedAt = UtcTimestamp.Parse("2026-08-18T09:30:00.000Z");

    /// <summary>
    /// ISC-79.1. A recording the application never finished is one somebody answers, and both
    /// answers are open to it.
    /// </summary>
    [Fact]
    public void A_recording_the_application_never_finished_is_kept_or_thrown_away()
    {
        using var context = corpus.OpenMigrated();
        Killed(context, recordedAt);

        var row = Rows(context).ShouldHaveSingleItem();

        row.Standing.ShouldBe(WaitingStanding.Waiting);
        row.WaitsOnSomebody.ShouldBeTrue();
        row.Allows(WaitingAnswer.Keep).ShouldBeTrue();
        row.Allows(WaitingAnswer.Discard).ShouldBeTrue();
    }

    /// <summary>
    /// ISC-79.1. The order is the statement: the newest recording somebody has to answer for is
    /// the one at the top, which is the order the meetings under it are already in.
    /// </summary>
    [Fact]
    public void The_recordings_somebody_has_to_answer_for_go_newest_first()
    {
        using var context = corpus.OpenMigrated();
        var earlier = Killed(context, recordedAt);
        var later = Killed(context, recordedAt + Duration.FromMilliseconds(3_600_000));

        Rows(context).Select(row => row.Recording.MeetingId)
            .ShouldBe([later.MeetingId, earlier.MeetingId]);
    }

    /// <summary>
    /// ISC-126, on the surface this card adds. A meeting still being recorded is on the list and
    /// says so rather than being left off it — the meeting somebody is in the middle of is the last
    /// thing to hide — and it takes neither answer.
    /// </summary>
    [Fact]
    public void A_meeting_still_being_recorded_is_on_the_list_and_offers_nothing()
    {
        using var context = corpus.OpenMigrated();
        var abandoned = Killed(context, recordedAt);
        var running = Killed(context, recordedAt + Duration.FromMilliseconds(60_000));

        // A capture holding its blocks, which is what a meeting in progress is on this machine.
        using var held = BlockSpool.FileFor(Spool(running.MeetingId), AudioChannel.Loopback)
            .Open(FileMode.Open, FileAccess.Read, FileShare.None);

        var rows = Rows(context);

        rows[0].Recording.MeetingId.ShouldBe(running.MeetingId);
        rows[0].Standing.ShouldBe(WaitingStanding.StillBeingRecorded);
        rows[0].WaitsOnSomebody.ShouldBeFalse();
        rows[0].Allows(WaitingAnswer.Keep).ShouldBeFalse();
        rows[0].Allows(WaitingAnswer.Discard).ShouldBeFalse();

        // Above the one waiting, although it started later — the order says which is which, and
        // the recording that started an hour earlier is the one with a question on it.
        rows[1].Recording.MeetingId.ShouldBe(abandoned.MeetingId);
        rows[1].Standing.ShouldBe(WaitingStanding.Waiting);
    }

    /// <summary>
    /// ISC-126.1. A meeting whose save is running is never offered as one to decide about, and it
    /// is the recorder that says which one it is: on disk that meeting is a row with no length and
    /// blocks nothing is holding, which is exactly what a recording nobody finished looks like.
    /// </summary>
    [Fact]
    public void A_meeting_whose_save_is_running_is_never_offered_as_one_to_decide_about()
    {
        using var context = corpus.OpenMigrated();
        var saving = Killed(context, recordedAt);

        var told = WaitingRows.Of(WaitingRecordings.In(context), saving.MeetingId)
            .ShouldHaveSingleItem();

        told.Standing.ShouldBe(WaitingStanding.BeingSavedNow);
        told.WaitsOnSomebody.ShouldBeFalse();
        told.Allows(WaitingAnswer.Keep).ShouldBeFalse();
        told.Allows(WaitingAnswer.Discard).ShouldBeFalse();

        // And nothing on disk says it, which is why the recorder has to. Told nothing is being
        // saved, the same corpus offers the same recording both answers — so a save marked
        // anywhere the corpus can see it would leave this meeting held out of reach by something
        // no restart lifts.
        Rows(context).ShouldHaveSingleItem().Standing.ShouldBe(WaitingStanding.Waiting);
    }

    /// <summary>
    /// ISC-79.3. A recording that cannot be made into the meeting it was of is never offered as
    /// one to keep, and throwing it away is still open — a recording nothing can be made of is
    /// still somebody's to be rid of.
    /// </summary>
    [Fact]
    public void A_recording_that_cannot_become_a_meeting_is_only_ever_thrown_away()
    {
        using var context = corpus.OpenMigrated();
        var orphan = Guid.NewGuid();
        Fabricated.Spools(CorpusFiles.SpoolFolderFor(corpus.Root, orphan), seconds: 1);

        var row = Rows(context).ShouldHaveSingleItem();

        row.Recording.MeetingId.ShouldBe(orphan);
        row.Standing.ShouldBe(WaitingStanding.CannotBecomeAMeeting);
        row.WaitsOnSomebody.ShouldBeTrue();
        row.Allows(WaitingAnswer.Keep).ShouldBeFalse();
        row.Allows(WaitingAnswer.Discard).ShouldBeTrue();

        // Said, and said as what was observed before what it means, so a row can carry the reason
        // instead of a person pressing to find it out.
        row.Recording.Unrecoverable.ShouldNotBeNull().ShouldContain(orphan.ToString());
    }

    /// <summary>
    /// Only the two the person is deciding on have their blocks read through. Saying how long a
    /// recording is costs a pass over every byte of it, which for two hours of meeting is a few
    /// hundred megabytes a source — and neither of the other two is a recording anybody is
    /// deciding on.
    /// </summary>
    [Fact]
    public void Only_a_recording_somebody_has_to_answer_for_is_read_through()
    {
        using var context = corpus.OpenMigrated();
        var saving = Killed(context, recordedAt);

        WaitingRows.Of(WaitingRecordings.In(context), saving.MeetingId)
            .ShouldHaveSingleItem().MayBeReadThrough.ShouldBeFalse();

        var waiting = Rows(context).ShouldHaveSingleItem();
        waiting.MayBeReadThrough.ShouldBeTrue();

        // And what the read answers is what a row shows: how long it turned out to be.
        waiting.Recording.Read().Length.Milliseconds.ShouldBeInRange(900, 1_050);
    }

    /// <summary>
    /// A standing no list offers is refused rather than answered. Unreachable while the standings
    /// are the whole enum, and the refusal is what says so out loud instead of handing back an
    /// answer worked out from a number that means nothing.
    /// </summary>
    [Fact]
    public void A_standing_nothing_answers_for_is_refused()
    {
        using var context = corpus.OpenMigrated();
        Killed(context, recordedAt);

        var row = Rows(context).ShouldHaveSingleItem() with { Standing = (WaitingStanding)99 };

        Should.Throw<RecordingException>(() => row.Allows(WaitingAnswer.Keep));
        Should.Throw<RecordingException>(() => row.Allows(WaitingAnswer.Discard));
    }

    public void Dispose() => corpus.Dispose();

    /// <summary>Every waiting recording as the list shows it, with nothing being saved.</summary>
    private static IReadOnlyList<WaitingRow> Rows(CorpusDbContext context) =>
        WaitingRows.Of(WaitingRecordings.In(context), beingSavedNow: null);

    private DirectoryInfo Spool(Guid meeting) => CorpusFiles.SpoolFolderFor(corpus.Root, meeting);

    /// <summary>
    /// A meeting recorded up to the moment the machine died: the row, the folder, the card, the
    /// row describing the run, whole blocks, and a last one cut off inside itself.
    /// </summary>
    private SpoolCard Killed(CorpusDbContext context, UtcTimestamp startedAt)
    {
        var prepared = MeetingRecordings.Open(context, "es", startedAt);
        var card = Fabricated.CardFor(prepared.MeetingId, startedAt);

        SpoolManifest.Write(prepared.Spool, card);
        MeetingRecordings.Began(context, card);

        Fabricated.Spools(prepared.Spool, seconds: 1);
        Fabricated.KilledMidBlock(
            BlockSpool.FileFor(prepared.Spool, AudioChannel.Microphone), inside: 700);

        return card;
    }
}
