using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Processing.Tests.Deepgram;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// Throwing away every projection in the corpus and producing it again from the sources. This is
/// the operation the whole "sources versus derivatives" policy is written for: if it is not safe to
/// run, the policy is a claim nobody can act on.
/// </summary>
public class CorpusRebuildTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    public void Every_meeting_that_is_still_here_is_rebuilt()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetings = DeepgramFixtures.All.Select(fixture => Recorded(context, corpus.Root, fixture)).ToArray();

        var report = CorpusRebuild.Run(context, corpus.Root, When);

        report.Meetings.ShouldBe(meetings.Length);
        report.CouldNotRebuild.ShouldBeEmpty();
        report.Turns.ShouldBe(context.Utterances.Count());
        report.Turns.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// The done condition: delete every projection, produce it again, and get the same corpus back.
    /// Compared row by row rather than by counting, because the same number of different turns is
    /// exactly the failure this is watching for.
    /// </summary>
    [Fact]
    public void Rebuilding_produces_the_same_projections_and_the_same_files()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        foreach (var fixture in DeepgramFixtures.All)
        {
            Recorded(context, corpus.Root, fixture);
        }

        CorpusRebuild.Run(context, corpus.Root, When);
        var turns = Turns(context);
        var derived = Derived(context);

        CorpusRebuild.Run(context, corpus.Root, When + Duration.FromMilliseconds(60_000));

        Turns(context).ShouldBe(turns);
        Derived(context).ShouldBe(derived);
    }

    /// <summary>
    /// The one failure a rebuild could have that nothing else would notice: a turn moving under the
    /// claims that cite it. The claims are not deleted and not touched — they anchor on the
    /// meeting and the position — and foreign keys are deferred to the commit, so a rebuild that
    /// renumbered anything fails there instead of quietly rewriting what a claim points at.
    /// </summary>
    [Fact]
    public void A_claim_still_points_at_the_turn_it_came_from()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, corpus.Root, When);

        var cited = context.Utterances.OrderBy(turn => turn.Ordinal).Skip(3).First();
        var run = Extracted(context, meeting);
        Claim(context, meeting, run, cited);

        CorpusRebuild.Run(context, corpus.Root, When);

        // Not one turn id in common, and the claim still lands on the same words.
        context.Utterances.ShouldAllBe(turn => turn.Id != cited.Id);
        var decision = context.Decisions.Single();
        context.Utterances
            .Single(turn => turn.MeetingId == decision.MeetingId
                && turn.Ordinal == decision.Evidence.UtteranceOrdinal)
            .Text.ShouldBe(cited.Text);
        context.ActionItemProgress.Single().State.ShouldBe(ActionItemState.Done);
    }

    /// <summary>
    /// Summaries, decisions and actions are left alone rather than reprojected. They come from the
    /// accepted extractions and nothing reads one back into rows yet, so deleting them would be
    /// losing what this cannot put back.
    /// </summary>
    [Fact]
    public void What_the_rebuild_cannot_produce_again_it_does_not_delete()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, corpus.Root, When);
        var run = Extracted(context, meeting);
        Claim(context, meeting, run, context.Utterances.First());

        CorpusRebuild.Run(context, corpus.Root, When);

        context.Decisions.Count().ShouldBe(1);
        context.ActionItems.Count().ShouldBe(1);
    }

    /// <summary>
    /// Nothing a person put in is a projection, and a rebuild is the operation most able to lose
    /// it by accident.
    /// </summary>
    [Fact]
    public void The_human_layer_comes_through_untouched()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, corpus.Root, When);

        var human = new HumanLayer(context, TimeProvider.System);
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var somebody = human.Add("Renata");
        human.ThisIsMe(somebody);
        human.Join(somebody, techsed);
        human.Link(meeting, techsed, MeetingNodeRole.WorkOf);
        human.Name(meeting, somebody, MeetingPersonRole.Attended);
        human.Assign(meeting, context.Utterances.First().SpeakerLabel, somebody);
        human.Correct("quati", "Coati");

        var before = HumanLayer(context);

        CorpusRebuild.Run(context, corpus.Root, When);

        HumanLayer(context).ShouldBe(before);
    }

    /// <summary>
    /// A meeting whose response is gone is a line in the report, not a rebuild that quietly does
    /// less. The other meetings still come through, which is what makes the report worth reading.
    /// </summary>
    [Fact]
    public void A_meeting_whose_response_is_gone_is_named_and_the_rest_still_rebuild()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        var lost = Recorded(context, corpus.Root, DeepgramFixtures.SingleTrackDiarized);
        CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(lost, "deepgram.json")).Delete();

        var report = CorpusRebuild.Run(context, corpus.Root, When);

        report.Meetings.ShouldBe(1);
        report.CouldNotRebuild.ShouldHaveSingleItem().ShouldContain(lost.ToString());
        context.Utterances.ShouldAllBe(turn => turn.MeetingId != lost);
    }

    /// <summary>
    /// A meeting on its way out is not rebuilt. Producing derivatives for something being deleted
    /// is work whose only result is files the deletion has to find again.
    /// </summary>
    [Fact]
    public void A_meeting_being_deleted_is_not_rebuilt()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        var leaving = $"UPDATE meetings SET lifecycle_state = 'deleting', deleted_at = '{When}';";
        context.Database.ExecuteSqlRaw(leaving);

        var report = CorpusRebuild.Run(context, corpus.Root, When);

        report.Meetings.ShouldBe(0);
        context.Utterances.ShouldBeEmpty();
        _ = meeting;
    }

    /// <summary>
    /// The indexes are rebuilt with everything else, so search answers over the turns that are
    /// there now rather than over the ones that were.
    /// </summary>
    [Fact]
    public void Search_answers_over_the_rebuilt_turns()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, corpus.Root, When);

        // A bare word: the query is FTS5's own syntax, so a trailing full stop is a syntax error
        // rather than punctuation to ignore.
        var word = new string([.. context.Utterances.First().Text.Split(' ')[0].Where(char.IsLetterOrDigit)]);
        var before = CorpusSearch.Find(context, word, limit: 100).Count;
        before.ShouldBeGreaterThan(0);

        CorpusRebuild.Run(context, corpus.Root, When);

        CorpusSearch.Find(context, word, limit: 100).Count.ShouldBe(before);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// The reference measurement. It asserts almost nothing on purpose — a wall clock assertion is
    /// a test that fails on somebody else's laptop — and exists so the number is produced by
    /// something anybody can run rather than quoted from a session nobody can repeat.
    /// </summary>
    /// <remarks>
    /// Every committed fixture, several times over, which is the largest corpus that exists without
    /// a person's own. It is what says whether EF Core tracking is still the right write path here:
    /// the moment this is slow, dropping to SQL over the same connection is a measured decision
    /// instead of a worry.
    /// <para>
    /// Where it stood when it was written, on a development machine and in Debug: 30 meetings and
    /// 6018 turns in 2.4 seconds, so about 80 ms a meeting and 0.4 ms a turn. That is one order of
    /// magnitude of headroom over any corpus a person records by hand, and it is why the write path
    /// is still ordinary tracked EF.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_corpus_of_every_fixture_several_times_over_rebuilds_in_one_pass()
    {
        const int Rounds = 6;

        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        for (var round = 0; round < Rounds; round++)
        {
            foreach (var fixture in DeepgramFixtures.All)
            {
                Recorded(context, corpus.Root, fixture);
            }
        }

        var report = CorpusRebuild.Run(context, corpus.Root, When);

        report.Meetings.ShouldBe(Rounds * DeepgramFixtures.All.Count());
        report.CouldNotRebuild.ShouldBeEmpty();
        report.Turns.ShouldBe(context.Utterances.Count());
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
    }

    /// <summary>Every turn of the corpus as text, for comparing a rebuild against the one before it.</summary>
    private static List<string> Turns(CorpusDbContext context) =>
    [
        .. context.Utterances
            .OrderBy(turn => turn.MeetingId)
            .ThenBy(turn => turn.Ordinal)
            .AsEnumerable()
            .Select(turn =>
                $"{turn.MeetingId}|{turn.Ordinal}|{turn.Start.Milliseconds}|{turn.End.Milliseconds}"
                + $"|{turn.Channel}|{turn.SpeakerLabel}|{turn.Confidence}|{turn.Text}"),
    ];

    /// <summary>The derived files, by what is in them rather than by when they were written.</summary>
    private static List<string> Derived(CorpusDbContext context) =>
    [
        .. context.Artifacts
            .Where(artifact => artifact.Origin == ArtifactOrigin.Derived)
            .OrderBy(artifact => artifact.RelativePath)
            .AsEnumerable()
            .Select(artifact => $"{artifact.RelativePath}|{artifact.ByteSize}|{artifact.Sha256}"),
    ];

    private static List<string> HumanLayer(CorpusDbContext context) =>
    [
        .. context.Nodes.AsEnumerable().Select(node => $"node|{node.Id}|{node.Name}|{node.Kind}"),
        .. context.People.AsEnumerable().Select(person => $"person|{person.Id}|{person.DisplayName}|{person.IsMe}"),
        .. context.Affiliations.AsEnumerable().Select(spell => $"at|{spell.PersonId}|{spell.OrganizationId}"),
        .. context.MeetingNodes.AsEnumerable().Select(link => $"link|{link.MeetingId}|{link.NodeId}|{link.Role}"),
        .. context.MeetingPeople.AsEnumerable().Select(named => $"named|{named.MeetingId}|{named.PersonId}|{named.Role}"),
        .. context.SpeakerAssignments.AsEnumerable()
            .Select(assignment => $"speaker|{assignment.MeetingId}|{assignment.SpeakerLabel}|{assignment.PersonId}"),
        .. context.TerminologyCorrections.AsEnumerable()
            .Select(correction => $"term|{correction.WrongText}|{correction.CorrectText}"),
    ];

    /// <summary>A meeting with its paid response in the corpus.</summary>
    private static Guid Recorded(CorpusDbContext context, DirectoryInfo root, string fixture)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = When,
            SourceProfile = DeepgramFixtures.ProfileOf(fixture),
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        };
        context.Meetings.Add(meeting);
        context.SaveChanges();

        DurableArtifact.Write(
            context,
            root,
            meeting.Id,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting.Id, "deepgram.json"),
            When,
            stream =>
            {
                using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
                response.CopyTo(stream);
            });

        return meeting.Id;
    }

    /// <summary>An accepted extraction to hang a claim off. Written as SQL: it is not under test.</summary>
    private static Guid Extracted(CorpusDbContext context, Guid meeting)
    {
        var job = Guid.NewGuid().ToString().ToUpperInvariant();
        var run = Guid.NewGuid();
        var owner = meeting.ToString().ToUpperInvariant();

        var accepted = $"""
            INSERT INTO processing_jobs (id, meeting_id, kind, state, idempotency_key, created_at, attempt)
            VALUES ('{job}', '{owner}', 'extract', 'succeeded', 'extract/{owner}', '{When}', 1);
            INSERT INTO extraction_runs (
                id, meeting_id, job_id, provider, prompt_version, schema_version, input_hash, accepted_at, created_at)
            VALUES ('{run.ToString().ToUpperInvariant()}', '{owner}', '{job}', 'claude_code', '1', '1',
                    '{Sha256}', '{When}', '{When}');
            """;
        context.Database.ExecuteSqlRaw(accepted);

        return run;
    }

    /// <summary>A decision and an action citing one turn, with somebody's state on the action.</summary>
    private static void Claim(CorpusDbContext context, Guid meeting, Guid run, Utterance cited)
    {
        var evidence = new Citation
        {
            MeetingId = meeting,
            UtteranceOrdinal = cited.Ordinal,
            Start = cited.Start,
            End = cited.End,
            SpeakerLabel = cited.SpeakerLabel,
            QuotedText = cited.Text,
            SourceArtifactSha256 = Sha256,
        };

        context.Decisions.Add(new Decision
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run,
            Statement = "lo decidido",
            Evidence = evidence,
            CreatedAt = When,
        });
        context.ActionItems.Add(new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run,
            Ordinal = 0,
            Statement = "lo pendiente",
            Evidence = evidence,
            CreatedAt = When,
        });
        context.ActionItemProgress.Add(new ActionItemProgress
        {
            ExtractionRunId = run,
            Ordinal = 0,
            State = ActionItemState.Done,
            UpdatedAt = When,
        });
        context.SaveChanges();
    }
}
