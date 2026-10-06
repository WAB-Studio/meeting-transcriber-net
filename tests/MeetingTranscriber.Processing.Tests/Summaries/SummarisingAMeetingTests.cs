using System.Text.Json.Nodes;

using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Processing.Summaries.ClaudeCode;
using MeetingTranscriber.Processing.Tests.Summaries.ClaudeCode;

using static MeetingTranscriber.Processing.Tests.Summaries.JsonNodeMutations;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// <see cref="SummarisingAMeeting.SummariseAsync"/>: one call to a provider, turned into a
/// <see cref="SummaryEnded"/>, and — on a correctable refusal — the one hand-back it is owed.
/// </summary>
public class SummarisingAMeetingTests
{
    private static readonly UtcTimestamp Recorded =
        UtcTimestamp.From(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task A_summary_that_holds_up_is_filed_and_the_job_succeeds()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(Extracted(Accepted(meeting)));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);
        ended.RunId.ShouldNotBeNull();

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.Succeeded);

        var run = reopened.ExtractionRuns.Single(row => row.Id == ended.RunId);
        run.AcceptedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task What_is_sent_is_the_meeting_as_prepared_with_the_instructions_and_the_shape()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(Extracted(Accepted(meeting)));

        await SummariseAsync(corpus, jobId, provider);

        var request = provider.Requests.Single();
        request.Instructions.ShouldBe(ExtractionInstructions.ToExtract);
        request.Schema.ShouldBe(ExtractionInstructions.Schema);
        request.Correction.ShouldBeNull();
        request.Input.MeetingId.ShouldBe(meeting);
        request.Input.Turns.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_run_records_the_session_the_provider_opened()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(
            new SummaryProviderAnswer.Extracted(Utf8(Accepted(meeting)), "1.0", "opus", "session-42"));

        var ended = await SummariseAsync(corpus, jobId, provider);

        using var reopened = corpus.OpenMigrated();
        reopened.ExtractionRuns.Single(row => row.Id == ended.RunId).SessionId.ShouldBe("session-42");
    }

    [Fact]
    public async Task Without_a_summariser_nothing_is_sent_and_the_job_says_why()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(
            new SummaryProviderAnswer.NotAvailable("Claude Code was not found on this machine."));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.NotSent);
        ended.Failure.ShouldBe(JobFailure.NoSummariserOnThisMachine);
        ended.Said.ShouldBe("Claude Code was not found on this machine.");

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Running);
        reopened.ExtractionRuns.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task A_memory_file_in_the_way_sends_nothing_and_says_so()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(
            new SummaryProviderAnswer.MemoryInTheWay("A memory file sits above.", @"C:\work\CLAUDE.md"));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.NotSent);
        ended.Failure.ShouldBe(JobFailure.MemoryFileInTheWay);
        ended.Said.ShouldBe("A memory file sits above.");

        using var reopened = corpus.OpenMigrated();
        reopened.ExtractionRuns.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task A_summariser_that_did_not_answer_leaves_the_job_to_the_runner()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(new SummaryProviderAnswer.DidNotAnswer("it timed out"));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.DidNotAnswer);
        ended.Said.ShouldBe("it timed out");
        ended.RunId.ShouldBeNull();

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Running);
        reopened.ExtractionRuns.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task A_meeting_with_nothing_to_summarise_sends_nothing()
    {
        using var corpus = new TemporaryCorpus();
        Guid jobId;
        using (var context = corpus.OpenMigrated())
        {
            var meeting = MeetingRows.Recorded(context, Recorded, []);
            var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", Recorded);
            job.Start(Recorded);
            MeetingRows.Add(context, job);
            jobId = job.Id;
        }

        var provider = new FakeSummaries();

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.NotSent);
        ended.Failure.ShouldBe(JobFailure.CorpusRefused);
        provider.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_fake_and_Claude_Code_file_the_same_summary(bool throughClaudeCode)
    {
        using var corpus = new TemporaryCorpus();
        using var temporary = new TemporaryFolder();
        var (meeting, jobId) = Arrange(corpus);
        var wired = Providing(throughClaudeCode, temporary, Accepted(meeting));

        var ended = await SummariseAsync(corpus, jobId, wired);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.Succeeded);

        reopened.Decisions.Single(row => row.MeetingId == meeting).Statement.ShouldBe("Lanzar el viernes.");
    }

    /// <summary>ISC-115.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_summary_refused_for_its_shape_is_handed_back_once_and_accepted_corrected(bool throughClaudeCode)
    {
        using var corpus = new TemporaryCorpus();
        using var temporary = new TemporaryFolder();
        var (meeting, jobId) = Arrange(corpus);
        var broken = Accepted(meeting);
        Remove(broken, "abstract");

        var wired = Providing(throughClaudeCode, temporary, broken, Accepted(meeting));

        var ended = await SummariseAsync(corpus, jobId, wired);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);

        if (wired.Fake is { } fake)
        {
            fake.Requests.Count.ShouldBe(2);

            var correction = fake.Requests[1];
            correction.Instructions.ShouldBe(ExtractionInstructions.ToCorrect);
            correction.Correction.ShouldNotBeNull();
            correction.Correction!.PreviousOutput.ShouldBe(Utf8(broken));
            correction.Correction.WhatWasWrong.ShouldBe("- At abstract: this is not in the shape schema.md describes. Fix it.");
        }
        else
        {
            AskedTwiceWithTheFirstAnswerHandedBack(wired);
        }

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);
        reopened.ExtractionRuns.Count(row => row.JobId == jobId).ShouldBe(2);

        var second = reopened.ExtractionRuns.Single(row => row.Id == ended.RunId);
        var first = reopened.ExtractionRuns.Single(row => row.JobId == jobId && row.Id != second.Id);
        second.AcceptedAt.ShouldNotBeNull();
        second.CorrectsRunId.ShouldBe(first.Id);
        first.AcceptedAt.ShouldBeNull();
    }

    [Fact]
    public async Task No_model_chosen_asks_sonnet()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(Extracted(Accepted(meeting)));

        await SummariseAsync(corpus, jobId, provider);

        provider.Requests.Single().Model.ShouldBe("sonnet");
    }

    [Fact]
    public async Task The_model_chosen_in_settings_is_the_one_asked_for()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        using (var context = corpus.OpenMigrated())
        {
            new CorpusSettings(context).SummaryModel(SummaryModel.Opus, Recorded);
        }

        var broken = Accepted(meeting);
        Remove(broken, "abstract");
        var provider = new FakeSummaries().Answering(Extracted(broken), Extracted(Accepted(meeting)));

        await SummariseAsync(corpus, jobId, provider);

        // Both rounds, so one job asks one model.
        provider.Requests.Count.ShouldBe(2);
        provider.Requests.ShouldAllBe(request => request.Model == "opus");
    }

    [Fact]
    public async Task No_effort_chosen_asks_high()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().Answering(Extracted(Accepted(meeting)));

        await SummariseAsync(corpus, jobId, provider);

        provider.Requests.Single().Effort.ShouldBe("high");
    }

    [Fact]
    public async Task The_effort_chosen_is_the_one_asked_for()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        using (var context = corpus.OpenMigrated())
        {
            new CorpusSettings(context).SummaryEffort(SummaryEffort.Low, Recorded);
        }

        var broken = Accepted(meeting);
        Remove(broken, "abstract");
        var provider = new FakeSummaries().Answering(Extracted(broken), Extracted(Accepted(meeting)));

        await SummariseAsync(corpus, jobId, provider);

        // Both rounds, so one job asks one effort.
        provider.Requests.Count.ShouldBe(2);
        provider.Requests.ShouldAllBe(request => request.Effort == "low");
    }

    /// <summary>ISC-116.</summary>
    [Fact]
    public async Task A_statement_nothing_supports_comes_back_accepted_only_without_it()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var broken = Accepted(meeting);
        Remove(broken, "decisions[0].evidence");

        var corrected = Accepted(meeting);
        corrected["decisions"] = new JsonArray();

        var provider = new FakeSummaries().Answering(Extracted(broken), Extracted(corrected));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Succeeded);
        reopened.ExtractionRuns.Single(row => row.Id == ended.RunId).AcceptedAt.ShouldNotBeNull();
        reopened.Decisions.Any(row => row.ExtractionRunId == ended.RunId).ShouldBeFalse();
    }

    /// <summary>ISC-116, anti.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_statement_handed_back_that_comes_back_citing_something_else_is_refused(bool throughClaudeCode)
    {
        using var corpus = new TemporaryCorpus();
        using var temporary = new TemporaryFolder();
        var (meeting, jobId) = Arrange(corpus);
        var broken = Accepted(meeting);
        Remove(broken, "decisions[0].evidence");

        var corrected = BroughtBackAsAnAction(meeting);

        var wired = Providing(throughClaudeCode, temporary, broken, corrected);

        var ended = await SummariseAsync(corpus, jobId, wired);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);

        if (wired.Fake is { } fake)
        {
            fake.Requests.Count.ShouldBe(2);
        }
        else
        {
            AskedTwiceWithTheFirstAnswerHandedBack(wired);
        }

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.ExtractionRefused);

        reopened.ExtractionRefusals
            .Where(row => row.ExtractionRunId == ended.RunId)
            .Select(row => new { row.Condition, row.Path, row.Statement })
            .ShouldContain(row => row.Condition == ExtractionCondition.CitedAgainElsewhere
                && row.Path == "actions[0]" && row.Statement == "Lanzar el viernes.");
    }

    [Fact]
    public async Task An_input_that_was_not_the_one_prepared_is_not_handed_back()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new ProviderThatRendersMidCall(corpus.Root, meeting, Utf8(Accepted(meeting)));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.ExtractionRefused);

        reopened.ExtractionRefusals
            .Where(row => row.ExtractionRunId == ended.RunId)
            .Select(row => row.Condition)
            .ShouldContain(ExtractionCondition.InputNotAsPrepared);
    }

    [Fact]
    public async Task A_correction_that_is_refused_again_fails_the_job_and_nothing_is_asked_a_third_time()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var broken = Accepted(meeting);
        Remove(broken, "abstract");
        var stillBroken = Accepted(meeting);
        Remove(stillBroken, "abstract");

        var provider = new FakeSummaries().Answering(Extracted(broken), Extracted(stillBroken));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);
        provider.Requests.Count.ShouldBe(2);

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.ExtractionRefused);
        reopened.ExtractionRuns.Count(row => row.JobId == jobId).ShouldBe(2);
    }

    [Fact]
    public async Task A_summary_the_meeting_does_not_support_fails_its_job_for_good()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var aboutAnotherMeeting = Accepted(meeting);
        Set(aboutAnotherMeeting, "meeting_id", Guid.NewGuid().ToString());

        var provider = new FakeSummaries().Answering(Extracted(aboutAnotherMeeting));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.Filed);
        provider.Requests.Count.ShouldBe(1);

        using var reopened = corpus.OpenMigrated();
        var job = reopened.ProcessingJobs.Single(row => row.Id == jobId);
        job.State.ShouldBe(JobState.FailedPermanent);
        job.Failure.ShouldBe(JobFailure.ExtractionRefused);

        reopened.ExtractionRefusals
            .Where(row => row.ExtractionRunId == ended.RunId)
            .Select(row => row.Condition)
            .ShouldContain(ExtractionCondition.AnotherMeeting);
    }

    /// <summary>
    /// An adversarial review noted that the broad catch turning "anything else" into
    /// <c>DidNotAnswer</c> was untested against the door's own refusal — as opposed to the
    /// provider's — so nothing distinguished a real answering failure from an internal one before
    /// this. Both still read the same to the runner (Decides 8), but the message differs and is
    /// worth telling apart when diagnosing one.
    /// </summary>
    [Fact]
    public async Task An_internal_refusal_the_door_raises_reads_as_the_provider_not_answering()
    {
        using var corpus = new TemporaryCorpus();
        var (meeting, jobId) = Arrange(corpus);
        var provider = new ProviderThatCancelsTheJobMidCall(corpus.Root, jobId, Utf8(Accepted(meeting)));

        var ended = await SummariseAsync(corpus, jobId, provider);

        ended.Outcome.ShouldBe(SummaryOutcome.DidNotAnswer);
        ended.Said.ShouldNotBeNull().ShouldContain("is not a summary that has been started");

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Cancelled);
        reopened.ExtractionRuns.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_callers_own_cancellation_is_let_out()
    {
        using var corpus = new TemporaryCorpus();
        var (_, jobId) = Arrange(corpus);
        var provider = new FakeSummaries().ThatNeverAnswers();
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            SummarisingAMeeting.SummariseAsync(corpus.Root, jobId, provider, TimeProvider.System, cancel.Token));

        using var reopened = corpus.OpenMigrated();
        reopened.ProcessingJobs.Single(row => row.Id == jobId).State.ShouldBe(JobState.Running);
    }

    /// <summary>
    /// A provider answering <paramref name="outputs"/> in order: the in-process fake, or the real
    /// <see cref="ClaudeCodeSummaries"/> over a generated <c>claude.cmd</c> that answers the same.
    /// </summary>
    private static Wired Providing(bool throughClaudeCode, TemporaryFolder temporary, params JsonNode[] outputs)
    {
        if (!throughClaudeCode)
        {
            var fake = new FakeSummaries().Answering([.. outputs.Select(Extracted)]);
            return new Wired(fake, fake, null);
        }

        var cli = FakeClaudeCode.In(new DirectoryInfo(Path.Combine(temporary.Folder.FullName, "cli")));
        cli.AnswersVersion("fake 1")
            .Answers([.. outputs.Select(output => FakeClaudeCode.Envelope(output.ToJsonString()))]);

        var workspaces = new DirectoryInfo(Path.Combine(temporary.Folder.FullName, "workspaces"));
        var provider = new ClaudeCodeSummaries(
            () => cli.Executable, FakeClaudeCode.MinimalEnvironment(), workspaces, TimeSpan.FromSeconds(30));

        return new Wired(provider, null, cli);
    }

    /// <summary>
    /// The second call that carried a prompt was the hand-back, with the refused answer and what
    /// was wrong with it to read, and there was no third.
    /// </summary>
    private static void AskedTwiceWithTheFirstAnswerHandedBack(Wired wired)
    {
        var asked = wired.Cli!.Calls.Where(call => call.Arguments.Contains("-p")).ToList();

        asked.Count.ShouldBe(2);
        asked[1].Files.ShouldContain("previous-output.json");
        asked[1].Files.ShouldContain("what-was-wrong.md");
    }

    private static (Guid Meeting, Guid JobId) Arrange(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
        var meeting = MeetingRows.Recorded(
            context, Recorded,
            ["Este es el primer turno de la reunion.", "Y este es el segundo."],
            responseSha256: new string('a', 64));

        var job = ProcessingJob.Queue(Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/extract", Recorded);
        job.Start(Recorded);
        MeetingRows.Add(context, job);

        return (meeting, job.Id);
    }

    /// <summary>
    /// A red run through the real adapter says, besides what failed, whether the fake CLI ever ran:
    /// without it a machine nobody can sit at reports only that the summary was not filed.
    /// </summary>
    private static async Task<SummaryEnded> SummariseAsync(TemporaryCorpus corpus, Guid jobId, Wired wired)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            return await SummariseAsync(corpus, jobId, wired.Provider);
        }
        catch (Exception exception) when (wired.Cli is not null && exception is not OperationCanceledException)
        {
            throw new Xunit.Sdk.XunitException(
                string.Join(
                    Environment.NewLine,
                    exception.Message,
                    string.Empty,
                    $"After {clock.Elapsed.TotalSeconds:F1}s.",
                    wired.Cli.Diagnosis()),
                exception);
        }
    }

    private sealed record Wired(ISummaryProvider Provider, FakeSummaries? Fake, FakeClaudeCode? Cli);

    private static Task<SummaryEnded> SummariseAsync(TemporaryCorpus corpus, Guid jobId, ISummaryProvider provider) =>
        SummarisingAMeeting.SummariseAsync(
            corpus.Root, jobId, provider, TimeProvider.System, TestContext.Current.CancellationToken);

    private static SummaryProviderAnswer.Extracted Extracted(JsonNode document) =>
        new(Utf8(document), "1.0", "opus", null);

    private static JsonNode Accepted(Guid meetingId) => JsonNode.Parse($$"""
        {
          "schema_version": "1",
          "meeting_id": "{{meetingId}}",
          "abstract": "Se decidio la fecha de lanzamiento.",
          "summary": "",
          "participants": ["{{MeetingRows.SpeakerLabel}}"],
          "decisions": [
            {
              "statement": "Lanzar el viernes.",
              "evidence": {
                "utterance_ordinal": 0,
                "start_ms": 1000,
                "end_ms": 1500,
                "speaker_label": "{{MeetingRows.SpeakerLabel}}",
                "quoted_text": "primer turno"
              }
            }
          ],
          "actions": [],
          "open_questions": []
        }
        """)!;

    private static JsonNode BroughtBackAsAnAction(Guid meetingId) => JsonNode.Parse($$"""
        {
          "schema_version": "1",
          "meeting_id": "{{meetingId}}",
          "abstract": "Se decidio la fecha de lanzamiento.",
          "summary": "",
          "participants": ["{{MeetingRows.SpeakerLabel}}"],
          "decisions": [],
          "actions": [
            {
              "statement": "Lanzar el viernes.",
              "due_date": null,
              "evidence": {
                "utterance_ordinal": 1,
                "start_ms": 2000,
                "end_ms": 2500,
                "speaker_label": "{{MeetingRows.SpeakerLabel}}",
                "quoted_text": "segundo"
              }
            }
          ],
          "open_questions": []
        }
        """)!;

    /// <summary>
    /// A provider whose one call renders the meeting again mid-flight, so what
    /// <c>SummarisingAMeeting</c> already prepared and hashed no longer matches what the door reads
    /// when it re-prepares inside its own transaction.
    /// </summary>
    private sealed class ProviderThatRendersMidCall(DirectoryInfo root, Guid meetingId, byte[] output)
        : ISummaryProvider
    {
        public string Name => "mid-call-render";

        public Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping) =>
            Task.FromResult(new SummaryAvailability(Availability.Answers, "1", null));

        public Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping)
        {
            using (var context = CorpusDatabase.Open(root))
            {
                var turn = context.Utterances.Single(row => row.MeetingId == meetingId && row.Ordinal == 0);
                turn.Text += " Ahora renderizado de nuevo.";
                context.SaveChanges();
            }

            return Task.FromResult<SummaryProviderAnswer>(new SummaryProviderAnswer.Extracted(output, "1.0", "opus", null));
        }
    }

    /// <summary>
    /// A provider whose one call cancels the job it was sent for before answering, so the door's
    /// own re-read finds it no longer <c>Running</c> and refuses — an internal refusal, never a
    /// provider one, for <see cref="An_internal_refusal_the_door_raises_reads_as_the_provider_not_answering"/>
    /// to tell apart from the provider genuinely not answering.
    /// </summary>
    private sealed class ProviderThatCancelsTheJobMidCall(DirectoryInfo root, Guid jobId, byte[] output)
        : ISummaryProvider
    {
        public string Name => "cancels-mid-call";

        public Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping) =>
            Task.FromResult(new SummaryAvailability(Availability.Answers, "1", null));

        public Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping)
        {
            using (var context = CorpusDatabase.Open(root))
            {
                var job = context.ProcessingJobs.Single(row => row.Id == jobId);
                job.Cancel(UtcTimestamp.From(TimeProvider.System.GetUtcNow()));
                context.SaveChanges();
            }

            return Task.FromResult<SummaryProviderAnswer>(new SummaryProviderAnswer.Extracted(output, "1.0", "opus", null));
        }
    }
}
