using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;

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
    /// A transcript says which paid response produced it, off the run that finished and not off the
    /// newest response filed against the meeting.
    /// </summary>
    /// <remarks>
    /// The two come apart exactly where it matters. A meeting transcribed a second time has a newer
    /// response on disk from the moment the job confirms it, and its turns are still the first
    /// response's until the projection is rebuilt — so answering from the artifact hands a reader a
    /// hash the quotation is not in, which is a corpus inconsistency that does not exist. Red the
    /// day this reads the artifacts table instead of the run.
    /// </remarks>
    [Fact]
    public void A_transcript_says_which_response_the_run_that_finished_produced_it_from()
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
