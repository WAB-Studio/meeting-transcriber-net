using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Meetings;

/// <summary>
/// ISC-167, and what the screen a meeting is read from shows at each stage.
/// </summary>
/// <remarks>
/// The claim is an anti: playing back what a meeting recorded never requires a transcription to
/// have been paid for. What would make it false is this screen coming to consult one, so the probe
/// is the whole cross of stage against every state a job can be in — a transcription is a job, and
/// a screen that started asking about one would show it here first.
/// </remarks>
public class MeetingScreenTests
{
    private static readonly Guid Meeting = Guid.NewGuid();

    private static readonly UtcTimestamp Then =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    public static TheoryData<MeetingStage, RecordedAudio> EveryStageAgainstEveryRecording()
    {
        var cross = new TheoryData<MeetingStage, RecordedAudio>();

        foreach (var stage in Enum.GetValues<MeetingStage>())
        {
            foreach (var recording in Enum.GetValues<RecordedAudio>())
            {
                cross.Add(stage, recording);
            }
        }

        return cross;
    }

    public static TheoryData<JobState> EveryJobState() => [.. Enum.GetValues<JobState>()];

    [Theory]
    [MemberData(nameof(EveryStageAgainstEveryRecording))]
    public void Whether_a_meeting_plays_is_whether_its_recording_is_there(
        MeetingStage stage, RecordedAudio recording)
    {
        var screen = Screen(new OwedWork(Meeting, stage, StageStanding.Offered), recording);

        // The file and nothing else. Every stage answers the same way, so no stage of a meeting is
        // one where hearing what it recorded has to be bought first.
        screen.MayBePlayedBack.ShouldBe(recording is RecordedAudio.Playable);
    }

    [Theory]
    [MemberData(nameof(EveryJobState))]
    public void What_a_transcription_is_doing_never_decides_whether_a_meeting_plays(JobState state)
    {
        var job = ProcessingJob.Queue(Guid.NewGuid(), Meeting, JobKind.Transcribe, "one", Then);
        Move(job, state);

        var screen = Screen(OwedWork.Of(Meeting, [ArtifactKind.Audio], [job]), RecordedAudio.Playable);

        // A transcription that came back moves the meeting up a rung and every other state leaves
        // it where it was. Both of those play, which is the point: the answer does not move.
        screen.Stage.ShouldBe(state is JobState.Succeeded
            ? MeetingStage.Transcribed
            : MeetingStage.Recorded);

        screen.MayBePlayedBack.ShouldBeTrue();
    }

    [Fact]
    public void A_meeting_nothing_was_ever_bought_for_plays_exactly_as_a_summarised_one_does()
    {
        var recorded = Screen(OwedWork.Of(Meeting, [ArtifactKind.Audio], []), RecordedAudio.Playable);

        var summarised = Screen(
            OwedWork.Of(
                Meeting,
                [ArtifactKind.Audio, ArtifactKind.DeepgramResponse, ArtifactKind.Extraction],
                []),
            RecordedAudio.Playable);

        recorded.Stage.ShouldBe(MeetingStage.Recorded);
        summarised.Stage.ShouldBe(MeetingStage.Summarised);
        recorded.MayBePlayedBack.ShouldBe(summarised.MayBePlayedBack);
        recorded.MayBePlayedBack.ShouldBeTrue();
    }

    [Fact]
    public void A_meeting_read_as_transcribed_whose_recording_has_gone_does_not_play()
    {
        // The case that says why this is not read off the stage: a meeting passes a rung when a
        // job came back saying so as well as when a file is there, so its stage can be above the
        // audio it no longer has.
        var job = ProcessingJob.Queue(Guid.NewGuid(), Meeting, JobKind.Transcribe, "one", Then);
        job.Start(Then);
        job.Succeed(Then);

        var screen = Screen(
            OwedWork.Of(Meeting, [], [job]), RecordedAudio.NotWhereTheCorpusSaysItIs);

        screen.Stage.ShouldBe(MeetingStage.Transcribed);
        screen.MayBePlayedBack.ShouldBeFalse();
        screen.ThereIsATranscription.ShouldBeTrue();
        screen.ThereIsASummary.ShouldBeFalse();
    }

    [Fact]
    public void A_meeting_with_no_audio_yet_is_neither_played_nor_named()
    {
        var screen = Screen(OwedWork.Of(Meeting, [], []), RecordedAudio.NoneYet);

        screen.Stage.ShouldBe(MeetingStage.Recording);
        screen.MayBePlayedBack.ShouldBeFalse();
        screen.TheNameMayBeTyped.ShouldBeFalse();
    }

    /// <summary>
    /// A meeting still being recorded is not one to file, and every other stage is.
    /// </summary>
    /// <remarks>
    /// The same window the name is kept out of: while the recording is still being filed the
    /// application is writing rows about this meeting, and links written into that window race the
    /// save that made it.
    /// </remarks>
    [Fact]
    public void A_meeting_still_being_recorded_is_not_one_to_file()
    {
        foreach (var stage in Enum.GetValues<MeetingStage>())
        {
            Screen(new OwedWork(Meeting, stage, StageStanding.Offered), RecordedAudio.Playable)
                .ItMayBeFiled
                .ShouldBe(stage is not MeetingStage.Recording, stage.ToString());
        }
    }

    [Fact]
    public void A_recorded_meeting_offers_the_transcription_and_the_name()
    {
        var screen = Screen(OwedWork.Of(Meeting, [ArtifactKind.Audio], []), RecordedAudio.Playable);

        screen.TheActOffered.ShouldBe(JobKind.Transcribe);
        screen.TheActMayBeLeft.ShouldBeTrue();
        screen.TheNameMayBeTyped.ShouldBeTrue();
    }

