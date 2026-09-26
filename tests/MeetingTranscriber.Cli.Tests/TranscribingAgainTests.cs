using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Jobs;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// <c>transcribe-again</c>, the second command that spends money — proved without spending any, the
/// same way <see cref="LiveDeepgramTests"/> proves <c>deepgram-live</c>.
/// </summary>
/// <remarks>
/// Every fact drives the public four-argument
/// <see cref="DeepgramCommands.TranscribeAgain(Arguments, TextWriter, Func{string}, SendingToTheProvider)"/>
/// with a keyboard and a <see cref="SendingToTheProvider"/> of its own, except the three refusals
/// that happen before the prompt is ever reached, which drive <see cref="CommandLine.Of"/> instead —
/// <see cref="CommandLine.Of"/> reaches the console keyboard, which answers null under a redirected
/// host, so it is safe only for a refusal on that side of the confirmation.
/// </remarks>
public sealed class TranscribingAgainTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    [Fact]
    public void Nothing_is_sent_unless_the_minutes_are_typed_back()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);
        using var output = new StringWriter();
        var asked = 0;

        var code = DeepgramCommands.TranscribeAgain(
            Args(meeting, corpus), output, () => "59", (_, _, _, _) =>
            {
                asked++;
                throw new InvalidOperationException("nothing here may reach the network");
            });

        code.ShouldBe(Cli.Ok);
        asked.ShouldBe(0);
        output.ToString().ShouldContain("not sent");

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Count(job => job.MeetingId == meeting && job.Kind == JobKind.Transcribe)
            .ShouldBe(0);
        reopened.Artifacts.Count(row => row.MeetingId == meeting && row.Kind == ArtifactKind.DeepgramResponse)
            .ShouldBe(1);
    }

    /// <summary>Pairs with the fact above: the key is read inside <c>send</c>, and never before it.</summary>
    [Fact]
    public void The_key_is_not_read_before_the_minutes_are_typed_back()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);
        using var output = new StringWriter();
        var asked = 0;

        DeepgramCommands.TranscribeAgain(
            Args(meeting, corpus), output, () => "not a number", (_, _, _, _) =>
            {
                asked++;
                throw new InvalidOperationException("nothing here may reach the network");
            });

        asked.ShouldBe(0);
    }

    /// <summary>The card's Proof through its own door.</summary>
    [Fact]
    public void Typing_the_minutes_back_files_a_second_version_beside_the_first()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);
        using var output = new StringWriter();

        var code = DeepgramCommands.TranscribeAgain(
            Args(meeting, corpus), output, () => "60", FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe));

        code.ShouldBe(Cli.Ok);
        new Run(code, output.ToString(), string.Empty).Value("version").ShouldBe("2");

        using var reopened = corpus.Open();

        var first = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.First));
        first.Sha256.ShouldBe(CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort))));
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, first.RelativePath)).ShouldBe(first.Sha256);

        var second = reopened.Artifacts.Single(row =>
            row.MeetingId == meeting
            && row.RelativePath == CorpusFiles.PathFor(meeting, ResponseVersions.Named(2)));
        second.Sha256.ShouldBe(
            CorpusFiles.Sha256Of(new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe))));

        var job = reopened.ProcessingJobs.Single(row =>
            row.MeetingId == meeting && row.Kind == JobKind.Transcribe && row.State == JobState.Succeeded);
        var run = reopened.TranscriptionRuns.Single(row => row.JobId == job.Id);
        run.ApprovedAt.ShouldNotBeNull();

        var expected = Turns.Group(DeepgramTranscriptParser.ParseFile(
            DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe),
            DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe)).Segments);

        var stored = reopened.Utterances
            .Where(turn => turn.MeetingId == meeting)
            .OrderBy(turn => turn.Ordinal)
            .AsEnumerable()
            .Select(turn => new Turn(
                turn.Ordinal, turn.Start, turn.End, turn.Channel, turn.SpeakerLabel, turn.Text, turn.Confidence))
            .ToArray();

        stored.ShouldBe(expected);
    }

    [Fact]
    public void A_meeting_never_transcribed_is_refused_before_anybody_is_asked()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting;

        using (var context = corpus.OpenMigrated())
        {
            meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
        }

        var run = CommandLine.Of("transcribe-again", meeting.ToString(), "--corpus", corpus.Root.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("has not been transcribed yet");
    }

    [Fact]
    public void A_corpus_whose_queue_another_process_is_running_is_refused_before_anybody_is_asked()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);

        using var held = RunnerLease.TryTake(corpus.Root);

        var run = CommandLine.Of("transcribe-again", meeting.ToString(), "--corpus", corpus.Root.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("only one may send at a time");

        using var reopened = corpus.Open();
        reopened.ProcessingJobs.Count(job => job.MeetingId == meeting && job.Kind == JobKind.Transcribe)
            .ShouldBe(0);
    }

    [Fact]
    public void A_meeting_stopped_on_a_person_is_not_transcribed_again()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);

        using (var context = corpus.Open())
        {
            var job = ProcessingJob.Queue(
                Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/2", When);
            job.Start(When);
            job.AwaitUser("stopped for this test");
            context.Add(job);
            context.SaveChanges();
        }

        var run = CommandLine.Of("transcribe-again", meeting.ToString(), "--corpus", corpus.Root.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("stopped waiting for a person");
    }

    /// <summary>Decides 14: said before the charge, and counted from the corpus after it.</summary>
    [Fact]
    public void A_name_a_voice_loses_is_said_before_and_counted_after()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = FirstTranscription(corpus);

        using (var context = corpus.Open())
        {
            var human = new HumanLayer(context, TimeProvider.System);
            var somebody = human.Add("Ada");

            // A label no real fixture reaches: what matters is that the second fixture's turns
            // do not carry it, and this is guaranteed of a synthetic label the way no real
            // speaker index this small a set of fixtures produces would be.
            human.Assign(meeting, "ch1:speaker_97", somebody);
        }

        using var output = new StringWriter();

        var code = DeepgramCommands.TranscribeAgain(
            Args(meeting, corpus), output, () => "60", FixtureBody(DeepgramFixtures.TwoChannelOneVoiceMe));

        code.ShouldBe(Cli.Ok);
        var report = output.ToString();
        report.ShouldContain("1 voice(s) carry a name somebody gave them");
        report.ShouldContain("0 of 1 name(s) are still on a voice");

        using var reopened = corpus.Open();
        reopened.SpeakerAssignments.Count(row => row.MeetingId == meeting).ShouldBe(0);
    }

    /// <summary>A meeting recorded and transcribed once, for 60 minutes, ready to be sent again.</summary>
    private static Guid FirstTranscription(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
        var meeting = RecordedMeetings.Recorded(context, SourceProfile.Multichannel, When);
        MeetingIntake.ReceiveInto(
            context, meeting, new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)), When);
        return meeting;
    }

    private static Arguments Args(Guid meeting, TemporaryCorpus corpus) =>
        Arguments.Parse([meeting.ToString(), "--corpus", corpus.Root.FullName]);

    private static SendingToTheProvider FixtureBody(string fixture) => async (_, _, response, stopping) =>
    {
        await using var body = File.OpenRead(DeepgramFixtures.PathOf(fixture));
        await body.CopyToAsync(response, stopping);
        return body.Length;
    };
}
