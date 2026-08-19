using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// ISC-82 and ISC-147 over the rule itself: which stage a meeting is at, what the application
/// would do to it next, and what its jobs say about that. That the answer survives the
/// application closing is a corpus's to prove and is in `MeetingWorkTests`.
/// </summary>
public class MeetingStageTests
{
    private static readonly UtcTimestamp Noon =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    private static readonly Guid TheMeeting = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Every_stage_says_what_the_application_would_do_next()
    {
        // Without this the table can grow a stage nobody wired up, and the first meeting to reach
        // it throws on a screen instead of showing a button.
        foreach (var stage in Enum.GetValues<MeetingStage>())
        {
            Should.NotThrow(() => stage.Offers());
        }
    }

    [Fact]
    public void A_stage_the_enum_does_not_have_is_not_guessed_at()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ((MeetingStage)99).Offers());
    }

    [Fact]
    public void Every_standing_says_whether_the_action_can_be_taken_and_whether_it_can_be_left()
    {
        foreach (var standing in Enum.GetValues<StageStanding>())
        {
            Should.NotThrow(() => standing.MayBeTaken());
            Should.NotThrow(() => standing.MayBeLeft());
        }

        Should.Throw<ArgumentOutOfRangeException>(() => ((StageStanding)99).MayBeTaken());
        Should.Throw<ArgumentOutOfRangeException>(() => ((StageStanding)99).MayBeLeft());
    }

    [Fact]
    public void Nothing_the_application_offers_is_work_it_could_do_for_nothing()
    {
        // The card's second sentence, as a rule rather than as a habit. Rendering the transcript's
        // files costs nothing and can be produced again, so it is never something a person is
        // asked about; every action that does reach a button spends money or quota.
        var offered = Enum.GetValues<MeetingStage>()
            .Select(stage => stage.Offers())
            .OfType<JobKind>()
            .ToHashSet();

        offered.ShouldNotContain(JobKind.Render);
        offered.ShouldBe([JobKind.Transcribe, JobKind.Extract], ignoreOrder: true);
    }

    [Fact]
    public void A_meeting_with_no_audio_yet_is_never_offered_for_transcription()
    {
        // A meeting's row and folder exist before its first sample, so a meeting being recorded
        // right now is in the corpus with nothing under it. Offering to pay a provider to
        // transcribe it is the one mistake here that costs money.
        var owed = Owed([]);

        owed.Stage.ShouldBe(MeetingStage.Recording);
        owed.Next.ShouldBeNull();
        owed.MayBeTaken.ShouldBeFalse();
        owed.MayBeLeft.ShouldBeFalse();
        owed.IsOwed.ShouldBeFalse();
        owed.Standing.ShouldBe(StageStanding.NothingToDo);
    }

    [Fact]
    public void A_recorded_meeting_is_owed_a_transcription()
    {
        var owed = Owed([ArtifactKind.Audio]);

        owed.Stage.ShouldBe(MeetingStage.Recorded);
        owed.Next.ShouldBe(JobKind.Transcribe);
        owed.Standing.ShouldBe(StageStanding.Offered);
        owed.MayBeTaken.ShouldBeTrue();
        owed.MayBeLeft.ShouldBeTrue();
        owed.IsOwed.ShouldBeTrue();
    }

    [Fact]
    public void A_transcribed_meeting_is_owed_a_summary()
    {
        var owed = Owed([ArtifactKind.Audio, ArtifactKind.DeepgramResponse]);

        owed.Stage.ShouldBe(MeetingStage.Transcribed);
        owed.Next.ShouldBe(JobKind.Extract);
        owed.IsOwed.ShouldBeTrue();
    }

    [Fact]
    public void A_summarised_meeting_is_owed_nothing_and_offers_nothing()
    {
        var owed = Owed([ArtifactKind.Audio, ArtifactKind.DeepgramResponse, ArtifactKind.Extraction]);

        owed.Stage.ShouldBe(MeetingStage.Summarised);
        owed.Next.ShouldBeNull();
        owed.Standing.ShouldBe(StageStanding.NothingToDo);
        owed.MayBeTaken.ShouldBeFalse();
        owed.MayBeLeft.ShouldBeFalse();
        owed.IsOwed.ShouldBeFalse();
    }

    [Fact]
    public void The_rendered_files_move_a_meeting_nowhere()
    {
        // They are derived, so a meeting that has them and a meeting that has lost them are owed
        // the same thing. A ladder that read them would show a meeting as further along for
        // having files anybody can make again.
        var owed = Owed([ArtifactKind.Audio, ArtifactKind.Transcript, ArtifactKind.Utterances, ArtifactKind.Summary]);

        owed.Stage.ShouldBe(MeetingStage.Recorded);
        owed.Next.ShouldBe(JobKind.Transcribe);
    }

    [Fact]
    public void A_meeting_that_arrived_already_summarised_is_not_offered_the_stage_underneath()
    {
        // An import can bring a summary without the response it came from. Reading the ladder
        // from the bottom would stop at the missing rung and offer to pay for a transcription
        // whose output is already there.
        var owed = Owed([ArtifactKind.Audio, ArtifactKind.Extraction]);

        owed.Stage.ShouldBe(MeetingStage.Summarised);
        owed.Next.ShouldBeNull();
    }

    [Fact]
    public void A_transcription_whose_job_landed_is_not_offered_again_because_its_file_is_missing()
    {
        // The corpus disagreeing with itself must not cost money. Offered again, the second press
        // is a second charge for work that already went through.
        var landed = Job(JobKind.Transcribe);
        landed.Start(Noon);
        landed.Succeed(Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [landed]);

        owed.Stage.ShouldBe(MeetingStage.Transcribed);
        owed.Next.ShouldBe(JobKind.Extract);
    }

    [Fact]
    public void A_stage_with_work_queued_cannot_be_asked_for_again_and_can_still_be_left()
    {
        // The press that spends money must not be the one with no way back. Nothing has run, so
        // nothing has been paid for, and somebody who asked can still say never mind.
        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [Job(JobKind.Transcribe)]);

        owed.Standing.ShouldBe(StageStanding.Underway);
        owed.MayBeTaken.ShouldBeFalse();
        owed.MayBeLeft.ShouldBeTrue();
        owed.IsOwed.ShouldBeFalse();
    }

    [Fact]
    public void A_stage_stopped_on_a_person_is_said_so_and_offers_no_press()
    {
        // The state with money on it: a charge that may already have happened. Anything that let
        // this read as offered would put the press that pays a second time back on the screen.
        var uncertain = Job(JobKind.Transcribe);
        uncertain.Start(Noon);
        uncertain.RecoverAfterRestart().ShouldBeTrue();

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [uncertain]);

        owed.Standing.ShouldBe(StageStanding.StoppedOnAPerson);
        owed.WaitsOnSomebody.ShouldBeTrue();
        owed.MayBeTaken.ShouldBeFalse();

        // Nor left. What is unsettled is whether a charge already happened, and dropping the job
        // throws away the only record that it might have.
        owed.MayBeLeft.ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_stopped_on_a_person_says_so_whatever_stage_it_is_at()
    {
        // The card's third paragraph is about the meeting, not about one of its stages. A capture
        // a restart stopped sits under a meeting with no audio, whose stage offers nothing — and
        // this screen is the only place in the application that shows a job stopped on a person.
        var interrupted = Job(JobKind.Capture);
        interrupted.Start(Noon);
        interrupted.RecoverAfterRestart().ShouldBeTrue();

        var owed = OwedWork.Of(TheMeeting, [], [interrupted]);

        owed.Stage.ShouldBe(MeetingStage.Recording);
        owed.WaitsOnSomebody.ShouldBeTrue();
        owed.MayBeTaken.ShouldBeFalse();
    }

    [Fact]
    public void An_unsettled_charge_on_a_stage_already_passed_still_withholds_the_next_one()
    {
        // A transcription left unsettled by a restart whose response then turned up. Reading the
        // standing off the next stage's kind alone would show it as waiting to be told, with an
        // accent button offering to spend again on a meeting that may already have been charged.
        var uncertain = Job(JobKind.Transcribe);
        uncertain.Start(Noon);
        uncertain.RecoverAfterRestart();

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio, ArtifactKind.DeepgramResponse], [uncertain]);

        owed.Stage.ShouldBe(MeetingStage.Transcribed);
        owed.Standing.ShouldBe(StageStanding.StoppedOnAPerson);
        owed.MayBeTaken.ShouldBeFalse();
    }

    [Fact]
    public void A_stage_whose_attempt_failed_for_good_is_owed_and_offered_exactly_as_before()
    {
        // Work that did not happen leaves the stage where it was. What a person is told about the
        // failure belongs beside whatever produced it, and nothing runs a job yet.
        var lost = Job(JobKind.Transcribe);
        lost.Start(Noon);
        lost.FailPermanently("the audio is not something the provider accepts", Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [lost]);

        owed.Standing.ShouldBe(StageStanding.Offered);
        owed.MayBeTaken.ShouldBeTrue();
        owed.IsOwed.ShouldBeTrue();
    }

    [Fact]
    public void A_declined_stage_stays_where_it_was_and_keeps_its_action()
    {
        // ISC-147. Ignoring is not final: the meeting has not moved, the same action is still
        // there to press, and what stopped is the application counting it among what it is
        // waiting on.
        var declined = Job(JobKind.Transcribe);
        declined.Cancel(Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [declined]);

        owed.Stage.ShouldBe(MeetingStage.Recorded);
        owed.Next.ShouldBe(JobKind.Transcribe);
        owed.Standing.ShouldBe(StageStanding.Declined);
        owed.MayBeTaken.ShouldBeTrue();
        owed.IsOwed.ShouldBeFalse();
    }

    [Fact]
    public void A_stage_declined_and_then_taken_reads_as_underway_rather_than_as_declined()
    {
        // Two rows of the same kind against one meeting, which is what a re-offer is. Asking
        // which is newer would make the answer turn on two timestamps; the precedence does not.
        var declined = Job(JobKind.Transcribe);
        declined.Cancel(Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [declined, Job(JobKind.Transcribe)]);

        owed.Standing.ShouldBe(StageStanding.Underway);
    }

    [Fact]
    public void An_uncertain_charge_is_shown_over_anything_else_that_is_also_true()
    {
        var uncertain = Job(JobKind.Transcribe);
        uncertain.Start(Noon);
        uncertain.RecoverAfterRestart();

        var declined = Job(JobKind.Transcribe);
        declined.Cancel(Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [uncertain, declined, Job(JobKind.Transcribe)]);

        owed.Standing.ShouldBe(StageStanding.StoppedOnAPerson);
    }

    [Fact]
    public void A_job_of_another_stage_says_nothing_about_this_one()
    {
        // A meeting waiting to be transcribed with a cancelled summary against it is offered its
        // transcription, not shown as having declined it.
        var declinedSummary = Job(JobKind.Extract);
        declinedSummary.Cancel(Noon);

        var owed = OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [declinedSummary]);

        owed.Next.ShouldBe(JobKind.Transcribe);
        owed.Standing.ShouldBe(StageStanding.Offered);
    }

    [Fact]
    public void Another_meetings_jobs_are_refused_rather_than_read()
    {
        var elsewhere = ProcessingJob.Queue(
            Guid.NewGuid(), Guid.NewGuid(), JobKind.Transcribe, "somewhere/1", Noon);

        Should.Throw<ArgumentException>(
            () => OwedWork.Of(TheMeeting, [ArtifactKind.Audio], [elsewhere]));
    }

    private static OwedWork Owed(ArtifactKind[] artifacts) => OwedWork.Of(TheMeeting, artifacts, []);

    private static ProcessingJob Job(JobKind kind) =>
        ProcessingJob.Queue(Guid.NewGuid(), TheMeeting, kind, $"{TheMeeting}/{Guid.NewGuid()}", Noon);
}
