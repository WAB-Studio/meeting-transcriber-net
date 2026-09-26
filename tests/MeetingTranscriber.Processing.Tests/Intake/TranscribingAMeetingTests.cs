using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Tests.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Intake;

/// <summary>
/// A meeting being transcribed: the call, what it came to, and what the corpus is left holding.
/// Offline throughout — every send that touches a provider goes through <see cref="FakeDeepgram"/>
/// or a lambda, and the file never spells the client type.
/// </summary>
public sealed class TranscribingAMeetingTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    [Fact]
    public async Task A_transcription_files_the_paid_response_and_everything_read_out_of_it()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, FixtureBody(DeepgramFixtures.TwoChannelShort), TimeProvider.System,
            TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldBeNull();

        using var reopened = corpus.Open();
        var run = reopened.TranscriptionRuns.Single(row => row.JobId == job);
        run.FinishedAt.ShouldNotBeNull();
        run.ResponseArtifactId.ShouldNotBeNull();
        run.ApprovedAt.ShouldBeNull();

        reopened.Artifacts
            .Where(row => row.MeetingId == meeting)
            .Select(row => row.Kind)
            .ShouldContain(ArtifactKind.DeepgramResponse);
        reopened.Artifacts
            .Where(row => row.MeetingId == meeting)
            .Select(row => row.Kind)
            .ShouldContain(ArtifactKind.Transcript);

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    /// <summary>Goes red with the row written after the send.</summary>
    [Fact]
    public async Task A_transcription_records_what_it_asked_for_before_it_asks()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);

        SendingToTheProvider send = async (_, asked, response, stopping) =>
        {
            using var checking = corpus.Open();
            var run = checking.TranscriptionRuns.Single(row => row.JobId == job);
            run.BillableConfigHash.ShouldBe(asked.BillableConfigHash);
            run.ApprovedAt.ShouldBeNull();
            run.FinishedAt.ShouldBeNull();

            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
    }

    /// <summary>Goes red with step 2 answered after the send instead of before it.</summary>
    [Fact]
    public async Task A_meeting_that_already_has_a_response_is_not_sent_again()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, job;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingRows.Transcribed(context, meeting, When, responseSha256: new string('a', 64));
            job = QueueDirectly(context, meeting);
        }

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.AlreadyTranscribed);
        ended.Said.ShouldBeNull();
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task A_meeting_whose_audio_is_gone_is_refused_before_anything_is_sent()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        var audio = CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, RecordingFiles.Recording));
        File.Delete(audio.FullName);

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.NothingWasCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("Nothing was sent and nothing was charged");
        called.ShouldBeFalse();
    }

    [Fact]
    public async Task A_provider_that_was_never_reached_charged_nothing()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);
        using var fake = FakeDeepgram.NeverConnecting();

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, Through(fake), TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.NothingWasCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("nothing was charged");
    }

    [Fact]
    public async Task A_machine_with_no_key_sends_nothing_and_says_so()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);

        SendingToTheProvider send = (_, _, _, _) => throw new DeepgramKeyException(
            "There is no Deepgram key on this machine, so nothing can be transcribed until one "
            + "is kept.");

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.NothingWasCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("no Deepgram key");
    }

    /// <summary>Goes red with the catch removed, and red with the <c>TryDelete</c> removed.</summary>
    [Fact]
    public async Task A_run_the_corpus_would_not_record_is_never_sent_and_leaves_nothing_behind()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        using (var context = corpus.Open())
        {
            Sql.Execute(
                context,
                "CREATE TRIGGER refuse_every_run BEFORE INSERT ON transcription_runs "
                + "BEGIN SELECT RAISE(ABORT, 'refused for this test'); END;");
        }

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.NothingWasCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldEndWith("Nothing was sent and nothing was charged.");
        called.ShouldBeFalse();

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_call_that_may_have_been_charged_stops_on_what_it_cannot_know()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);

        SendingToTheProvider send = (_, _, _, _) =>
            throw new InvalidOperationException("the socket vanished");

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.MayHaveBeenCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("the socket vanished");
        ended.Said.ShouldContain("is not something this end can tell");
    }

    /// <summary>Goes red with the bytes written at <c>deepgram.json</c> instead of beside it.</summary>
    [Fact]
    public async Task A_send_that_failed_partway_leaves_no_half_written_file()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            await response.WriteAsync(new byte[] { 1, 2, 3, 4 }, stopping);
            throw new DeepgramCallException("the connection dropped mid-body", mayHaveBeenCharged: true);
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.MayHaveBeenCharged);

        var destination = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meeting, ResponseVersions.First));
        destination.Refresh();
        destination.Exists.ShouldBeFalse();

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    /// <summary>Goes red with the temporary removed instead of kept on this arm.</summary>
    [Fact]
    public async Task A_whole_response_the_corpus_would_not_file_is_kept_under_its_run()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            var bytes = "not json at all"u8.ToArray();
            await response.WriteAsync(bytes, stopping);
            return bytes.Length;
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.MayHaveBeenCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("paid for");
        ended.Said.ShouldContain("check names it");

        var folder = MeetingFolder(corpus, meeting);
        folder.EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
        folder.EnumerateFiles("deepgram.refused.*.json").Count().ShouldBe(1);

        using var reopened = corpus.Open();
        reopened.TranscriptionRuns.Single(row => row.JobId == job).LastError.ShouldNotBeNull();
    }

    /// <summary>Goes red when the outcome is decided by the throw alone.</summary>
    [Fact]
    public async Task A_response_filed_before_its_render_failed_is_filed_and_kept_once()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        // Something standing where the transcript goes, the same way OwedRendersTests forces a
        // render to fail after its artifact is already committed.
        Directory.CreateDirectory(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "transcript.md")).FullName);

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, FixtureBody(DeepgramFixtures.TwoChannelShort), TimeProvider.System,
            TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("The next launch renders it again");

        using var reopened = corpus.Open();
        reopened.Artifacts
            .Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(1);

        var run = reopened.TranscriptionRuns.Single(row => row.JobId == job);
        run.FinishedAt.ShouldNotBeNull();
        run.ResponseArtifactId.ShouldNotBeNull();

        var folder = MeetingFolder(corpus, meeting);
        folder.EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
        folder.EnumerateFiles("deepgram.refused.*.json").ShouldBeEmpty();
    }

    /// <summary>Goes red when the check accepts any response row rather than this one's own hash.</summary>
    [Fact]
    public async Task A_response_somebody_else_filed_while_the_call_was_out_is_not_taken_for_this_one()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            // Somebody else's response lands on this meeting while this call is still out.
            using var elsewhere = corpus.Open();
            MeetingIntake.ReceiveInto(
                elsewhere,
                meeting,
                new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe)),
                When);

            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.MayHaveBeenCharged);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("paid for");

        MeetingFolder(corpus, meeting).EnumerateFiles("deepgram.refused.*.json").Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_call_somebody_stopped_is_handed_back_as_a_cancellation()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);
        using var cancelling = new CancellationTokenSource();

        SendingToTheProvider send = (_, _, _, stopping) =>
        {
            cancelling.Cancel();
            stopping.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        };

        await Should.ThrowAsync<OperationCanceledException>(() =>
            TranscribingAMeeting.TranscribeAsync(
                corpus.Root, job, send, TimeProvider.System, cancelling.Token));

        using var reopened = corpus.Open();
        var run = reopened.TranscriptionRuns.Single(row => row.JobId == job);
        run.LastError.ShouldBeNull();
        run.FinishedAt.ShouldBeNull();
        reopened.ProcessingJobs.Single(row => row.Id == job).State.ShouldBe(JobState.Running);

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_job_nobody_started_is_never_sent()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;

        using (var context = corpus.OpenMigrated())
        {
            var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            var job = new MeetingWork(context, When).Take(meeting);
            context.SaveChanges();
            jobId = job.Id;
        }

        var called = false;
        SendingToTheProvider send = (_, _, _, _) =>
        {
            called = true;
            return Task.FromResult(0L);
        };

        await Should.ThrowAsync<InvalidOperationException>(() =>
            TranscribingAMeeting.TranscribeAsync(
                corpus.Root, jobId, send, TimeProvider.System, TestContext.Current.CancellationToken));

        called.ShouldBeFalse();
    }

    /// <summary>Goes red with the write handle opened with <c>FileShare.Delete</c>.</summary>
    [Fact]
    public async Task A_response_waiting_to_be_filed_cannot_be_swept()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);

            var path = ((FileStream)response).Name;
            Should.Throw<IOException>(() => File.Delete(path));

            return body.Length;
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
    }

    /// <summary>Goes red with <c>stopping</c> handed to anything after the send.</summary>
    [Fact]
    public async Task A_token_cancelled_once_the_response_has_arrived_still_files_it()
    {
        using var corpus = new TemporaryCorpus();
        var (_, job) = Queue(corpus);
        using var cancelling = new CancellationTokenSource();

        SendingToTheProvider send = async (_, _, response, stopping) =>
        {
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort));
            await body.CopyToAsync(response, stopping);
            cancelling.Cancel();
            return body.Length;
        };

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, send, TimeProvider.System, cancelling.Token);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
    }

    /// <summary>The card's first Proof.</summary>
    [Fact]
    public async Task Transcribing_again_files_a_new_version_beside_the_first_and_leaves_it_untouched()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, job;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
            job = QueueDirectly(context, meeting);
        }

        var approvedAt = When + Duration.FromSeconds(1);

        var ended = await TranscribingAMeeting.TranscribeAgainAsync(
            corpus.Root, job, approvedAt, FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe),
            TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldBeNull();

        using var reopened = corpus.Open();

        var firstRow = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.First));
        var firstSha = CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)));
        firstRow.Sha256.ShouldBe(firstSha);
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, firstRow.RelativePath)).ShouldBe(firstSha);

        var secondRow = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting
            && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)));
        secondRow.Sha256.ShouldBe(
            CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe))));

        reopened.TranscriptionRuns.Single(row => row.JobId == job).ApprovedAt.ShouldBe(approvedAt);

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    /// <summary>Goes red when the two paths share the sentence, and red when the render-failed
    /// branch stops marking the run finished.</summary>
    [Fact]
    public async Task A_render_that_fails_after_another_version_is_filed_does_not_promise_a_launch_will_fix_it()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, job;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
            job = QueueDirectly(context, meeting);
        }

        var transcript = CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(meeting, "transcript.md"));
        transcript.Delete();
        Directory.CreateDirectory(transcript.FullName);

        var ended = await TranscribingAMeeting.TranscribeAgainAsync(
            corpus.Root, job, When + Duration.FromSeconds(1), FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe),
            TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("version 2");
        ended.Said.ShouldContain("render ");
        ended.Said.ShouldContain(meeting.ToString());
        ended.Said.ShouldNotContain("next launch");
        ended.Said.ShouldNotContain("still reads");

        using var reopened = corpus.Open();
        var secondRow = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting
            && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)));

        var run = reopened.TranscriptionRuns.Single(row => row.JobId == job);
        run.FinishedAt.ShouldNotBeNull();
        run.ResponseArtifactId.ShouldBe(secondRow.Id);

        new MeetingReading(reopened, TimeProvider.System).TranscribedFrom(meeting).ShouldBe(secondRow.Sha256);
    }

    [Fact]
    public async Task Transcribing_again_moves_what_the_meeting_says_its_turns_came_from()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting, job;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
            MeetingIntake.ReceiveInto(
                context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
            job = QueueDirectly(context, meeting);
        }

        var ended = await TranscribingAMeeting.TranscribeAgainAsync(
            corpus.Root, job, When + Duration.FromSeconds(1), FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe),
            TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);

        using var reopened = corpus.Open();
        var secondSha = reopened.Artifacts.Single(row =>
                row.MeetingId == meeting
                && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)))
            .Sha256;

        new MeetingReading(reopened, TimeProvider.System).TranscribedFrom(meeting).ShouldBe(secondSha);
    }

    /// <summary>O-20260925-13. Goes red when the guard is removed: the exception escapes and the
    /// test throws.</summary>
    [Fact]
    public async Task A_response_filed_whose_run_could_not_be_updated_is_still_filed_and_says_so()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        using (var context = corpus.Open())
        {
            Sql.Execute(
                context,
                "CREATE TRIGGER refuse_every_update BEFORE UPDATE ON transcription_runs "
                + "BEGIN SELECT RAISE(ABORT, 'refused for this test'); END;");
        }

        var ended = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, FixtureBody(DeepgramFixtures.TwoChannelShort), TimeProvider.System,
            TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldContain("filed");
        ended.Said.ShouldContain("could not be written");

        using var reopened = corpus.Open();
        reopened.Artifacts
            .Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(1);
        reopened.TranscriptionRuns.Single(row => row.JobId == job).FinishedAt.ShouldBeNull();

        MeetingFolder(corpus, meeting).EnumerateFiles($"*{CorpusFiles.UnfinishedSuffix}").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_re_transcription_whose_run_could_not_be_recorded_says_its_turns_are_named_for_the_response_before()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, job) = Queue(corpus);

        var first = await TranscribingAMeeting.TranscribeAsync(
            corpus.Root, job, FixtureBody(DeepgramFixtures.TwoChannelShort), TimeProvider.System,
            TestContext.Current.CancellationToken);
        first.Outcome.ShouldBe(TranscriptionOutcome.Filed);

        string firstSha;
        Guid againJob;

        using (var context = corpus.Open())
        {
            firstSha = context.Artifacts.Single(row =>
                row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse).Sha256;

            Sql.Execute(
                context,
                "CREATE TRIGGER refuse_every_update BEFORE UPDATE ON transcription_runs "
                + "BEGIN SELECT RAISE(ABORT, 'refused for this test'); END;");

            againJob = QueueDirectly(context, meeting);
        }

        var ended = await TranscribingAMeeting.TranscribeAgainAsync(
            corpus.Root, againJob, When + Duration.FromSeconds(1), FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe),
            TimeProvider.System, TestContext.Current.CancellationToken);

        ended.Outcome.ShouldBe(TranscriptionOutcome.Filed);
        ended.Said.ShouldNotBeNull();
        ended.Said.ShouldEndWith(
            "Until that record is written, what this meeting says its turns were read from still "
            + "names the response before this one.");

        using var reopened = corpus.Open();
        new MeetingReading(reopened, TimeProvider.System).TranscribedFrom(meeting).ShouldBe(firstSha);
    }

    private static (Guid Meeting, Guid Job) Queue(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
        var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
        var job = RecordedMeetings.Started(context, meeting, When);
        return (meeting, job);
    }

    /// <summary>
    /// A job queued directly rather than through <c>MeetingWork</c>, for a meeting that no longer
    /// offers <c>Transcribe</c> as its next stage — <c>MeetingRows.Transcribed</c>'s own shape.
    /// </summary>
    private static Guid QueueDirectly(CorpusDbContext context, Guid meeting)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid():n}", When);
        context.ProcessingJobs.Add(job);
        job.Start(When);
        context.SaveChanges();
        return job.Id;
    }

    private static SendingToTheProvider FixtureBody(string fixture) =>
        async (_, _, response, stopping) =>
        {
            await using var body = File.OpenRead(DeepgramFixtures.PathOf(fixture));
            await body.CopyToAsync(response, stopping);
            return body.Length;
        };

    private static SendingToTheProvider Through(FakeDeepgram fake) =>
        (audio, asked, response, stopping) =>
            new DeepgramTranscription(fake.Client())
                .SendAsync(audio, asked, "not-a-real-key", response, stopping);

    private static DirectoryInfo MeetingFolder(TemporaryCorpus corpus, Guid meeting) =>
        new(Path.Combine(corpus.Root.FullName, CorpusFiles.Meetings, meeting.ToString()));
}
