using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// The one status a meeting is in, which a card and the meeting's own screen both print: one fact
/// per row of the mapping, over <see cref="OwedWork.Of"/> and the job rows it reads, never over a
/// stage and a standing written by hand.
/// </summary>
public class MeetingStatusTests
{
    private static readonly UtcTimestamp Noon =
        UtcTimestamp.From(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    private static readonly Guid TheMeeting = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly ArtifactKind[] Recorded = [ArtifactKind.Audio];

    private static readonly ArtifactKind[] Transcribed = [ArtifactKind.Audio, ArtifactKind.DeepgramResponse];

    private static readonly ArtifactKind[] Summarised =
        [ArtifactKind.Audio, ArtifactKind.DeepgramResponse, ArtifactKind.Extraction];

    [Fact]
    public void A_meeting_with_no_audio_reads_as_no_audio() =>
        OwedWork.Of(TheMeeting, [], []).Status.ShouldBe(MeetingStatus.NoAudio);

    [Fact]
    public void A_recorded_meeting_nobody_has_answered_for_reads_as_recorded() =>
        OwedWork.Of(TheMeeting, Recorded, []).Status.ShouldBe(MeetingStatus.Recorded);

    [Fact]
    public void A_transcription_waiting_to_run_reads_as_queued() =>
        OwedWork.Of(TheMeeting, Recorded, [Job(JobKind.Transcribe)]).Status.ShouldBe(MeetingStatus.Queued);

    [Fact]
    public void A_transcription_being_run_reads_as_transcribing()
    {
        var sent = Job(JobKind.Transcribe);
        sent.Start(Noon);

        OwedWork.Of(TheMeeting, Recorded, [sent]).Status.ShouldBe(MeetingStatus.Transcribing);
    }

    [Fact]
    public void A_transcription_turned_down_reads_as_ignored()
    {
        var declined = Job(JobKind.Transcribe);
        declined.Cancel(Noon);

        OwedWork.Of(TheMeeting, Recorded, [declined]).Status.ShouldBe(MeetingStatus.Ignored);
    }

    [Fact]
    public void A_transcribed_meeting_nobody_has_summarised_reads_as_transcribed() =>
        OwedWork.Of(TheMeeting, Transcribed, []).Status.ShouldBe(MeetingStatus.Transcribed);

    [Fact]
    public void A_summary_waiting_to_run_reads_as_queued() =>
        OwedWork.Of(TheMeeting, Transcribed, [Job(JobKind.Extract)]).Status.ShouldBe(MeetingStatus.Queued);

    [Fact]
    public void A_summary_being_run_on_a_transcribed_meeting_reads_as_summarising()
    {
        var running = Job(JobKind.Extract);
        running.Start(Noon);

        OwedWork.Of(TheMeeting, Transcribed, [running]).Status.ShouldBe(MeetingStatus.Summarising);
    }

    [Fact]
    public void A_summarised_meeting_with_nothing_running_reads_as_summarised() =>
        OwedWork.Of(TheMeeting, Summarised, []).Status.ShouldBe(MeetingStatus.Summarised);

    [Fact]
    public void A_second_summary_running_on_a_summarised_meeting_reads_as_summarising()
    {
        var second = Job(JobKind.Extract);
        second.Start(Noon);

        OwedWork.Of(TheMeeting, Summarised, [second]).Status.ShouldBe(MeetingStatus.Summarising);
    }

    [Fact]
    public void A_second_summary_waiting_to_run_reads_as_queued() =>
        OwedWork.Of(TheMeeting, Summarised, [Job(JobKind.Extract)]).Status.ShouldBe(MeetingStatus.Queued);

    [Theory]
    [InlineData(JobKind.Transcribe)]
    [InlineData(JobKind.Extract)]
    public void A_job_stopped_on_a_person_reads_as_stopped_whatever_the_stage(JobKind kind)
    {
        foreach (var artifacts in new[] { Recorded, Transcribed, Summarised })
        {
            OwedWork.Of(TheMeeting, artifacts, [Stopped(kind)]).Status.ShouldBe(MeetingStatus.Stopped);
        }
    }

    [Fact]
    public void A_capture_stopped_on_a_person_reads_as_stopped_on_a_meeting_with_no_audio()
    {
        // The stop outranks "no audio": a charge that may already have happened is the one thing
        // no other word may cover.
        var owed = OwedWork.Of(TheMeeting, [], [Stopped(JobKind.Capture)]);

        owed.Stage.ShouldBe(MeetingStage.Recording);
        owed.Status.ShouldBe(MeetingStatus.Stopped);
    }

    [Fact]
    public void A_pair_no_meeting_reaches_has_no_status_rather_than_another_one()
    {
        Should.Throw<InvalidOperationException>(
            () => new OwedWork(TheMeeting, MeetingStage.Summarised, StageStanding.Offered).Status);
        Should.Throw<InvalidOperationException>(
            () => new OwedWork(TheMeeting, MeetingStage.Transcribed, StageStanding.NothingToDo).Status);
    }

    [Fact]
    public void Every_status_is_reached_by_some_meeting()
    {
        var reached = new[]
        {
            OwedWork.Of(TheMeeting, [], []),
            OwedWork.Of(TheMeeting, Recorded, []),
            OwedWork.Of(TheMeeting, Recorded, [Job(JobKind.Transcribe)]),
            OwedWork.Of(TheMeeting, Recorded, [Started(JobKind.Transcribe)]),
            OwedWork.Of(TheMeeting, Transcribed, []),
            OwedWork.Of(TheMeeting, Transcribed, [Started(JobKind.Extract)]),
            OwedWork.Of(TheMeeting, Summarised, []),
            OwedWork.Of(TheMeeting, Recorded, [Declined(JobKind.Transcribe)]),
            OwedWork.Of(TheMeeting, Recorded, [Stopped(JobKind.Transcribe)]),
        }.Select(owed => owed.Status);

        reached.ShouldBe(Enum.GetValues<MeetingStatus>(), ignoreOrder: true);
    }

    private static ProcessingJob Job(JobKind kind) =>
        ProcessingJob.Queue(Guid.NewGuid(), TheMeeting, kind, $"{TheMeeting}/{Guid.NewGuid()}", Noon);

    private static ProcessingJob Started(JobKind kind)
    {
        var job = Job(kind);
        job.Start(Noon);
        return job;
    }

    private static ProcessingJob Declined(JobKind kind)
    {
        var job = Job(kind);
        job.Cancel(Noon);
        return job;
    }

    private static ProcessingJob Stopped(JobKind kind)
    {
        var job = Started(kind);
        job.AwaitUser("a restart found it running");
        return job;
    }
}
