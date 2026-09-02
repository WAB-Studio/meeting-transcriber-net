using System.Text;

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

    /// <summary>
    /// The command that puts a corpus right, applied to the card. A meeting filed before this
    /// corpus wrote cards at all has none, and nothing else would ever give it one: intake only
    /// reaches the meeting whose response is being filed. So a rebuild writes every card, which is
    /// also what makes the promise in docs/corpus.md true of a folder rather than of a moment.
    /// </summary>
    [Fact]
    public void A_rebuild_leaves_every_meeting_with_the_card_that_names_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetings = DeepgramFixtures.All.Select(fixture => Recorded(context, corpus.Root, fixture)).ToArray();
        context.Artifacts.Where(artifact => artifact.Kind == ArtifactKind.Manifest).ShouldBeEmpty();

        CorpusRebuild.Run(context, When);

        foreach (var meeting in meetings)
        {
            var manifest = context.Artifacts.Single(
                artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Manifest);
            manifest.Origin.ShouldBe(ArtifactOrigin.Source);
            MeetingManifest.Read(CorpusFiles.Locate(corpus.Root, manifest.RelativePath))
                .MeetingId.ShouldBe(meeting);
        }
    }

    /// <summary>
    /// The staleness a rebuild is the answer to today: the card is written where a meeting is
    /// filed, so a title somebody changed afterwards does not reach it until something writes it
    /// again. Running the rebuild is that something.
    /// </summary>
    [Fact]
    public void A_rebuild_brings_a_card_up_to_a_title_somebody_changed_since()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, When);

        context.Meetings.Single(row => row.Id == meeting).Title = "la que renombraron después";
        context.SaveChanges();

        CorpusRebuild.Run(context, When);

        var manifest = context.Artifacts.Single(
            artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Manifest);
        MeetingManifest.Read(CorpusFiles.Locate(corpus.Root, manifest.RelativePath))
            .Title.ShouldBe("la que renombraron después");
    }

    [Fact]
    public void Every_meeting_that_is_still_here_is_rebuilt()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meetings = DeepgramFixtures.All.Select(fixture => Recorded(context, corpus.Root, fixture)).ToArray();

        var report = CorpusRebuild.Run(context, When);

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

        CorpusRebuild.Run(context, When);
        var turns = Turns(context);
        var derived = Derived(context);

        CorpusRebuild.Run(context, When + Duration.FromMilliseconds(60_000));

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
        CorpusRebuild.Run(context, When);

        var cited = context.Utterances.OrderBy(turn => turn.Ordinal).Skip(3).First();
        var run = Extracted(context, meeting);
        Claim(context, meeting, run, cited);

        CorpusRebuild.Run(context, When);

        // Not one turn id in common, and the claim still lands on the same words.
        context.Utterances.ShouldAllBe(turn => turn.Id != cited.Id);
        var decision = context.Decisions.Single();
        context.Utterances
            .Single(turn => turn.MeetingId == decision.MeetingId
                && turn.Ordinal == decision.Evidence.UtteranceOrdinal)
            .Text.ShouldBe(cited.Text);
        var question = context.OpenQuestions.Single();
        context.Utterances
            .Single(turn => turn.MeetingId == question.MeetingId
                && turn.Ordinal == question.Evidence.UtteranceOrdinal)
            .Text.ShouldBe(cited.Text);
        context.ActionItemProgress.Single().State.ShouldBe(ActionItemState.Done);
    }

    /// <summary>
    /// Summaries, decisions, actions and open questions are left alone rather than reprojected.
    /// They come from the accepted extractions and nothing reads one back into rows yet, so
    /// deleting them would be losing what this cannot put back.
    /// </summary>
    [Fact]
    public void What_the_rebuild_cannot_produce_again_it_does_not_delete()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        CorpusRebuild.Run(context, When);
        var run = Extracted(context, meeting);
        Claim(context, meeting, run, context.Utterances.First());

        CorpusRebuild.Run(context, When);

        context.Decisions.Count().ShouldBe(1);
        context.ActionItems.Count().ShouldBe(1);
        context.OpenQuestions.Count().ShouldBe(1);
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
        CorpusRebuild.Run(context, When);

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

        CorpusRebuild.Run(context, When);

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

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(1);
        report.CouldNotRebuild.ShouldHaveSingleItem().ShouldContain(lost.ToString());
        context.Utterances.ShouldAllBe(turn => turn.MeetingId != lost);
    }

    /// <summary>
    /// A response that stops early, reached through the parser and outside anything a list of what
    /// a render throws had in it. It used to escape the loop, roll back every meeting rebuilt
    /// before it, and do the same on the next run and every run after it — so the corpus could
    /// never be rebuilt at all, and the command said the same thing each time.
    /// </summary>
    /// <remarks>
    /// One before and one after, because both halves are the point: the meetings already rebuilt
    /// have to survive the commit, and the meetings behind it have to be reached at all. Nothing
    /// files a response through the parser — the legacy importer copies a <c>deepgram.json</c> and
    /// hashes it — and imported meetings are the oldest in a corpus, so this sits where a rebuild
    /// meets it first.
    /// </remarks>
    [Fact]
    public void A_response_the_parser_cannot_read_costs_that_meeting_and_neither_side_of_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var older = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var unreadable = Filed(context, SourceProfile.Multichannel, Later(1), Truncated());
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(2));

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(2);
        Files(context, older).ShouldBe(Rebuilt, ignoreOrder: true);
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);

        // The line has to be the parser saying it could not read the response, or this would still
        // pass if the refusal arrived as something an allowlist would have carried anyway.
        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(unreadable.ToString());
        line.ShouldContain("stops early");
    }

    /// <summary>
    /// The other one the response itself says: a single track filed against a meeting recorded on
    /// two channels. The audio contract refuses it, thrown by the domain and reached through the
    /// parser, and it costs its own meeting and nobody else's.
    /// </summary>
    [Fact]
    public void A_response_that_disagrees_with_its_profile_costs_only_its_own_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var mismatched = Filed(
            context, SourceProfile.Multichannel, When, Copied(DeepgramFixtures.SingleTrackDiarized));
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, Later(1));

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(1);
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);

        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(mismatched.ToString());
        line.ShouldContain("needs 2 channel(s), got 1");
    }

    /// <summary>
    /// The rule itself, as against the two above, which only pin the two refusals the old catch
    /// happened to miss.
    /// </summary>
    /// <remarks>
    /// A speaker numbered below zero: the parser range checks the channel of an utterance and not
    /// its speaker, so this walks straight through to <c>SpeakerLabels.For</c> and comes back as
    /// the domain's speaker contract refusing — an <see cref="ArgumentOutOfRangeException"/>, which
    /// reads like somebody's bug rather than a refusal and which no list of what a render may throw
    /// would ever have carried. Lengthen the allowlist to the parser and the audio contract and the
    /// two probes above stay green while this one goes red, which is the whole reason it is here.
    /// </remarks>
    [Fact]
    public void A_refusal_no_list_would_have_carried_costs_only_its_own_meeting()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var impossible = Filed(context, SourceProfile.Multichannel, When, WithSpeakerBelowZero());
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, Later(1));

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(1);
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);

        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(impossible.ToString());
        line.ShouldContain("A provider numbers speakers from zero");
    }

    /// <summary>
    /// A meeting refused partway leaves its rows and its files saying the same thing, and takes
    /// nothing of the corpus with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one refusal that lands after the turns have been written rather than before them, and
    /// the reason a rebuild absorbs rather than rolls back. A folder half synced from elsewhere, or
    /// a path another program is holding, is what a directory standing where <c>transcript.md</c>
    /// goes comes to; the render reaches it having already projected the turns.
    /// </para>
    /// <para>
    /// So this meeting keeps turns from a render that did not finish, and that is the deliberate
    /// choice rather than the accident. They are the same turns the same response projected before
    /// — a rebuild is repeatable — and the alternative was undoing the artifact rows of a meeting
    /// that already had them, under files <c>DurableArtifact</c> moves into place before it records
    /// anything. <see cref="CorpusIntegrity.Check"/> is what says which of the two the corpus can
    /// live with: it reads every row against the file it names.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meeting_refused_after_its_turns_were_written_leaves_the_corpus_saying_one_thing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var blocked = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(1));
        Directory.CreateDirectory(
            CorpusFiles.Locate(corpus.Root, CorpusFiles.PathFor(blocked, "transcript.md")).FullName);

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(1);
        report.CouldNotRebuild.ShouldHaveSingleItem().ShouldContain(blocked.ToString());
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);

        // Neither derived file, the card it can still be recognised by, and every row it does have
        // agreeing with the file on disk that row names.
        Files(context, blocked).ShouldBe([ArtifactKind.Manifest]);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting refused earlier in the run leaves the deferral standing for the meetings behind
    /// it, whose turns are cited by claims and cannot be replaced without it.
    /// </summary>
    /// <remarks>
    /// The one thing the savepoint could quietly have cost. <c>PRAGMA defer_foreign_keys</c> is set
    /// once, inside the transaction, and SQLite turns it off again at the end of one — so whether a
    /// rollback to a savepoint counts as that end is the question, and getting it wrong would turn
    /// every meeting after the first refusal into a refusal of its own, but only in a corpus that
    /// had claims in it. Which is every corpus anybody has actually used.
    /// </remarks>
    [Fact]
    public void A_meeting_refused_first_leaves_the_claims_of_the_meetings_behind_it_alone()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var cited = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, Later(1));
        CorpusRebuild.Run(context, When);
        var turn = context.Utterances.OrderBy(row => row.Ordinal).Skip(3).First();
        Claim(context, cited, Extracted(context, cited), turn);

        // Filed after the claim so it sorts first and is refused before the cited meeting is
        // reached, which is the whole shape of the question.
        Filed(context, SourceProfile.Multichannel, When, Truncated());

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(1);
        report.CouldNotRebuild.ShouldHaveSingleItem().ShouldContain("stops early");
        context.Utterances.ShouldAllBe(row => row.Id != turn.Id);
        var decision = context.Decisions.Single();
        context.Utterances
            .Single(row => row.MeetingId == decision.MeetingId
                && row.Ordinal == decision.Evidence.UtteranceOrdinal)
            .Text.ShouldBe(turn.Text);
    }

    /// <summary>
    /// What the meeting that could not be rebuilt is left holding: the card that names it, and
    /// neither of the two derived files.
    /// </summary>
    /// <remarks>
    /// The card is the half worth pinning. It is written inside the same guard as the render and
    /// survives one that fails, because a refused meeting is absorbed and never rolled back — a
    /// meeting whose response cannot be read is exactly the one worth being able to recognise in a
    /// folder, and it is the only thing this rebuild can still give it.
    /// </remarks>
    [Fact]
    public void A_meeting_the_rebuild_could_not_render_keeps_its_card_and_neither_derived_file()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var unreadable = Filed(context, SourceProfile.Multichannel, When, Truncated());

        CorpusRebuild.Run(context, When).CouldNotRebuild.ShouldHaveSingleItem();

        Files(context, unreadable).ShouldBe([ArtifactKind.Manifest]);
        MeetingManifest.Read(CorpusFiles.Locate(
                corpus.Root,
                context.Artifacts.Single(artifact => artifact.MeetingId == unreadable
                    && artifact.Kind == ArtifactKind.Manifest).RelativePath))
            .MeetingId.ShouldBe(unreadable);
        context.Utterances.ShouldAllBe(turn => turn.MeetingId != unreadable);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
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

        var report = CorpusRebuild.Run(context, When);

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
        CorpusRebuild.Run(context, When);

        // A bare word: the query is FTS5's own syntax, so a trailing full stop is a syntax error
        // rather than punctuation to ignore.
        var word = new string([.. context.Utterances.First().Text.Split(' ')[0].Where(char.IsLetterOrDigit)]);
        var before = CorpusSearch.Find(context, word, limit: 100).Count;
        before.ShouldBeGreaterThan(0);

        CorpusRebuild.Run(context, When);

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

        var report = CorpusRebuild.Run(context, When);

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

    /// <summary>Everything a rebuilt meeting is left holding, other than the response itself.</summary>
    private static readonly ArtifactKind[] Rebuilt =
        [ArtifactKind.Manifest, ArtifactKind.Transcript, ArtifactKind.Utterances];

    /// <summary>An hour later, so meetings can be put in the order a rebuild will reach them in.</summary>
    private static UtcTimestamp Later(int hours) => When + Duration.FromMilliseconds(hours * 3_600_000L);

    /// <summary>What a meeting has been given, other than the response it was given it from.</summary>
    private static ArtifactKind[] Files(CorpusDbContext context, Guid meeting) => context.Artifacts
        .Where(artifact => artifact.MeetingId == meeting && artifact.Kind != ArtifactKind.DeepgramResponse)
        .Select(artifact => artifact.Kind)
        .ToArray();

    /// <summary>A meeting with its paid response in the corpus.</summary>
    private static Guid Recorded(
        CorpusDbContext context, DirectoryInfo root, string fixture, UtcTimestamp? startedAt = null) =>
        Filed(context, DeepgramFixtures.ProfileOf(fixture), startedAt ?? When, Copied(fixture));

    /// <summary>
    /// A meeting with a response filed against it — whatever bytes the caller writes, under
    /// whatever profile the meeting was recorded on.
    /// </summary>
    /// <remarks>
    /// The two are separate arguments and not one fixture on purpose. Nothing checks that a filed
    /// response can be read, or that it agrees with the meeting it is filed against, so a real
    /// corpus holds pairs that do not: <c>tools/MeetingTranscriber.CorpusImport</c> copies a
    /// <c>deepgram.json</c> and hashes it, and reads only its metadata on the way past. A
    /// fixture-only helper could not put the corpus into the state a rebuild actually meets.
    /// </remarks>
    private static Guid Filed(
        CorpusDbContext context,
        SourceProfile profile,
        UtcTimestamp startedAt,
        Action<Stream> response)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAt,
            SourceProfile = profile,
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        };
        context.Meetings.Add(meeting);
        context.SaveChanges();

        DurableArtifact.Write(
            context,
            meeting.Id,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting.Id, "deepgram.json"),
            When,
            response);

        return meeting.Id;
    }

    /// <summary>A fixture, as it was sent.</summary>
    private static Action<Stream> Copied(string fixture) => stream =>
    {
        using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
        response.CopyTo(stream);
    };

    /// <summary>A fixture cut off partway, which is what a response that stopped early looks like.</summary>
    private static Action<Stream> Truncated(string fixture = DeepgramFixtures.TwoChannelShort) => stream =>
    {
        using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
        var head = new byte[4096];
        response.ReadExactly(head);
        stream.Write(head);
    };

    /// <summary>
    /// A fixture whose speakers are numbered below zero. Every number, timing and channel it was
    /// sent with is otherwise left alone, and the one edit is the one the parser does not check:
    /// it range checks the channel of an utterance and not its speaker.
    /// </summary>
    private static Action<Stream> WithSpeakerBelowZero(string fixture = DeepgramFixtures.TwoChannelShort) =>
        stream =>
        {
            var response = File.ReadAllText(DeepgramFixtures.PathOf(fixture));
            response.ShouldContain(@"""speaker"":0");
            stream.Write(Encoding.UTF8.GetBytes(response.Replace(@"""speaker"":0", @"""speaker"":-1", StringComparison.Ordinal)));
        };

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

    /// <summary>
    /// A decision, an action and an open question citing one turn, with somebody's state on the
    /// action. Every one of them is named by the run and its position, which is what a rebuild has
    /// to leave where it is.
    /// </summary>
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
            Ordinal = 0,
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
        context.OpenQuestions.Add(new OpenQuestion
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run,
            Ordinal = 0,
            Question = "lo que quedó abierto",
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
