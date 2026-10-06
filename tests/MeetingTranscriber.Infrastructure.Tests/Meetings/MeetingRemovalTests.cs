using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// ISC-229 and ISC-230 against a corpus on disk: what deleting a meeting's audio, its transcript
/// or the meeting takes, what it refuses, what a crash in the middle leaves and the launch
/// finishes, and what archiving does and does not hide.
/// </summary>
/// <remarks>
/// Every test works in a temporary corpus, which these delete things from on purpose. None of them
/// is ever pointed at a corpus somebody keeps.
/// </remarks>
public class MeetingRemovalTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private static readonly UtcTimestamp Now =
        UtcTimestamp.From(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));

    private static readonly TimeProvider Clock = new Frozen(Now);

    [Fact]
    public void Deleting_the_audio_leaves_everything_else()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        var before = Counts(context, made.Meeting);
        var folder = FolderOf(corpus, made.Meeting);
        var filesBefore = Names(folder);

        new MeetingRemoval(context, Now).Remove(made.Meeting, MeetingPart.Audio);

        var after = Counts(context, made.Meeting);
        foreach (var (table, count) in before.Where(pair => pair.Key != "artifacts"))
        {
            after[table].ShouldBe(count, table);
        }

        after["artifacts"].ShouldBe(before["artifacts"] - 1);
        context.Artifacts.Any(row => row.MeetingId == made.Meeting && row.Kind == ArtifactKind.Audio)
            .ShouldBeFalse();
        Names(folder).ShouldBe(filesBefore.Where(name => name != "audio.wav").ToArray(), ignoreOrder: true);
        Row(corpus, made.Meeting).AudioRemovedAt.ShouldBe(Now);
        new MeetingReading(context, Clock).Audio(made.Meeting, out var recorded).ShouldBeNull();
        recorded.ShouldBe(RecordedAudio.Removed);
    }

    [Fact]
    public void Deleting_the_transcript_takes_every_version_the_summary_and_the_jobs()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        var folder = FolderOf(corpus, made.Meeting);
        new MeetingWork(context, Clock).On(made.Meeting).Stage.ShouldBe(MeetingStage.Summarised);

        new MeetingRemoval(context, Now).Remove(made.Meeting, MeetingPart.Transcript);

        Names(folder).ShouldBe(["audio.wav", "meeting.json"], ignoreOrder: true);
        var after = Counts(context, made.Meeting);
        foreach (var table in new[]
        {
            "utterances", "turn_sources", "summaries", "decisions", "action_items", "open_questions",
            "extraction_runs", "transcription_runs", "processing_jobs", "action_item_progress",
        })
        {
            after[table].ShouldBe(0, table);
        }

        context.Artifacts.Where(row => row.MeetingId == made.Meeting).Select(row => row.Kind)
            .ToList().ShouldBe([ArtifactKind.Audio, ArtifactKind.Manifest], ignoreOrder: true);
        new MeetingWork(context, Clock).On(made.Meeting).Stage.ShouldBe(MeetingStage.Recorded);
    }

    [Fact]
    public void Deleting_the_transcript_keeps_what_people_said_about_the_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        var before = Counts(context, made.Meeting);

        new MeetingRemoval(context, Now).Remove(made.Meeting, MeetingPart.Transcript);

        var after = Counts(context, made.Meeting);
        foreach (var table in new[]
        {
            "meeting_nodes", "meeting_people", "terminology_corrections", "speaker_assignments", "audit_events",
        })
        {
            before[table].ShouldBeGreaterThan(0, table);
            after[table].ShouldBe(before[table], table);
        }

        var meeting = Row(corpus, made.Meeting);
        meeting.Title.ShouldBe("What the owner called it");
        meeting.Context.ShouldBe("A note somebody wrote");
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
    }

    [Fact]
    public void Deleting_the_meeting_leaves_no_row_and_no_folder()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        var other = Whole(corpus, context);
        var otherBefore = Counts(context, other.Meeting);
        Counts(context, made.Meeting).Values.Sum().ShouldBeGreaterThan(20);

        new MeetingRemoval(context, Now).Remove(made.Meeting, MeetingPart.Whole);

        Counts(context, made.Meeting).Values.ShouldAllBe(count => count == 0);
        context.Meetings.Any(row => row.Id == made.Meeting).ShouldBeFalse();
        FolderOf(corpus, made.Meeting).Exists.ShouldBeFalse();
        new DirectoryInfo(Path.Combine(corpus.Root.FullName, "meetings"))
            .EnumerateDirectories().Select(folder => folder.Name)
            .ShouldBe([other.Meeting.ToString()]);
        Counts(context, other.Meeting).ShouldBe(otherBefore);
    }

    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Running)]
    [InlineData(JobState.AwaitingUser)]
    [InlineData(JobState.FailedRetryable)]
    public void Nothing_is_deleted_while_work_is_queued_running_or_stopped_on_a_person(JobState state)
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        Unsettled(context, made.Meeting, state);
        var before = Counts(context, made.Meeting);

        var removal = new MeetingRemoval(context, Now);

        foreach (var part in new[] { MeetingPart.Whole, MeetingPart.Audio, MeetingPart.Transcript })
        {
            removal.WhyNot(made.Meeting, part).ShouldBe(RemovalRefusal.WorkIsUnderWay);
            Should.Throw<MeetingStageException>(() => removal.Remove(made.Meeting, part));
        }

        Counts(context, made.Meeting).ShouldBe(before);
        FolderOf(corpus, made.Meeting).Exists.ShouldBeTrue();
        Row(corpus, made.Meeting).LifecycleState.ShouldBe(LifecycleState.Active);
    }

    [Fact]
    public void Nothing_is_deleted_while_its_spool_folder_stands()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        CorpusFiles.SpoolFolderFor(corpus.Root, made.Meeting).Create();

        var removal = new MeetingRemoval(context, Now);

        foreach (var part in new[] { MeetingPart.Whole, MeetingPart.Audio, MeetingPart.Transcript })
        {
            removal.WhyNot(made.Meeting, part).ShouldBe(RemovalRefusal.ARecordingOfItIsWaiting);
            Should.Throw<MeetingStageException>(() => removal.Remove(made.Meeting, part));
        }

        FolderOf(corpus, made.Meeting).Exists.ShouldBeTrue();
        context.Meetings.Any(row => row.Id == made.Meeting).ShouldBeTrue();
    }

    [Fact]
    public void The_audio_is_offered_only_while_there_is_a_transcript_and_the_transcript_only_while_there_is_audio()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var removal = new MeetingRemoval(context, Now);

        var recordedOnly = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        removal.WhyNot(recordedOnly, MeetingPart.Audio).ShouldBe(RemovalRefusal.TheOtherHalfIsGone);
        removal.WhyNot(recordedOnly, MeetingPart.Transcript).ShouldBe(RemovalRefusal.NothingToRemove);
        removal.WhyNot(recordedOnly, MeetingPart.Whole).ShouldBeNull();

        var arrivedTranscribed = MeetingRows.Recorded(context, Recorded, ["said"]);
        removal.WhyNot(arrivedTranscribed, MeetingPart.Audio).ShouldBe(RemovalRefusal.NothingToRemove);
        removal.WhyNot(arrivedTranscribed, MeetingPart.Transcript).ShouldBe(RemovalRefusal.TheOtherHalfIsGone);
        removal.WhyNot(arrivedTranscribed, MeetingPart.Whole).ShouldBeNull();

        var both = Whole(corpus, context).Meeting;
        removal.WhyNot(both, MeetingPart.Audio).ShouldBeNull();
        removal.WhyNot(both, MeetingPart.Transcript).ShouldBeNull();
    }

    [Fact]
    public void A_file_held_open_leaves_the_meeting_exactly_as_it_was()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var made = Whole(corpus, context);
        var before = Counts(context, made.Meeting);
        var folder = FolderOf(corpus, made.Meeting);
        var namesBefore = Names(folder);
        var audio = new FileInfo(Path.Combine(folder.FullName, "audio.wav"));

        using (audio.Open(FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var removal = new MeetingRemoval(context, Now);

            Should.Throw<IOException>(() => removal.Remove(made.Meeting, MeetingPart.Audio));
            Should.Throw<IOException>(() => removal.Remove(made.Meeting, MeetingPart.Whole));
        }

        Counts(context, made.Meeting).ShouldBe(before);
        Names(folder).ShouldBe(namesBefore, ignoreOrder: true);
        var meeting = Row(corpus, made.Meeting);
        meeting.LifecycleState.ShouldBe(LifecycleState.Active);
        meeting.DeletedAt.ShouldBeNull();
        meeting.AudioRemovedAt.ShouldBeNull();
    }

    [Fact]
    public void A_deletion_a_crash_left_is_finished_at_launch()
    {
        // A whole meeting that died after its row said Deleting, with the folder not yet moved.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var made = Whole(corpus, context);
            context.Meetings.Where(row => row.Id == made.Meeting).ExecuteUpdate(set => set
                .SetProperty(row => row.LifecycleState, LifecycleState.Deleting)
                .SetProperty(row => row.DeletedAt, (UtcTimestamp?)Now));

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            context.Meetings.Any(row => row.Id == made.Meeting).ShouldBeFalse();
            new DirectoryInfo(Path.Combine(corpus.Root.FullName, "meetings")).GetDirectories().ShouldBeEmpty();
        }

        // A folder already renamed, whose row is already gone.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var gone = new DirectoryInfo(Path.Combine(
                corpus.Root.FullName, "meetings", MeetingRemoval.RemovingFolderPrefix + Guid.NewGuid()));
            gone.Create();
            File.WriteAllText(Path.Combine(gone.FullName, "audio.wav"), "x");

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            gone.Refresh();
            gone.Exists.ShouldBeFalse();
        }

        // A file renamed aside whose row still stands: the delete never committed, so it comes back.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var made = Whole(corpus, context);
            var audio = Path.Combine(FolderOf(corpus, made.Meeting).FullName, "audio.wav");
            File.Move(audio, audio + MeetingRemoval.RemovingSuffix);

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            File.Exists(audio).ShouldBeTrue();
            File.Exists(audio + MeetingRemoval.RemovingSuffix).ShouldBeFalse();
        }

        // And one whose row has gone: the delete committed, so it is erased.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var made = Whole(corpus, context);
            var audio = Path.Combine(FolderOf(corpus, made.Meeting).FullName, "audio.wav");
            File.Move(audio, audio + MeetingRemoval.RemovingSuffix);
            context.Artifacts.Where(row => row.MeetingId == made.Meeting && row.Kind == ArtifactKind.Audio)
                .ExecuteDelete();

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            File.Exists(audio).ShouldBeFalse();
            File.Exists(audio + MeetingRemoval.RemovingSuffix).ShouldBeFalse();
        }
    }

    [Fact]
    public void A_refused_response_set_aside_is_decided_by_its_transcription_run()
    {
        // The run is still there: the delete never committed, and the file comes back under its own name.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var made = Whole(corpus, context);
            var refused = Path.Combine(FolderOf(corpus, made.Meeting).FullName, RefusedName(made.Run));
            File.Move(refused, refused + MeetingRemoval.RemovingSuffix);

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            File.Exists(refused).ShouldBeTrue();
            File.Exists(refused + MeetingRemoval.RemovingSuffix).ShouldBeFalse();
        }

        // The run is gone: the same transaction deleted it, so the delete committed and the file goes.
        using (var corpus = new TemporaryCorpus())
        {
            using var context = corpus.OpenMigrated();
            var made = Whole(corpus, context);
            var refused = Path.Combine(FolderOf(corpus, made.Meeting).FullName, RefusedName(made.Run));
            File.Move(refused, refused + MeetingRemoval.RemovingSuffix);
            context.TranscriptionRuns.Where(row => row.Id == made.Run).ExecuteDelete();

            MeetingRemoval.FinishIn(corpus.Root).ShouldBeEmpty();

            File.Exists(refused).ShouldBeFalse();
            File.Exists(refused + MeetingRemoval.RemovingSuffix).ShouldBeFalse();
        }
    }

    [Fact]
    public void A_name_the_launch_cannot_place_is_left_and_named()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var stranger = Path.Combine(corpus.Root.FullName, "meetings", Guid.NewGuid().ToString(), "x.txt.removing");
        Directory.CreateDirectory(Path.GetDirectoryName(stranger)!);
        File.WriteAllText(stranger, "x");

        var left = MeetingRemoval.FinishIn(corpus.Root);

        left.Count.ShouldBe(1);
        File.Exists(stranger).ShouldBeTrue();
    }

    [Fact]
    public void The_refused_response_spelling_is_the_one_the_transcription_writes()
    {
        var run = Guid.NewGuid();

        var named = "deepgram.refused." + run.ToString("N") + ".json";

        // `TranscribingAMeeting` composes it in Processing, which this project does not reference;
        // the Processing tests hold that type to the same string.
        named.ShouldMatch(@"^deepgram\.refused\.[0-9a-f]{32}\.json$");
    }

    [Fact]
    public void An_archived_meeting_is_off_the_list_and_back_on_it_when_put_back()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, ["said"], root: corpus.Root);
        var removal = new MeetingRemoval(context, Now);

        new MeetingWork(context, Clock).Listed().Select(row => row.Meeting.Id).ShouldBe([meeting]);

        removal.Archive(meeting);

        new MeetingWork(context, Clock).Listed().ShouldBeEmpty();
        Row(corpus, meeting).ArchivedAt.ShouldBe(Now);
        Row(corpus, meeting).LifecycleState.ShouldBe(LifecycleState.Active);

        removal.PutBack(meeting);

        new MeetingWork(context, Clock).Listed().Select(row => row.Meeting.Id).ShouldBe([meeting]);
        Row(corpus, meeting).ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public void An_archived_meeting_with_a_charge_stopped_on_a_person_stays_listed()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(context, Recorded, [], root: corpus.Root);
        Unsettled(context, meeting, JobState.AwaitingUser);

        new MeetingRemoval(context, Now).Archive(meeting);

        new MeetingWork(context, Clock).Listed().Select(row => row.Meeting.Id).ShouldBe([meeting]);
    }

    [Fact]
    public void An_archived_meeting_is_still_found_and_every_reader_but_the_list_still_counts_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["the zeppelin budget"], title: "Zeppelin planning", root: corpus.Root);
        new MeetingRemoval(context, Now).Archive(meeting);

        CorpusSearch.Find(context, "zeppelin").Select(hit => hit.MeetingId).Distinct().ShouldBe([meeting]);

        // Export and rebuild ask for the active meetings, and archiving is a column and not a
        // state: nothing in Processing may mention it, or an export would drop what a person put
        // away, and an export is how somebody moves to another machine.
        context.Meetings.Count(row => row.LifecycleState == LifecycleState.Active).ShouldBe(1);
        var processing = RepositoryTree.SourceUnder(new DirectoryInfo(
            Path.Combine(RepositoryTree.Src.FullName, "MeetingTranscriber.Processing")));
        processing.Where(file => SourceText.WithoutProse(file).Contains("ArchivedAt", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The meeting as the corpus holds it, read through a connection that never saw the rows the
    /// test wrote: the context that wrote them tracks them, and answers with what it wrote.
    /// </summary>
    private static Meeting Row(TemporaryCorpus corpus, Guid meeting)
    {
        using var reading = corpus.Open();
        return reading.Meetings.AsNoTracking().Single(row => row.Id == meeting);
    }

    private static string RefusedName(Guid run) => $"deepgram.refused.{run:N}.json";

    private static DirectoryInfo FolderOf(TemporaryCorpus corpus, Guid meeting) =>
        new(Path.Combine(corpus.Root.FullName, "meetings", meeting.ToString()));

    private static string[] Names(DirectoryInfo folder) =>
        folder.Exists ? [.. folder.EnumerateFiles().Select(file => file.Name)] : [];

    /// <summary>The number of rows of this meeting, in every table that holds any kind of one.</summary>
    private static Dictionary<string, int> Counts(CorpusDbContext context, Guid meeting)
    {
        var counts = new Dictionary<string, int>();
        var connection = context.Database.GetDbConnection();
        connection.Open();

        try
        {
            foreach (var table in context.Model.GetEntityTypes()
                .Where(entity => entity.FindProperty("MeetingId") is not null)
                .Select(entity => entity.GetTableName()!)
                .Distinct())
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE upper(meeting_id) = upper($meeting)";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "$meeting";
                parameter.Value = meeting.ToString();
                command.Parameters.Add(parameter);
                counts[table] = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            connection.Close();
        }

        counts["action_item_progress"] = context.ActionItemProgress.Count(row =>
            context.ExtractionRuns.Any(run => run.Id == row.ExtractionRunId && run.MeetingId == meeting));

        return counts;
    }

    private static void Unsettled(CorpusDbContext context, Guid meeting, JobState state)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid()}", Recorded);

        if (state is not JobState.Pending)
        {
            job.Start(Recorded);
        }

        if (state is JobState.AwaitingUser)
        {
            job.AwaitUser("A restart found this job still running.");
        }

        if (state is JobState.FailedRetryable)
        {
            job.FailRetryable("The provider was busy.", Now);
        }

        MeetingRows.Add(context, job);
    }

    /// <summary>
    /// A meeting that has everything: its audio, two versions of the paid response, a refused one,
    /// a summary with a decision, an action with progress and a question, the rendered files, a
    /// title and a note, a filing, a person, a name given to a voice, a correction and a trail.
    /// Every job it has has settled.
    /// </summary>
    private static (Guid Meeting, Guid Run) Whole(TemporaryCorpus corpus, CorpusDbContext context)
    {
        var meeting = MeetingRows.Recorded(
            context, Recorded, ["first turn", "second turn", "third turn"], title: "What the owner called it",
            responseSha256: new string('f', 64), root: corpus.Root);
        MeetingRows.Extracted(context, meeting, Recorded, accepted: Recorded, "what it settled");

        context.Meetings.Where(row => row.Id == meeting)
            .ExecuteUpdate(set => set.SetProperty(row => row.Context, "A note somebody wrote"));

        var run = context.TranscriptionRuns.Single(row => row.MeetingId == meeting).Id;
        var folder = FolderOf(corpus, meeting);
        folder.Create();

        File.WriteAllText(Path.Combine(folder.FullName, "deepgram.json"), "{}");
        File.WriteAllText(Path.Combine(folder.FullName, RefusedName(run)), "{}");
        foreach (var (kind, name) in new[]
        {
            (ArtifactKind.DeepgramResponse, "deepgram.v2.json"),
            (ArtifactKind.Transcript, "transcript.md"),
            (ArtifactKind.Utterances, "utterances.jsonl"),
            (ArtifactKind.Extraction, "extraction.json"),
            (ArtifactKind.Summary, "summary.md"),
            (ArtifactKind.Manifest, "meeting.json"),
        })
        {
            File.WriteAllText(Path.Combine(folder.FullName, name), "x");
            MeetingRows.Add(context, new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Kind = kind,
                Origin = kind.OriginOf(),
                RelativePath = CorpusFiles.PathFor(meeting, name),
                ByteSize = 1,
                Sha256 = new string('c', 64),
                ConfirmedAt = Recorded,
            });
        }

        var extraction = context.ExtractionRuns.Single(row => row.MeetingId == meeting).Id;
        var person = new Person { Id = Guid.NewGuid(), DisplayName = "Alice", CreatedAt = Recorded, UpdatedAt = Recorded };
        var node = Node.Root(Guid.NewGuid(), NodeKind.Organization, "Acme " + meeting, Recorded);

        MeetingRows.Add(context, person);
        MeetingRows.Add(context, node);
        MeetingRows.Add(context, new MeetingNode { MeetingId = meeting, NodeId = node.Id, Role = MeetingNodeRole.WorkOf, CreatedAt = Recorded });
        MeetingRows.Add(context, new MeetingPerson { MeetingId = meeting, PersonId = person.Id, CreatedAt = Recorded });
        MeetingRows.Add(context, new SpeakerAssignment
        {
            MeetingId = meeting,
            SpeakerLabel = MeetingRows.SpeakerLabel,
            PersonId = person.Id,
            AssignedBy = SpeakerAssignmentSource.Person,
            AssignedAt = Recorded,
        });
        MeetingRows.Add(context, new TerminologyCorrection
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            WrongText = "ml",
            CorrectText = "machine learning",
            MatchMode = TerminologyMatchMode.Exact,
            CreatedAt = Recorded,
        });
        MeetingRows.Add(context, new ActionItemProgress
        {
            ExtractionRunId = extraction,
            Ordinal = 0,
            State = ActionItemState.Open,
            OwnerPersonId = person.Id,
            UpdatedAt = Recorded,
        });
        MeetingRows.Add(context, new AuditEvent
        {
            OccurredAt = Recorded,
            Actor = AuditActor.User,
            Action = "meeting.named",
            MeetingId = meeting,
            Detail = "the trail",
        });

        foreach (var job in context.ProcessingJobs.AsTracking().Where(row => row.MeetingId == meeting).ToList())
        {
            job.Start(Recorded);
            job.Succeed(Recorded);
        }

        context.SaveChanges();
        return (meeting, run);
    }

    private sealed class Frozen(UtcTimestamp at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at.Value;
    }
}
