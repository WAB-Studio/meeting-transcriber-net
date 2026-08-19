using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// ISC-82, ISC-147 and ISC-148 against a corpus on disk. What the rule says is
/// `MeetingStageTests`; what this adds is the half no pure test can reach — that the answer comes
/// back the same through a connection that never saw the one that wrote it, which is what closing
/// and reopening the application is.
/// </summary>
public class MeetingWorkTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly TimeProvider Clock =
        new FakeClock(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_recorded_meeting_says_what_it_is_owed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        var listed = new MeetingWork(context, Clock).Listed();

        listed.Count.ShouldBe(1);
        listed[0].Meeting.Id.ShouldBe(meeting);
        listed[0].Owed.Stage.ShouldBe(MeetingStage.Recorded);
        listed[0].Owed.Next.ShouldBe(JobKind.Transcribe);
        listed[0].Owed.IsOwed.ShouldBeTrue();
    }

    [Fact]
    public void A_meeting_being_recorded_is_listed_and_offered_nothing()
    {
        // Its row and folder exist before the first sample, so it is in the corpus with no audio
        // under it. It shows, because it is a meeting; it offers nothing, because there is
        // nothing yet to transcribe.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Add(context, NewMeeting(Guid.NewGuid()));

        var listed = new MeetingWork(context, Clock).Listed();

        listed.Count.ShouldBe(1);
        listed[0].Owed.Stage.ShouldBe(MeetingStage.Recording);
        listed[0].Owed.MayBeTaken.ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_on_its_way_out_is_owed_nothing_and_is_not_listed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        var row = context.Meetings.Single(stored => stored.Id == meeting);
        row.LifecycleState = LifecycleState.Deleting;
        row.DeletedAt = Recorded;
        context.SaveChanges();

        new MeetingWork(context, Clock).Listed().ShouldBeEmpty();
    }

    [Fact]
    public void Taking_a_stage_queues_its_work_and_starts_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        var job = new MeetingWork(context, Clock).Take(meeting);

        job.Kind.ShouldBe(JobKind.Transcribe);
        job.State.ShouldBe(JobState.Pending);
        job.Attempt.ShouldBe(0);
        job.StartedAt.ShouldBeNull();
    }

    [Fact]
    public void A_stage_already_taken_is_not_offered_a_second_time()
    {
        // The press that would pay twice. A screen drawn before the first press is exactly the
        // screen that would make it, which is why the answer is re-read rather than trusted.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        work.Take(meeting);

        work.On(meeting).Standing.ShouldBe(StageStanding.Underway);
        work.On(meeting).MayBeTaken.ShouldBeFalse();
        Should.Throw<MeetingStageException>(() => work.Take(meeting));
    }

    [Fact]
    public void A_stage_asked_for_and_not_yet_run_can_be_taken_back()
    {
        // Otherwise the one press that spends money is the only one on the screen with no way
        // back, which is exactly backwards. Nothing has run, so nothing has been paid for.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        var queued = work.Take(meeting);
        var left = work.Decline(meeting);

        left.Id.ShouldBe(queued.Id);
        left.State.ShouldBe(JobState.Cancelled);

        // The same row taken back rather than a second one written over the top of it: a queue
        // nothing drains would otherwise keep the first job forever.
        context.ProcessingJobs.Count(job => job.MeetingId == meeting).ShouldBe(1);

        var owed = work.On(meeting);
        owed.Standing.ShouldBe(StageStanding.Declined);
        owed.MayBeTaken.ShouldBeTrue();
    }

    [Fact]
    public void Declining_a_stage_leaves_the_meeting_where_it_was_with_the_same_action()
    {
        // ISC-147, as far as one connection can show it. The stage has not moved and the button
        // is there; what changed is that the application is no longer waiting to be told.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        var declined = work.Decline(meeting);

        declined.State.ShouldBe(JobState.Cancelled);

        var owed = work.On(meeting);
        owed.Stage.ShouldBe(MeetingStage.Recorded);
        owed.Next.ShouldBe(JobKind.Transcribe);
        owed.Standing.ShouldBe(StageStanding.Declined);
        owed.MayBeTaken.ShouldBeTrue();
        owed.IsOwed.ShouldBeFalse();
    }

    [Fact]
    public void A_stage_declined_can_be_taken_later()
    {
        // ISC-147 the whole way. One ignored today is transcribed next month, and the two answers
        // are two rows against the same meeting and the same kind — which is what the key has to
        // survive.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        work.Decline(meeting);
        var taken = work.Take(meeting);

        taken.State.ShouldBe(JobState.Pending);
        context.ProcessingJobs.Count(job => job.MeetingId == meeting && job.Kind == JobKind.Transcribe)
            .ShouldBe(2);
        work.On(meeting).Standing.ShouldBe(StageStanding.Underway);
    }

    [Fact]
    public void Declining_twice_says_nothing_new_and_writes_nothing_new()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        var first = work.Decline(meeting);
        var again = work.Decline(meeting);

        again.Id.ShouldBe(first.Id);
        context.ProcessingJobs.Count(job => job.MeetingId == meeting).ShouldBe(1);
    }

    [Fact]
    public void What_a_meeting_is_waiting_for_is_the_same_after_the_application_is_reopened()
    {
        // ISC-148. Every meeting is read back through a connection that never saw the one that
        // answered, which is the whole of what closing and opening the application is here.
        using var corpus = new TemporaryCorpus();
        Guid untouched;
        Guid declined;
        Guid taken;

        using (var writing = corpus.OpenMigrated())
        {
            var work = new MeetingWork(writing, Clock);
            untouched = Record(writing);
            declined = Record(writing);
            taken = Record(writing);

            work.Decline(declined);
            work.Take(taken);
        }

        using var reading = corpus.Open();
        var reopened = new MeetingWork(reading, Clock);

        reopened.On(untouched).Standing.ShouldBe(StageStanding.Offered);
        reopened.On(untouched).IsOwed.ShouldBeTrue();

        reopened.On(declined).Standing.ShouldBe(StageStanding.Declined);
        reopened.On(declined).Stage.ShouldBe(MeetingStage.Recorded);
        reopened.On(declined).Next.ShouldBe(JobKind.Transcribe);
        reopened.On(declined).IsOwed.ShouldBeFalse();

        reopened.On(taken).Standing.ShouldBe(StageStanding.Underway);
        reopened.On(taken).MayBeTaken.ShouldBeFalse();
    }

    [Fact]
    public void A_transcribed_meeting_reopens_owed_a_summary()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting;

        using (var writing = corpus.OpenMigrated())
        {
            meeting = Record(writing);
            Add(writing, NewArtifact(meeting, ArtifactKind.DeepgramResponse));
        }

        using var reading = corpus.Open();
        var owed = new MeetingWork(reading, Clock).On(meeting);

        owed.Stage.ShouldBe(MeetingStage.Transcribed);
        owed.Next.ShouldBe(JobKind.Extract);
    }

    [Fact]
    public void A_meeting_this_corpus_does_not_hold_is_said_so_rather_than_answered_for()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<MeetingStageException>(() => new MeetingWork(context, Clock).On(Guid.NewGuid()));
    }

    [Fact]
    public void A_meeting_with_an_unsettled_charge_offers_nothing_and_says_why()
    {
        // Nothing in the application settles one of these yet, so the meetings list is the only
        // place it shows at all. The one thing that must not happen is the button being there.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        var uncertain = work.Take(meeting);
        uncertain.Start(UtcTimestamp.From(Clock.GetUtcNow()));
        uncertain.RecoverAfterRestart().ShouldBeTrue();
        context.SaveChanges();

        var owed = work.On(meeting);
        owed.Standing.ShouldBe(StageStanding.StoppedOnAPerson);
        owed.WaitsOnSomebody.ShouldBeTrue();
        owed.MayBeTaken.ShouldBeFalse();
        owed.MayBeLeft.ShouldBeFalse();

        Should.Throw<MeetingStageException>(() => work.Take(meeting));
        Should.Throw<MeetingStageException>(() => work.Decline(meeting));
    }

    [Fact]
    public void One_meetings_answer_is_never_read_off_anothers()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var mine = Record(context);
        var theirs = Record(context);
        var work = new MeetingWork(context, Clock);

        work.Decline(theirs);

        work.On(mine).Standing.ShouldBe(StageStanding.Offered);
        work.On(theirs).Standing.ShouldBe(StageStanding.Declined);
    }

    private static Guid Record(CorpusDbContext context)
    {
        var meeting = Guid.NewGuid();
        Add(context, NewMeeting(meeting));
        Add(context, NewArtifact(meeting, ArtifactKind.Audio));
        return meeting;
    }

    private static void Add(CorpusDbContext context, object row)
    {
        context.Add(row);
        context.SaveChanges();
    }

    private static Meeting NewMeeting(Guid id) => new()
    {
        Id = id,
        StartedAt = Recorded,
        SourceProfile = SourceProfile.Multichannel,
        Language = "es",
        CreatedAt = Recorded,
        UpdatedAt = Recorded,
    };

    private static Artifact NewArtifact(Guid meeting, ArtifactKind kind) => new()
    {
        Id = Guid.NewGuid(),
        MeetingId = meeting,
        Kind = kind,
        Origin = kind.OriginOf(),
        RelativePath = $"meetings/{meeting}/{kind}",
        ByteSize = 1024,
        Sha256 = new string('a', 64),
        ConfirmedAt = Recorded,
    };

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
