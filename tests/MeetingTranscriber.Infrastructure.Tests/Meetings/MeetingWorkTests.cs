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
/// and reopening the application is — and the order it comes back in, which is a promise about
/// something SQLite is otherwise free to decide either way.
/// </summary>
public class MeetingWorkTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    /// <summary>An hour after <see cref="Recorded"/>, and the only instant on the list that differs.</summary>
    private static readonly UtcTimestamp Later =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Two meetings that started in the same millisecond, in ascending id, and one that started an
    /// hour later whose id sorts after both. Fixed rather than <see cref="Guid.NewGuid"/>, because
    /// what is claimed about them is an order and a random id has none — and the later meeting's
    /// id sorting last is what keeps a list that lost the instant from being accidentally right.
    /// </summary>
    private static readonly Guid Tied = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AlsoTied = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid Newest = Guid.Parse("33333333-3333-3333-3333-333333333333");

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Meetings_are_listed_newest_first_and_ties_by_id_however_they_were_written(
        bool newestWrittenFirst)
    {
        // The order `MeetingsWatch` depends on, said the only way SQLite cannot pass by accident.
        // Two meetings that started in the same millisecond leave the engine free to answer in
        // either order, and what it does is read off the file: `ORDER BY started_at DESC` over
        // `ix_meetings_started_at` is cheapest as a reverse scan of that index, which — being
        // non-unique on a rowid table, so carrying the rowid as its implicit last key column —
        // hands ties back in descending insertion order. A sorter over a full scan hands them
        // back ascending instead. So the claim is made twice over two corpora that differ in
        // nothing but the order the rows were written: whichever way the engine leans, one of the
        // two is the way round that only the tie-breaker corrects.
        //
        // Those two corpora are two different files only because `Add` below commits one row per
        // call. Batching the three into a single `SaveChanges` would hand the insert order to EF,
        // which orders by entity type rather than by call, and would collapse both cases onto one
        // physical order — leaving a theory that is green whether or not the tie-breaker is there.
        //
        // `Newest` is here for the other half: with `.OrderByDescending` gone, id alone puts it
        // last instead of first and both cases fall.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        if (newestWrittenFirst)
        {
            Add(context, NewMeeting(Newest, Later));
            Add(context, NewMeeting(AlsoTied, Recorded));
            Add(context, NewMeeting(Tied, Recorded));
        }
        else
        {
            Add(context, NewMeeting(Tied, Recorded));
            Add(context, NewMeeting(AlsoTied, Recorded));
            Add(context, NewMeeting(Newest, Later));
        }

        new MeetingWork(context, Clock).Listed()
            .Select(listed => listed.Meeting.Id)
            .ShouldBe([Newest, Tied, AlsoTied]);
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
    public void A_meeting_on_its_way_out_stopped_on_a_person_is_listed_and_offers_nothing()
    {
        // The card's third sentence held against the exclusion above it. Somebody asks to delete a
        // meeting whose transcription a restart left unsettled: there may be a charge that already
        // happened, nobody has established whether there was, and this list is the only place that
        // says which meeting it was. Dropping it would make the deletion the thing that hid the
        // charge.
        //
        // And the exclusion loses nothing, which is why both rules can be kept whole: this meeting
        // comes back with neither answer on offer, so nothing is ever offered on a meeting somebody
        // asked to get rid of. That pair is the last two assertions, and they are the ones that
        // would have to go red for this to be a hole rather than an exception. What the standing
        // itself means is `MeetingStageTests`' and the unsettled-charge test below — not repeated
        // here, which leaves this test holding only what is new: the lifecycle, and that it shows.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);
        var work = new MeetingWork(context, Clock);

        var uncertain = work.Take(meeting);
        uncertain.Start(UtcTimestamp.From(Clock.GetUtcNow()));
        uncertain.RecoverAfterRestart().ShouldBeTrue();

        var row = context.Meetings.Single(stored => stored.Id == meeting);
        row.LifecycleState = LifecycleState.Deleting;
        row.DeletedAt = Recorded;
        context.SaveChanges();

        var listed = work.Listed();

        listed.Count.ShouldBe(1);
        listed[0].Meeting.Id.ShouldBe(meeting);
        listed[0].Owed.WaitsOnSomebody.ShouldBeTrue();

        listed[0].Owed.MayBeTaken.ShouldBeFalse();
        listed[0].Owed.MayBeLeft.ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_on_its_way_out_is_listed_for_a_charge_its_own_stage_says_nothing_about()
    {
        // The composition the list performs, which neither half proves alone: the database
        // narrows on a job row and the rule decides on the answer. A capture a restart stopped is
        // not the transcription this meeting's stage would offer, so either side tidied to read
        // only the stage's own kind drops it — and it is a meeting on its way out, so nothing
        // else will ever mention it again.
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context);

        var interrupted = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Capture, $"{meeting}/capture-1", Recorded);
        interrupted.Start(Recorded);
        interrupted.RecoverAfterRestart().ShouldBeTrue();
        Add(context, interrupted);

        var row = context.Meetings.Single(stored => stored.Id == meeting);
        row.LifecycleState = LifecycleState.Deleting;
        row.DeletedAt = Recorded;
        context.SaveChanges();

        var listed = new MeetingWork(context, Clock).Listed();

        listed.Count.ShouldBe(1);
        listed[0].Meeting.Id.ShouldBe(meeting);
        listed[0].Owed.WaitsOnSomebody.ShouldBeTrue();
        listed[0].Owed.MayBeTaken.ShouldBeFalse();
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

    private static Meeting NewMeeting(Guid id) => NewMeeting(id, Recorded);

    private static Meeting NewMeeting(Guid id, UtcTimestamp startedAt) => new()
    {
        Id = id,
        StartedAt = startedAt,
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