    [Fact]
    public void A_summarised_meeting_offers_nothing_to_buy_and_still_reads()
    {
        var screen = Screen(
            OwedWork.Of(
                Meeting,
                [ArtifactKind.Audio, ArtifactKind.DeepgramResponse, ArtifactKind.Extraction],
                []),
            RecordedAudio.Playable);

        screen.TheActOffered.ShouldBeNull();
        screen.TheActMayBeLeft.ShouldBeFalse();
        screen.MayBePlayedBack.ShouldBeTrue();
        screen.TheNameMayBeTyped.ShouldBeTrue();
    }

    [Fact]
    public void A_stage_stopped_on_a_person_is_never_offered_again_from_this_screen()
    {
        var job = ProcessingJob.Queue(Guid.NewGuid(), Meeting, JobKind.Transcribe, "one", Then);
        job.Start(Then);
        job.AwaitUser("a charge that may already have happened");

        var screen = Screen(OwedWork.Of(Meeting, [ArtifactKind.Audio], [job]), RecordedAudio.Playable);

        screen.TheActOffered.ShouldBeNull();
        screen.TheActMayBeLeft.ShouldBeFalse();

        // And it still plays. Hearing what was recorded has nothing to do with a charge nobody has
        // settled, which is ISC-167 said on the loudest standing there is.
        screen.MayBePlayedBack.ShouldBeTrue();
    }

    [Fact]
    public void Every_thing_the_ai_left_is_marked_where_it_was_said()
    {
        var left = new WhatTheAiLeft(
            "what it was about",
            WhatTheAiLeft.InTheOrderTheyWereSaid([
                Thing(LeftKind.Question, 52_000, 9),
                Thing(LeftKind.Decision, 4_000, 1),
                Thing(LeftKind.Action, 38_000, 7),
            ]),
            WhoWroteThis.Nobody);

        var screen = new MeetingScreen(
            OwedWork.Of(
                Meeting,
                [ArtifactKind.Audio, ArtifactKind.DeepgramResponse, ArtifactKind.Extraction],
                []),
            left,
            RecordedAudio.Playable);

        screen.MarkedAlongTheMeeting.Count.ShouldBe(3);
        screen.MarkedAlongTheMeeting.ShouldBe([
            Duration.FromMilliseconds(4_000),
            Duration.FromMilliseconds(38_000),
            Duration.FromMilliseconds(52_000),
        ]);
    }

    [Fact]
    public void Things_said_at_the_same_moment_come_out_in_the_same_order_every_time()
    {
        // Two runs over the same rows in different orders, and every key equal but the last. A
        // list that shuffled under somebody between one look at a meeting and the next would be
        // this screen redrawing, not the corpus changing.
        LeftThing[] rows =
        [
            Thing(LeftKind.Decision, 12_000, 4, "the second thing settled"),
            Thing(LeftKind.Decision, 12_000, 4, "the first thing settled"),
            Thing(LeftKind.Question, 12_000, 4),
            Thing(LeftKind.Action, 12_000, 4),
        ];

        var one = WhatTheAiLeft.InTheOrderTheyWereSaid(rows);
        var other = WhatTheAiLeft.InTheOrderTheyWereSaid(rows.Reverse());

        one.Select(thing => thing.Says).ShouldBe(other.Select(thing => thing.Says));
        one.Select(thing => thing.Kind).ShouldBe(
            [LeftKind.Decision, LeftKind.Decision, LeftKind.Action, LeftKind.Question]);
    }

    [Fact]
    public void A_section_gets_its_own_and_nobody_elses()
    {
        var left = new WhatTheAiLeft(
            null,
            WhatTheAiLeft.InTheOrderTheyWereSaid([
                Thing(LeftKind.Decision, 1_000, 1),
                Thing(LeftKind.Decision, 3_000, 3),
                Thing(LeftKind.Question, 2_000, 2),
            ]),
            WhoWroteThis.Nobody);

        left.Of(LeftKind.Decision).Count.ShouldBe(2);
        left.Of(LeftKind.Question).Count.ShouldBe(1);
        left.Of(LeftKind.Action).ShouldBeEmpty();
    }

    [Fact]
    public void A_meeting_nothing_has_been_made_of_has_nothing_left_of_it()
    {
        WhatTheAiLeft.Nothing.Things.ShouldBeEmpty();
        WhatTheAiLeft.Nothing.Abstract.ShouldBeNull();
        WhatTheAiLeft.Nothing.MarkedAlongTheMeeting.ShouldBeEmpty();
        WhatTheAiLeft.Nothing.Wrote.ShouldBe(WhoWroteThis.Nobody);
    }

    private static MeetingScreen Screen(OwedWork owed, RecordedAudio recording) =>
        new(owed, WhatTheAiLeft.Nothing, recording);

    private static LeftThing Thing(
        LeftKind kind, long at, int ordinal, string says = "what it said") =>
        new(kind, says, Duration.FromMilliseconds(at), ordinal, "what was said there", "ch1:speaker_0");

    /// <summary>
    /// Walks a job to the state asked for, through its own methods and never around them.
    /// </summary>
    private static void Move(ProcessingJob job, JobState state)
    {
        switch (state)
        {
            case JobState.Pending:
                return;
            case JobState.Running:
                job.Start(Then);
                return;
            case JobState.AwaitingUser:
                job.Start(Then);
                job.AwaitUser("uncertain");
                return;
            case JobState.Succeeded:
                job.Start(Then);
                job.Succeed(Then);
                return;
            case JobState.FailedRetryable:
                job.Start(Then);
                job.FailRetryable("try again", Then);
                return;
            case JobState.FailedPermanent:
                job.Start(Then);
                job.FailPermanently("no", Then);
                return;
            case JobState.Cancelled:
                job.Cancel(Then);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown job state.");
        }
    }
}
