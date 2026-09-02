using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The query the list emits, as against <c>MeetingWorkTests</c>, which is about the answer it
/// comes back with.
/// </summary>
/// <remarks>
/// Both are needed and neither covers the other. <see cref="MeetingWork.Listed"/> narrows in the
/// database and then decides in memory over what came back, so the narrowing can be deleted
/// outright and every assertion about which meetings are listed stays green — the same meetings
/// come back, read off the whole table with the two side queries run over rows that are then
/// thrown away. That is not a hypothetical: it happened, and an audit found it rather than a test.
/// <para>
/// The SQL and not SQLite's plan for it. A plan is the engine's answer to the query and changes
/// when the engine does, which would put a test here that goes red over a release with nothing
/// about this application being wrong. What EF emitted is the application's own, down to the
/// stored names <c>CorpusNamingTests</c> pins.
/// </para>
/// </remarks>
public class MeetingListQueryTests
{
    /// <summary>Meetings in the corpus. Enough that reading the table and reading the list differ.</summary>
    private const int InTheCorpus = 4;

    /// <summary>
    /// How many of them the list brings back: the active one, and the one on its way out that a
    /// job has stopped on a person. One for each half of the rule, so neither half can be dropped
    /// without a meeting going missing.
    /// </summary>
    private const int OnTheList = 2;

    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly TimeProvider Clock =
        new FakeClock(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_list_narrows_in_the_database()
    {
        using var corpus = new TemporaryCorpus();

        // Before the corpus, which is the order this has to be written in: EF settles once whether
        // anybody is listening, so a listener opened later hears nothing for a while.
        using var heard = EmittedSql.Over(corpus.Root);
        using var context = corpus.OpenMigrated();

        Add(context, NewMeeting());
        Add(context, OnItsWayOut(NewMeeting()));
        Add(context, OnItsWayOut(NewMeeting()));

        var unsettled = OnItsWayOut(NewMeeting());
        Add(context, unsettled);
        Add(context, StoppedOnAPerson(unsettled.Id));

        heard.Forget();
        var listed = new MeetingWork(context, Clock).Listed();
        var narrowed = heard.Reading("meetings");

        listed.Count.ShouldBe(OnTheList);

        // Both halves of the rule as predicates and not as columns: the meetings that are active,
        // and the ones a job has stopped on a person. The column name on its own would prove
        // nothing — every column of the row is in the projection whatever the query filters on —
        // and the bare word WHERE would prove nothing either, since the subquery carries one of
        // its own. Reaching processing_jobs from here is what keeps a meeting on its way out with
        // an unsettled charge on the list, so a narrowing that dropped it would be a hole and not
        // a tidy-up.
        narrowed.Sql.ShouldContain("\"lifecycle_state\" = 'active'");
        narrowed.Sql.ShouldContain("\"state\" = 'awaiting_user'");

        // And the narrowing is what the database was asked to do, rather than something the
        // statement mentions on its way to handing back everything: a filter moved into the
        // projection would satisfy both lines above and show up here. Said against the size of the
        // table rather than as a count of its own, because the reader is pulled once per row and
        // once more to find the end, and that last one is EF's business rather than this
        // application's. Nothing heard at all is zero, which is out of range and so still red.
        narrowed.Reads.ShouldBeInRange(1, InTheCorpus - 1);
    }

    private static void Add<TRow>(CorpusDbContext context, TRow row)
        where TRow : class
    {
        context.Add(row);
        context.SaveChanges();
    }

    private static Meeting NewMeeting() => new()
    {
        Id = Guid.NewGuid(),
        StartedAt = Recorded,
        SourceProfile = SourceProfile.Multichannel,
        Language = "es",
        CreatedAt = Recorded,
        UpdatedAt = Recorded,
    };

    /// <summary>
    /// A meeting somebody asked to get rid of. It is in the table and off the list, unless
    /// something is stopped on a person about it.
    /// </summary>
    private static Meeting OnItsWayOut(Meeting meeting)
    {
        meeting.LifecycleState = LifecycleState.Deleting;
        meeting.DeletedAt = Recorded;
        return meeting;
    }

    /// <summary>
    /// Transcription a restart found running: it may already have been paid for, so it waits on a
    /// person. It is the row the subquery exists to reach.
    /// </summary>
    private static ProcessingJob StoppedOnAPerson(Guid meeting)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/1", Recorded);

        job.Start(Recorded);
        job.RecoverAfterRestart().ShouldBeTrue();

        return job;
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
