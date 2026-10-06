using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// The screen one meeting is read from, against a corpus on disk.
/// </summary>
/// <remarks>
/// What the screen decides is `MeetingScreenTests`; what this adds is everything that needs rows
/// and files — that a recorded meeting finds its audio, that every thing an extraction left comes
/// back carrying where it was said, that one accepted extraction is read and the others are not,
/// and that a name typed here reaches the folder as well as the database.
/// </remarks>
public class MeetingReadingTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly TimeProvider Clock =
        new FakeClock(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_meeting_that_was_only_recorded_reads_with_its_audio_and_nothing_else()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        var read = new MeetingReading(context, Clock).Of(meeting);

        read.Screen.Stage.ShouldBe(MeetingStage.Recorded);
        read.Screen.MayBePlayedBack.ShouldBeTrue();
        read.Screen.TheRecording.ShouldBe(RecordedAudio.Playable);
        read.Screen.TheActOffered.ShouldBe(JobKind.Transcribe);
        read.Audio.ShouldNotBeNull();
        read.Audio.Exists.ShouldBeTrue();
        read.Screen.Left.Things.ShouldBeEmpty();
        read.Screen.Left.Abstract.ShouldBeNull();
        read.Screen.Left.Wrote.ShouldBe(WhoWroteThis.Nobody);
    }

    [Fact]
    public void A_meeting_whose_audio_row_points_at_nothing_offers_no_file_to_play()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, []);

        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.Audio,
            Origin = ArtifactKind.Audio.OriginOf(),
            RelativePath = CorpusFiles.PathFor(meeting, "audio.wav"),
            ByteSize = 4,
            Sha256 = new string('a', 64),
            ConfirmedAt = Recorded,
        });

        var read = new MeetingReading(context, Clock).Of(meeting);

        // The stage still says the audio was filed, because the row says so. What the screen gets
        // is no file — a play button over a path that is not there is one that does nothing.
        read.Screen.Stage.ShouldBe(MeetingStage.Recorded);
        // The player is the file's answer and not the stage's, so a row over a missing file is a
        // meeting that reads as recorded and does not play — and says which of the two absences it
        // is, because a recording the corpus records and cannot find is a source gone.
        read.Screen.MayBePlayedBack.ShouldBeFalse();
        read.Screen.TheRecording.ShouldBe(RecordedAudio.NotWhereTheCorpusSaysItIs);
        read.Audio.ShouldBeNull();
    }

    [Fact]
    public void A_meeting_whose_audio_was_deleted_says_so()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["turn 0"]);
        var asked = new MeetingReading(context, Clock);

        // Nobody recorded any: the same absence on the disk, and not the same thing to say.
        asked.Of(meeting).Screen.TheRecording.ShouldBe(RecordedAudio.NoneYet);

        context.Meetings.Where(row => row.Id == meeting)
            .ExecuteUpdate(set => set.SetProperty(row => row.AudioRemovedAt, (UtcTimestamp?)Recorded));

        var read = asked.Of(meeting);

        read.Screen.TheRecording.ShouldBe(RecordedAudio.Removed);
        read.Screen.MayBePlayedBack.ShouldBeFalse();
        read.Audio.ShouldBeNull();
    }

    [Fact]
    public void Every_thing_the_ai_left_comes_back_carrying_where_it_was_said()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);
        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: Recorded, "what the meeting was about",
            actionAt: 1, questionAt: 2);

        var read = new MeetingReading(context, Clock).Of(meeting);
        var left = read.Screen.Left;

        left.Abstract.ShouldBe("what the meeting was about");
        left.Things.Count.ShouldBe(3);
        left.Things.ShouldAllBe(thing => thing.At > Duration.Zero);
        left.Of(LeftKind.Decision).Single().Says.ShouldBe("what the meeting was about");
        left.Of(LeftKind.Action).Single().Says.ShouldBe("what the meeting was about, to do");
        left.Of(LeftKind.Question).Single().Says.ShouldBe("what the meeting was about, unresolved");

        // Earliest in the meeting first, which is the order a meeting is read in.
        left.MarkedAlongTheMeeting.ShouldBe([
            Duration.FromMilliseconds(1_000),
            Duration.FromMilliseconds(2_000),
            Duration.FromMilliseconds(3_000),
        ]);

        left.Things[0].TurnOrdinal.ShouldBe(0);
        left.Things[0].Quoted.ShouldNotBeNullOrWhiteSpace();
        left.Things[0].SpeakerLabel.ShouldBe("ch1:speaker_0");
    }

    [Fact]
    public void A_summarised_meeting_reads_the_summary_s_body()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);
        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: Recorded, "what the meeting was about",
            actionAt: 1, questionAt: 2);

        context.Summaries.Single(row => row.MeetingId == meeting).Body = "the longer account";
        context.SaveChanges();

        var left = new MeetingReading(context, Clock).Of(meeting).Screen.Left;

        left.Abstract.ShouldBe("what the meeting was about");
        left.Body.ShouldBe("the longer account");
    }

    [Fact]
    public void A_summary_with_no_body_reads_as_none()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);
        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: Recorded, "what the meeting was about",
            actionAt: 1, questionAt: 2);

        context.Summaries.Single(row => row.MeetingId == meeting).Body = "  ";
        context.SaveChanges();

        new MeetingReading(context, Clock).Of(meeting).Screen.Left.Body.ShouldBeNull();
    }

    [Fact]
    public void An_extraction_nobody_accepted_is_not_read_at_all()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);
        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: null, "what the meeting was about",
            actionAt: 1, questionAt: 2);

        var left = new MeetingReading(context, Clock).Of(meeting).Screen.Left;

        left.Things.ShouldBeEmpty();
        left.Abstract.ShouldBeNull();
        left.Wrote.Summariser.ShouldBeNull();
    }

    [Fact]
    public void Two_accepted_extractions_show_the_one_accepted_last_and_never_both()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);

        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: Recorded, "the first go",
            actionAt: 1, questionAt: 2);
        MeetingRows.Extracted(
            context,
            meeting,
            UtcTimestamp.From(Recorded.Value.AddHours(1)),
            accepted: UtcTimestamp.From(Recorded.Value.AddHours(1)),
            "the second go",
            actionAt: 1,
            questionAt: 2);

        var left = new MeetingReading(context, Clock).Of(meeting).Screen.Left;

        left.Abstract.ShouldBe("the second go");
        left.Things.Count.ShouldBe(3);
    }

    /// <summary>
    /// The two instants are pulled apart on purpose. Accepting is a person's act and it does not
    /// happen in the order the runs were made — somebody reads the newer extraction, does not take
    /// it, and accepts the older one afterwards — so an ordering that read <c>created_at</c> first
    /// would put on screen the very extraction they looked at and refused. Every other test in this
    /// suite creates and accepts a run in the same instant, which leaves the first step of that
    /// ordering unprobed on both readers.
    /// </summary>
    [Fact]
    public void The_run_read_is_the_one_accepted_last_even_where_it_was_created_first()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);

        // The older extraction, accepted after the newer one — somebody read both and kept the first.
        MeetingRows.Extracted(
            context,
            meeting,
            Recorded,
            accepted: UtcTimestamp.From(Recorded.Value.AddHours(2)),
            "the older run, accepted later",
            actionAt: 1,
            questionAt: 2);
        MeetingRows.Extracted(
            context,
            meeting,
            UtcTimestamp.From(Recorded.Value.AddHours(1)),
            accepted: UtcTimestamp.From(Recorded.Value.AddHours(1)),
            "the newer run, accepted first",
            actionAt: 1,
            questionAt: 2);

        var left = new MeetingReading(context, Clock).Of(meeting).Screen.Left;

        left.Abstract.ShouldBe("the older run, accepted later");
    }

    private static UtcTimestamp Hour(int hours) => UtcTimestamp.From(Recorded.Value.AddHours(hours));

    [Fact]
    public void Every_summary_a_meeting_was_given_is_offered_newest_first_with_the_one_shown_marked()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["turn 0"], root: corpus.Root);

        var older = MeetingRows.Extracted(context, meeting, Hour(1), accepted: Hour(1), "older");
        var newer = MeetingRows.Extracted(context, meeting, Hour(2), accepted: Hour(2), "newer");
        MeetingRows.Extracted(context, meeting, Hour(3), accepted: null, "never accepted");

        var every = new MeetingReading(context, Clock).Of(meeting).Screen.EverySummary;

        every.Select(given => given.RunId).ShouldBe([newer, older]);
        every.Select(given => given.IsShown).ShouldBe([true, false]);
        every.Select(given => given.AcceptedAt).ShouldBe([Hour(2), Hour(1)]);
        every[0].WrittenBy.ShouldBe("claude-code");
    }

    [Fact]
    public void An_earlier_summary_put_back_is_read_whole_as_it_was_accepted()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["turn 0", "turn 1"], root: corpus.Root);

        var first = MeetingRows.Extracted(
            context, meeting, Hour(1), accepted: Hour(1), "the first go", actionAt: 1, questionAt: 1);
        MeetingRows.Extracted(
            context, meeting, Hour(2), accepted: Hour(2), "the second go", actionAt: 1, questionAt: 1);

        var reading = new MeetingReading(context, Clock);
        reading.ShowSummary(meeting, first);

        var screen = reading.Of(meeting).Screen;

        screen.Left.Abstract.ShouldBe("the first go");
        screen.Left.Things.Select(thing => thing.Says)
            .ShouldBe(["the first go", "the first go, to do", "the first go, unresolved"], ignoreOrder: true);
        screen.Left.Wrote.SummarisedAt.ShouldBe(Hour(1));
        screen.EverySummary.Single(given => given.IsShown).RunId.ShouldBe(first);
    }

    [Fact]
    public void A_summary_put_back_is_still_the_one_shown_after_the_corpus_is_opened_again()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting;
        Guid first;

        using (var context = corpus.OpenMigrated())
        {
            meeting = MeetingRows.Recorded(context, Recorded, ["turn 0"], root: corpus.Root);
            first = MeetingRows.Extracted(context, meeting, Hour(1), accepted: Hour(1), "the first go");
            MeetingRows.Extracted(context, meeting, Hour(2), accepted: Hour(2), "the second go");

            new MeetingReading(context, Clock).ShowSummary(meeting, first);
        }

        using var reopened = corpus.Open();

        new MeetingReading(reopened, Clock).Of(meeting).Screen.Left.Abstract.ShouldBe("the first go");
    }

    [Fact]
    public void Putting_a_summary_back_loses_none_of_the_others_and_each_can_be_chosen_again()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["turn 0"], root: corpus.Root);

        var oldest = MeetingRows.Extracted(context, meeting, Hour(1), accepted: Hour(1), "oldest");
        var middle = MeetingRows.Extracted(context, meeting, Hour(2), accepted: Hour(2), "middle");
        var newest = MeetingRows.Extracted(context, meeting, Hour(3), accepted: Hour(3), "newest");

        // Each press a minute after the one before, because a summary put back at the same instant
        // as another has no later word than it.
        var minute = 0;
        foreach (var (chosen, abstractIs) in new[] { (oldest, "oldest"), (middle, "middle"), (newest, "newest") })
        {
            minute++;
            var at = new FakeClock(new DateTimeOffset(2026, 8, 19, 12, minute, 0, TimeSpan.Zero));
            var reading = new MeetingReading(context, at);

            reading.ShowSummary(meeting, chosen);

            var screen = reading.Of(meeting).Screen;
            screen.EverySummary.Count.ShouldBe(3);
            screen.EverySummary.Single(given => given.IsShown).RunId.ShouldBe(chosen);
            screen.Left.Abstract.ShouldBe(abstractIs);
        }
    }

    [Fact]
    public void A_summary_accepted_after_one_was_put_back_is_the_one_shown()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["turn 0"], root: corpus.Root);

        var first = MeetingRows.Extracted(context, meeting, Hour(1), accepted: Hour(1), "the first go");
        MeetingRows.Extracted(context, meeting, Hour(2), accepted: Hour(2), "the second go");

        new MeetingReading(context, Clock).ShowSummary(meeting, first);
        new MeetingReading(context, Clock).Of(meeting).Screen.Left.Abstract.ShouldBe("the first go");

        // Accepted after the 12:00 the first was put back at.
        var later = UtcTimestamp.From(Clock.GetUtcNow().AddMinutes(30));
        MeetingRows.Extracted(context, meeting, later, accepted: later, "the third go");

        new MeetingReading(context, Clock).Of(meeting).Screen.Left.Abstract.ShouldBe("the third go");
    }

    [Fact]
    public void A_meeting_that_arrived_without_a_recording_says_so_rather_than_saying_it_is_lost()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, []);

        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.DeepgramResponse,
            Origin = ArtifactKind.DeepgramResponse.OriginOf(),
            RelativePath = CorpusFiles.PathFor(meeting, "deepgram.json"),
            ByteSize = 4,
            Sha256 = new string('a', 64),
            ConfirmedAt = Recorded,
        });

        var read = new MeetingReading(context, Clock).Of(meeting);

        // A paid response and no audio: this meeting never had a recording, and the corpus records
        // none. Not the same news as one whose file the disk has lost.
        read.Screen.Stage.ShouldBe(MeetingStage.Transcribed);
        read.Screen.TheRecording.ShouldBe(RecordedAudio.NoneYet);
        read.Screen.MayBePlayedBack.ShouldBeFalse();
        read.Screen.ThereIsATranscription.ShouldBeTrue();
        read.Screen.ThereIsASummary.ShouldBeFalse();
    }

    [Fact]
    public void Who_transcribed_it_and_who_summarised_it_are_said_with_when()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2"], root: corpus.Root);
        MeetingRows.Transcribed(context, meeting, Recorded, responseSha256: new string('d', 64));
        MeetingRows.Extracted(
            context, meeting, Recorded, accepted: Recorded, "what the meeting was about",
            actionAt: 1, questionAt: 2);

        var wrote = new MeetingReading(context, Clock).Of(meeting).Screen.Left.Wrote;

        wrote.Transcriber.ShouldBe("deepgram nova-3");
        wrote.TranscribedAt.ShouldBe(Recorded);
        wrote.Summariser.ShouldBe("claude-code");
        wrote.SummarisedAt.ShouldBe(Recorded);
    }

    [Fact]
    public void A_transcription_that_never_came_back_names_nobody()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        MeetingRows.Transcribed(context, meeting, Recorded, finished: false);

        var wrote = new MeetingReading(context, Clock).Of(meeting).Screen.Left.Wrote;

        wrote.Transcriber.ShouldBeNull();
        wrote.TranscribedAt.ShouldBeNull();
    }

    /// <summary>ISC-143.</summary>
    [Fact]
    public void A_meeting_left_without_a_summary_says_which_condition_failed_and_on_which_statement()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);
        var refusal = new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", "Lanzar el viernes.");
        MeetingRows.RefusedExtraction(context, meeting, Recorded, refusal);

        var screen = new MeetingReading(context, Clock).Of(meeting).Screen;

        screen.WhyTheSummaryWasRefused.ShouldBe(refusal);
    }

    /// <summary>
    /// #121. Both runs share one <c>CreatedAt</c>, which every fixed test clock leaves them with —
    /// so what says which one a person reads is <c>CorrectsRunId</c> and neither time nor id.
    /// </summary>
    [Fact]
    public void A_meeting_whose_correction_was_refused_says_what_the_correction_was_refused_for()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/1", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        var first = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = Recorded,
        };
        MeetingRows.Add(context, first);
        MeetingRows.Add(context, new ExtractionRunRefusal
        {
            ExtractionRunId = first.Id,
            Ordinal = 0,
            Condition = ExtractionCondition.NoEvidence,
            Path = "decisions[0]",
            Statement = "Lanzar el viernes.",
        });

        var correction = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            CorrectsRunId = first.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = Recorded,
        };
        MeetingRows.Add(context, correction);
        MeetingRows.Add(context, new ExtractionRunRefusal
        {
            ExtractionRunId = correction.Id,
            Ordinal = 0,
            Condition = ExtractionCondition.CitedAgainElsewhere,
            Path = "actions[0]",
            Statement = "Lanzar el viernes.",
        });

        job.FailPermanently(JobFailure.ExtractionRefused, "refused again", Recorded);
        context.SaveChanges();

        var screen = new MeetingReading(context, Clock).Of(meeting).Screen;

        screen.WhyTheSummaryWasRefused.ShouldBe(
            new ExtractionRefusal(ExtractionCondition.CitedAgainElsewhere, "actions[0]", "Lanzar el viernes."));
    }

    /// <summary>
    /// The runner's retries leave one uncorrected run per attempt, and the newest is the one that
    /// failed the job. The earlier is inserted first: unordered, SQLite would return it.
    /// </summary>
    [Fact]
    public void A_meeting_refused_on_a_later_attempt_says_what_that_attempt_was_refused_for()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/1", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        var later = UtcTimestamp.From(Recorded.Value.AddMinutes(1));

        foreach (var (at, condition) in new[]
        {
            (Recorded, ExtractionCondition.NoEvidence),
            (later, ExtractionCondition.CitedAgainElsewhere),
        })
        {
            var run = new ExtractionRun
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                JobId = job.Id,
                Provider = "claude-code",
                PromptVersion = "1",
                SchemaVersion = "1",
                InputHash = new string('d', 64),
                CreatedAt = at,
            };
            MeetingRows.Add(context, run);
            MeetingRows.Add(context, new ExtractionRunRefusal
            {
                ExtractionRunId = run.Id,
                Ordinal = 0,
                Condition = condition,
                Path = "decisions[0]",
                Statement = "Lanzar el viernes.",
            });
        }

        job.FailPermanently(JobFailure.ExtractionRefused, "refused", later);
        context.SaveChanges();

        var screen = new MeetingReading(context, Clock).Of(meeting).Screen;

        screen.WhyTheSummaryWasRefused.ShouldBe(
            new ExtractionRefusal(ExtractionCondition.CitedAgainElsewhere, "decisions[0]", "Lanzar el viernes."));
    }

    /// <summary>
    /// The self-referencing <c>corrects_run_id</c> foreign key cascades cleanly on top of the
    /// table's own <c>meeting_id</c> cascade, rather than the two conflicting the way
    /// <c>docs/migrations.md</c> warns a self-referencing foreign key can.
    /// </summary>
    [Fact]
    public void Deleting_a_meeting_takes_a_refused_run_and_its_correction_together()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/1", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        var first = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = Recorded,
        };
        MeetingRows.Add(context, first);

        var correction = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            CorrectsRunId = first.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = Recorded,
        };
        MeetingRows.Add(context, correction);

        // Raw SQL and not a tracked Remove: this suite's meeting also carries a transcription
        // whose own job EF's client-side cascade would try to sever at the same time, over a
        // relationship that is NO ACTION rather than cascading — a confusion the database itself
        // does not have. The literal is upper-cased because that is the case SQLite actually
        // stores a GUID column under; a lower-case literal here would silently match no row.
        Sql.Execute(context, $"DELETE FROM meetings WHERE id = '{meeting.ToString().ToUpperInvariant()}';");

        Sql.Scalar(context, "SELECT count(*) FROM extraction_runs;").ShouldBe(0L);
    }

    [Fact]
    public void A_meeting_that_has_a_summary_says_nothing_about_an_attempt_that_was_refused()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);
        MeetingRows.RefusedExtraction(
            context, meeting, Recorded, new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", null));

        var later = UtcTimestamp.From(Recorded.Value.AddSeconds(1));
        MeetingRows.Extracted(context, meeting, later, accepted: later, "what the meeting was about");

        var screen = new MeetingReading(context, Clock).Of(meeting).Screen;

        screen.WhyTheSummaryWasRefused.ShouldBeNull();
    }

    [Fact]
    public void A_refused_second_summary_leaves_the_first_on_screen_and_says_what_it_was_refused_for()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0"], responseSha256: new string('a', 64), root: corpus.Root);
        var first = UtcTimestamp.From(Recorded.Value.AddSeconds(1));
        MeetingRows.Extracted(context, meeting, first, accepted: first, "what the meeting was about");
        context.ProcessingJobs.Single(row => row.Kind == JobKind.Extract).Start(first);
        context.ProcessingJobs.Single(row => row.Kind == JobKind.Extract).Succeed(first);
        context.SaveChanges();

        var later = UtcTimestamp.From(Recorded.Value.AddSeconds(2));
        MeetingRows.RefusedExtraction(
            context, meeting, later, new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", null));

        var screen = new MeetingReading(context, Clock).Of(meeting).Screen;

        screen.ThereIsASummary.ShouldBeTrue();
        screen.Left.Abstract.ShouldBe("what the meeting was about");
        screen.WhyTheSummaryWasRefused.ShouldBe(
            new ExtractionRefusal(ExtractionCondition.NoEvidence, "decisions[0]", null));
        screen.WhyTheLastSummaryFailed.ShouldBe(JobFailure.ExtractionRefused);
    }

    [Fact]
    public void A_citation_opens_the_turns_around_the_one_it_anchors_on()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context,
            Recorded,
            [.. Enumerable.Range(0, 10).Select(ordinal => $"turn {ordinal}")],
            root: corpus.Root);

        var around = new MeetingReading(context, Clock).Around(meeting, 5);

        around.Select(turn => turn.Ordinal).ShouldBe([3, 4, 5, 6, 7]);
    }

    [Fact]
    public void A_citation_at_the_start_of_a_meeting_opens_what_there_is()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1"], root: corpus.Root);

        new MeetingReading(context, Clock).Around(meeting, 0)
            .Select(turn => turn.Ordinal)
            .ShouldBe([0, 1]);
    }

    [Fact]
    public void A_meeting_whose_turns_were_never_produced_opens_nothing_and_does_not_refuse()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        new MeetingReading(context, Clock).Around(meeting, 4).ShouldBeEmpty();
    }

    [Fact]
    public void A_name_typed_here_reaches_the_row_and_the_recovery_card()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        new MeetingReading(context, Clock).Name(meeting, "Entrevista — Marina Robles");

        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBe("Entrevista — Marina Robles");

        // The folder as well as the database. A rename that reached only one of them leaves the
        // card beside the audio saying something else until the next rebuild.
        var card = MeetingManifest.Read(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, MeetingManifest.FileName)));

        card.Title.ShouldBe("Entrevista — Marina Robles");
    }

    [Fact]
    public void A_name_somebody_cleared_leaves_the_meeting_reading_as_one_nobody_named()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        var reading = new MeetingReading(context, Clock);

        reading.Name(meeting, "something");
        reading.Name(meeting, "   ");

        // Null and never the empty string, which on a list looks exactly like a blank title and
        // reads to a screen reader as nothing at all.
        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBeNull();
    }

    [Fact]
    public void A_name_is_taken_as_typed_without_the_space_around_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        new MeetingReading(context, Clock).Name(meeting, "  Llamada con proveedor  ");

        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBe("Llamada con proveedor");
    }

    [Fact]
    public void Naming_a_meeting_what_it_is_already_called_writes_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        var reading = new MeetingReading(context, Clock);

        reading.Name(meeting, "Llamada");
        var after = context.Meetings.Single(row => row.Id == meeting).UpdatedAt;

        reading.Name(meeting, "Llamada");

        context.Meetings.Single(row => row.Id == meeting).UpdatedAt.ShouldBe(after);
    }

    [Fact]
    public void Naming_if_nobody_has_leaves_a_named_meeting_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        var reading = new MeetingReading(context, Clock);

        reading.Name(meeting, "Llamada");

        reading.NameIfNobodyHas(meeting, "Lanzamiento").ShouldBeFalse();
        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBe("Llamada");
    }

    [Fact]
    public void Naming_if_nobody_has_writes_the_recovery_card()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        new MeetingReading(context, Clock).NameIfNobodyHas(meeting, " Lanzamiento ").ShouldBeTrue();

        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBe("Lanzamiento");
        MeetingManifest.Read(
                CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, MeetingManifest.FileName)))
            .Title.ShouldBe("Lanzamiento");
    }

    [Fact]
    public void A_meeting_this_corpus_does_not_hold_is_refused_by_name()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var reading = new MeetingReading(context, Clock);
        var nobody = Guid.NewGuid();

        Should.Throw<MeetingStageException>(() => reading.Of(nobody));
        Should.Throw<MeetingStageException>(() => reading.Name(nobody, "anything"));
    }

    /// <summary>
    /// A stretch of a meeting is the turns that began inside it, closed at the bottom and open at
    /// the top.
    /// </summary>
    /// <remarks>
    /// Red the day the top of the window is made inclusive, which is not a rounding difference: two
    /// calls walking a meeting back to back would each answer with the turn on the boundary, and an
    /// agent reading a meeting in stretches would quote it twice with nothing downstream able to
    /// tell that from a thing that really was said twice. The second half — that the two halves
    /// together are the whole — is what says the window loses nothing either.
    /// </remarks>
    [Fact]
    public void Turns_between_two_offsets_are_the_ones_said_in_that_stretch()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2", "turn 3"], root: corpus.Root);

        var reading = new MeetingReading(context, Clock);

        // The turns start at 1000, 2000, 3000 and 4000 ms.
        var stretch = reading.Between(
            meeting, Duration.FromMilliseconds(2_000), Duration.FromMilliseconds(4_000), 10);

        stretch.Select(turn => turn.Ordinal).ShouldBe([1, 2]);

        var before = reading.Between(meeting, Duration.Zero, Duration.FromMilliseconds(2_000), 10);
        var after = reading.Between(
            meeting, Duration.FromMilliseconds(2_000), Duration.FromMilliseconds(10_000), 10);

        before.Select(turn => turn.Ordinal).ShouldBe([0]);
        after.Select(turn => turn.Ordinal).ShouldBe([1, 2, 3]);
    }

    /// <summary>
    /// The bound is applied to the read and not to what comes back from it.
    /// </summary>
    /// <remarks>
    /// The stretch a caller asks for can be the whole of a three-hour meeting, so a bound taken
    /// after the read is every turn of one materialised to hand back two hundred. Red against a
    /// <c>Take</c> moved out of the query — which answers the same rows and so cannot be seen in
    /// what comes back, only in how many the meeting had to give up to produce it. The earliest
    /// turns and not any two: what a bounded stretch means is the start of it.
    /// </remarks>
    [Fact]
    public void A_stretch_longer_than_the_bound_comes_back_as_its_first_turns()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["turn 0", "turn 1", "turn 2", "turn 3"], root: corpus.Root);

        new MeetingReading(context, Clock)
            .Between(meeting, Duration.Zero, Duration.FromMilliseconds(10_000), 2)
            .Select(turn => turn.Ordinal)
            .ShouldBe([0, 1]);
    }

    /// <summary>
    /// A transcript says which paid response its turns were projected from, off the turn source and
    /// not off the newest response filed against the meeting.
    /// </summary>
    /// <remarks>
    /// The two come apart exactly where it matters. A meeting transcribed a second time has a newer
    /// response on disk from the moment the job confirms it, and its turns are still the first
    /// response's until the projection is rendered again — so answering from the newest artifact
    /// hands a reader a hash the quotation is not in, which is a corpus inconsistency that does not
    /// exist. Red the day this reads the newest response artifact instead of the turn source.
    /// </remarks>
    [Fact]
    public void A_transcript_says_which_response_its_turns_were_projected_from()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        var produced = new string('7', 64);
        MeetingRows.Transcribed(context, meeting, Recorded, produced);

        // A second response, paid for and filed later, whose own run has not come back. The turns
        // on disk are still the first one's.
        MeetingRows.Add(context, new Artifact
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            Kind = ArtifactKind.DeepgramResponse,
            Origin = ArtifactKind.DeepgramResponse.OriginOf(),
            RelativePath = CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)),
            ByteSize = 4,
            Sha256 = new string('8', 64),
            ConfirmedAt = UtcTimestamp.From(Recorded.Value.AddHours(2)),
        });

        new MeetingReading(context, Clock).TranscribedFrom(meeting).ShouldBe(produced);
    }

    /// <summary>
    /// A meeting whose turns came from no response anybody paid for says so, rather than saying
    /// nothing or guessing.
    /// </summary>
    [Fact]
    public void A_meeting_with_no_paid_response_behind_it_names_none()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);

        new MeetingReading(context, Clock).TranscribedFrom(meeting).ShouldBeNull();
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
