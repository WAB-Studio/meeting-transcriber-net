using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

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
        var meeting = Record(context, corpus.Root);

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
        var meeting = Guid.NewGuid();

        Add(context, NewMeeting(meeting));
        Add(context, NewArtifact(meeting, ArtifactKind.Audio));

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
        var meeting = Record(context, corpus.Root);
        Summarise(context, meeting, accepted: true);

        var read = new MeetingReading(context, Clock).Of(meeting);
        var left = read.Screen.Left;

        left.Abstract.ShouldBe("what the meeting was about");
        left.Things.Count.ShouldBe(3);
        left.Things.ShouldAllBe(thing => thing.At > Duration.Zero);
        left.Of(LeftKind.Decision).Single().Says.ShouldBe("what was settled");
        left.Of(LeftKind.Action).Single().Says.ShouldBe("what is left to do");
        left.Of(LeftKind.Question).Single().Says.ShouldBe("what was left unresolved");

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
        var meeting = Record(context, corpus.Root);
        Summarise(context, meeting, accepted: false);

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
        var meeting = Record(context, corpus.Root);

        Summarise(context, meeting, accepted: true, at: Recorded, saying: "the first go");
        Summarise(
            context,
            meeting,
            accepted: true,
            at: UtcTimestamp.From(Recorded.Value.AddHours(1)),
            saying: "the second go");

        var left = new MeetingReading(context, Clock).Of(meeting).Screen.Left;

        left.Abstract.ShouldBe("the second go");
        left.Things.Count.ShouldBe(3);
    }

    [Fact]
    public void A_meeting_that_arrived_without_a_recording_says_so_rather_than_saying_it_is_lost()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Guid.NewGuid();

        Add(context, NewMeeting(meeting));
        Add(context, NewArtifact(meeting, ArtifactKind.DeepgramResponse));

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
        var meeting = Record(context, corpus.Root);
        Transcribe(context, meeting);
        Summarise(context, meeting, accepted: true);

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
        var meeting = Record(context, corpus.Root);
        Transcribe(context, meeting, finished: false);

        var wrote = new MeetingReading(context, Clock).Of(meeting).Screen.Left.Wrote;

        wrote.Transcriber.ShouldBeNull();
        wrote.TranscribedAt.ShouldBeNull();
    }

    [Fact]
    public void A_citation_opens_the_turns_around_the_one_it_anchors_on()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context, corpus.Root);

        for (var ordinal = 0; ordinal < 10; ordinal++)
        {
            Add(context, NewTurn(meeting, ordinal));
        }

        var around = new MeetingReading(context, Clock).Around(meeting, 5);

        around.Select(turn => turn.Ordinal).ShouldBe([3, 4, 5, 6, 7]);
    }

    [Fact]
    public void A_citation_at_the_start_of_a_meeting_opens_what_there_is()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context, corpus.Root);

        Add(context, NewTurn(meeting, 0));
        Add(context, NewTurn(meeting, 1));

        new MeetingReading(context, Clock).Around(meeting, 0)
            .Select(turn => turn.Ordinal)
            .ShouldBe([0, 1]);
    }

    [Fact]
    public void A_meeting_whose_turns_were_never_produced_opens_nothing_and_does_not_refuse()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context, corpus.Root);

        new MeetingReading(context, Clock).Around(meeting, 4).ShouldBeEmpty();
    }

    [Fact]
    public void A_name_typed_here_reaches_the_row_and_the_recovery_card()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context, corpus.Root);

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
        var meeting = Record(context, corpus.Root);
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
        var meeting = Record(context, corpus.Root);

        new MeetingReading(context, Clock).Name(meeting, "  Llamada con proveedor  ");

        context.Meetings.Single(row => row.Id == meeting).Title.ShouldBe("Llamada con proveedor");
    }

    [Fact]
    public void Naming_a_meeting_what_it_is_already_called_writes_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Record(context, corpus.Root);
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

    private static Guid Record(CorpusDbContext context, DirectoryInfo root)
    {
        var meeting = Guid.NewGuid();
        Add(context, NewMeeting(meeting));

        var audio = CorpusFiles.Locate(root, CorpusFiles.PathFor(meeting, "audio.wav"));
        audio.Directory!.Create();
        File.WriteAllBytes(audio.FullName, [0x52, 0x49, 0x46, 0x46]);

        Add(context, NewArtifact(meeting, ArtifactKind.Audio));
        return meeting;
    }

    private static void Transcribe(CorpusDbContext context, Guid meeting, bool finished = true)
    {
        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/1", Recorded);
        Add(context, job);

        Add(context, new TranscriptionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "deepgram",
            Model = "nova-3",
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            AudioSha256 = new string('b', 64),
            BillableConfigHash = new string('c', 64),
            CreatedAt = Recorded,
            FinishedAt = finished ? Recorded : null,
        });
    }

    /// <summary>
    /// One extraction, with a decision, an action and an open question, each cited to a turn of its
    /// own. The three sit at three different offsets so the order they come back in says something.
    /// </summary>
    private static Guid Summarise(
        CorpusDbContext context,
        Guid meeting,
        bool accepted,
        UtcTimestamp? at = null,
        string saying = "what the meeting was about")
    {
        var when = at ?? Recorded;
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/{Guid.NewGuid()}", when);

        Add(context, job);

        var run = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = when,
            AcceptedAt = accepted ? when : null,
        };

        Add(context, run);

        // The citations anchor on turns, so the turns have to be there: the corpus refuses a claim
        // whose citation lands nowhere, which is the rule this screen leans on.
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            if (!context.Utterances.Any(turn => turn.MeetingId == meeting && turn.Ordinal == ordinal))
            {
                Add(context, NewTurn(meeting, ordinal));
            }
        }

        Add(context, new Summary
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Abstract = saying,
            CreatedAt = when,
        });

        Add(context, new Decision
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Statement = "what was settled",
            Evidence = Cited(meeting, 0),
            CreatedAt = when,
        });

        Add(context, new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Statement = "what is left to do",
            Evidence = Cited(meeting, 1),
            CreatedAt = when,
        });

        Add(context, new OpenQuestion
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Question = "what was left unresolved",
            Evidence = Cited(meeting, 2),
            CreatedAt = when,
        });

        return run.Id;
    }

    private static Citation Cited(Guid meeting, int ordinal) => new()
    {
        MeetingId = meeting,
        UtteranceOrdinal = ordinal,
        Start = Duration.FromMilliseconds((ordinal + 1) * 1_000),
        End = Duration.FromMilliseconds(((ordinal + 1) * 1_000) + 500),
        SpeakerLabel = "ch1:speaker_0",
        QuotedText = "what was actually said there",
        SourceArtifactSha256 = new string('e', 64),
    };

    private static Utterance NewTurn(Guid meeting, int ordinal) => new()
    {
        Id = Guid.NewGuid(),
        MeetingId = meeting,
        Ordinal = ordinal,
        Start = Duration.FromMilliseconds((ordinal + 1) * 1_000),
        End = Duration.FromMilliseconds(((ordinal + 1) * 1_000) + 500),
        Channel = AudioChannel.Microphone,
        SpeakerLabel = "ch1:speaker_0",
        Text = $"turn {ordinal}",
    };

    private static void Add(CorpusDbContext context, object row)
    {
        context.Add(row);
        context.SaveChanges();
    }

    private static Meeting NewMeeting(Guid id) => new()
    {
        Id = id,
        StartedAt = Recorded,
        Duration = Duration.FromMilliseconds(1_360_000),
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
        RelativePath = CorpusFiles.PathFor(
            meeting, kind == ArtifactKind.Audio ? "audio.wav" : $"{kind}"),
        ByteSize = 4,
        Sha256 = new string('a', 64),
        ConfirmedAt = Recorded,
    };

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
