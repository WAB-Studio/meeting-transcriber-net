using System.Text;
using System.Text.RegularExpressions;

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
    /// anything. Which of the two the corpus can live with is a judgement nothing here measures:
    /// <see cref="CorpusIntegrity.Check"/> runs <c>PRAGMA integrity_check</c>,
    /// <c>PRAGMA foreign_key_check</c> and an integrity-check per search index, and never opens an
    /// artifact file — so it would read clean either way, and the assertion below is a floor.
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

        // Neither derived file, and the card it can still be recognised by.
        Files(context, blocked).ShouldBe([ArtifactKind.Manifest]);
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting refused by the corpus as its own turns are saved costs that meeting and leaves the
    /// meetings behind it a change tracker with nothing of it in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other side of absorbing, and the one the shared context makes possible. Every other
    /// refusal in this file is thrown before the turns are saved — by the parser, by the domain, or
    /// by the filesystem on the write that comes after — so the tracker the next meeting inherits
    /// is clean without anything having kept it that way. This one is refused by SQLite from inside
    /// <c>MeetingRenderer.Project</c>'s own <c>SaveChanges</c>, with the whole meeting's turns
    /// already added, and <c>Project</c> only ever detaches turns of the meeting it is rendering.
    /// So without a reset those rows are still pending when the next meeting saves anything at all,
    /// and the corpus refuses them again: one meeting costing every meeting behind it, which is
    /// what absorbing exists to stop.
    /// </para>
    /// <para>
    /// One good meeting on each side, because both halves are the point: the meeting already
    /// rebuilt has to come through the reset whole, and the meeting behind the refusal has to come
    /// back rebuilt exactly as it would have without one.
    /// </para>
    /// <para>
    /// What the refused meeting itself is left holding is not asserted here, and deliberately.
    /// This one has never been rendered, so it has nothing to lose and would say nothing about a
    /// meeting that has —
    /// <see cref="A_meeting_the_rebuild_could_not_render_keeps_its_card_and_neither_derived_file"/>
    /// covers the half that is settled, and the half that is not is the renderer dropping a
    /// meeting's turns before it knows the new ones will save.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meeting_the_corpus_refuses_as_it_saves_leaves_the_next_one_a_clean_change_tracker()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var older = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var refused = Filed(context, SourceProfile.Multichannel, Later(1), WithConfidenceOffTheScale());
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(2));

        var report = CorpusRebuild.Run(context, When);

        report.Meetings.ShouldBe(2);
        Files(context, older).ShouldBe(Rebuilt, ignoreOrder: true);
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);

        // The constraint by name, or this would pass on a refusal that never reached SQLite at all
        // — which is every other refusal in this file, and precisely what makes them the wrong
        // probe for this.
        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(refused.ToString());
        line.ShouldContain("ck_utterances_confidence");

        // The corpus still sound as SQLite counts soundness: the file readable, every reference
        // pointing at something, and both indexes agreeing with the tables they index. It opens no
        // artifact file and asks no meeting whether it has turns, so it is the floor and not the
        // proof — what the reset threw away is said by the two meetings above being whole.
        CorpusIntegrity.Check(context).ShouldBeEmpty();
    }

    /// <summary>
    /// A meeting the corpus refuses as its turns are saved keeps the turns it already had.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The corpus rebuilt before is the whole setup, and it is the ordinary case rather than an
    /// exotic one: a rebuild is a command somebody runs more than once. The first run gives this
    /// meeting its turns; the second finds a response the corpus will not store and refuses it
    /// partway. Between the two, the meeting is left holding a transcript and a jsonl that describe
    /// turns — so a meeting with none is a meeting whose files name what its rows no longer have,
    /// and <c>utterances</c> is what a citation anchors on.
    /// </para>
    /// <para>
    /// It went to zero before the projection was made undoable: <c>ExecuteDelete</c> is a statement
    /// that has run rather than a change waiting to be sent, so nothing the change tracker did on
    /// the way out could put those rows back, and the rebuild that would have is the one refusing.
    /// Nothing reported it either — no foreign key is broken by a meeting having no turns.
    /// </para>
    /// <para>
    /// The rows are compared one by one and not counted. The same number of different turns is a
    /// meeting whose citations have quietly moved, which is the failure this is watching for, and
    /// counting would not see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meeting_refused_as_it_saves_keeps_the_turns_it_already_had()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var losing = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var newer = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(1));
        CorpusRebuild.Run(context, When);

        var had = Turns(context, losing);
        had.ShouldNotBeEmpty();
        Refiled(context, corpus.Root, losing, WithConfidenceOffTheScale());

        var report = CorpusRebuild.Run(context, Later(2));

        report.Meetings.ShouldBe(1);
        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(losing.ToString());
        line.ShouldContain("ck_utterances_confidence");

        Turns(context, losing).ShouldBe(had);

        // And the two files still describing them, which is what makes keeping the turns the whole
        // answer rather than half of one: the meeting comes out of the refusal saying what it said
        // going in, and the meeting behind it comes out rebuilt.
        Files(context, losing).ShouldBe(Rebuilt, ignoreOrder: true);
        Files(context, newer).ShouldBe(Rebuilt, ignoreOrder: true);
    }

    /// <summary>
    /// A meeting refused as it saves, whose turns claims cite, costs that meeting and not the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The load-bearing case, and the only one where the renderer's savepoint does something EF's
    /// own cannot. EF takes a savepoint around every <c>SaveChanges</c> that runs inside a caller's
    /// transaction, so the rows a refused save was sending are undone with or without this change.
    /// The delete is not: it goes through <c>ExecuteDelete</c>, outside that unit of work entirely.
    /// So this is the shape that separates the two — a meeting whose turns are already there, and
    /// gone by the time the save is refused.
    /// </para>
    /// <para>
    /// Claims citing those turns are what make it take the whole run rather than the meeting. The
    /// citation foreign keys are <c>(meeting_id, utterance_ordinal)</c> with no cascade, so deleting
    /// a cited turn raises the deferred count once per claim and only re-inserting the same ordinal
    /// brings it back down. A meeting refused between the two used to leave it raised, and the
    /// corpus-wide commit is where that lands: the run ends on a foreign key failure outside every
    /// guard, taking every meeting it rebuilt and the report naming the one it could not.
    /// </para>
    /// <para>
    /// A cited meeting behind it as well, and that half is about the pragma rather than the count:
    /// its turns can be replaced at all only while foreign keys are still deferred, which is the
    /// question of whether SQLite treats a rollback to a savepoint as the end of a transaction. So
    /// one run answers both — the refused meeting keeps what it had, and the meeting behind it is
    /// rebuilt as if nothing had been refused.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_meeting_refused_with_cited_turns_costs_that_meeting_and_not_the_run()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var losing = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var behind = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(1));
        CorpusRebuild.Run(context, When);

        var quoted = Claimed(context, losing);
        var quotedBehind = Claimed(context, behind);
        var had = Turns(context, losing);
        Refiled(context, corpus.Root, losing, WithConfidenceOffTheScale());

        var report = CorpusRebuild.Run(context, Later(2));

        report.Meetings.ShouldBe(1);
        report.CouldNotRebuild.ShouldHaveSingleItem().ShouldContain(losing.ToString());
        Turns(context, losing).ShouldBe(had);

        // Both claims still landing on the words they were made from: the one over the meeting that
        // was refused, whose turn was never replaced, and the one over the meeting behind it, whose
        // turn was deleted and put back under a deferral that had already survived a rollback.
        Resolved(context, losing).ShouldBe(quoted);
        Resolved(context, behind).ShouldBe(quotedBehind);
    }

    /// <summary>
    /// A meeting whose response no longer reaches the position its claim cites costs that meeting
    /// and not the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refusal that used to arrive at the corpus-wide commit, which is the one place a rebuild
    /// has no guard around. The deferral is what lets every meeting's turns be deleted and put back
    /// at all, and the price of deferring is that a citation left pointing at nothing is not
    /// discovered until the commit — outside the loop, after every meeting has been rebuilt and the
    /// report written. So one meeting whose response had changed underneath it took all of them: the
    /// run threw, nothing was committed, and the line naming the meeting to look at went with it.
    /// </para>
    /// <para>
    /// The meeting is given a response that is real and readable and simply not the one its claims
    /// were made from — a shorter one, whose projection stops before the position the claim
    /// cites. That is the folder half restored from somewhere else, or a re-transcription, and it is
    /// the state <c>MeetingRenderer</c> now refuses before it deletes anything rather than after it
    /// has saved everything.
    /// </para>
    /// <para>
    /// A cited meeting behind it too, because the promise is about the run and not only about the
    /// refused meeting: it is rebuilt, its claim lands back on the words it came from, and the
    /// commit that used to be where all of this went wrong is the one that carries it.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_response_that_no_longer_reaches_a_cited_turn_costs_that_meeting_and_not_the_run()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var losing = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort, When);
        var behind = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelOneVoiceMe, Later(1));
        CorpusRebuild.Run(context, When);

        var quoted = ClaimedOnItsLastTurn(context, losing);
        var quotedBehind = Claimed(context, behind);
        var had = Turns(context, losing);
        Refiled(context, corpus.Root, losing, Copied(DeepgramFixtures.TwoChannelOneVoiceMe));

        // What the swap is worth saying out loud: the meeting behind it was rebuilt from that same
        // response, so its turn count is what this one is about to come to — and the position its
        // claim cites is the last of the turns it has now, which is past the end of them.
        had.Count.ShouldBeGreaterThan(Turns(context, behind).Count);

        var report = CorpusRebuild.Run(context, Later(2));

        report.Meetings.ShouldBe(1);
        var line = report.CouldNotRebuild.ShouldHaveSingleItem();
        line.ShouldContain(losing.ToString());
        line.ShouldContain("citing nothing");

        Turns(context, losing).ShouldBe(had);
        Files(context, losing).ShouldBe(Rebuilt, ignoreOrder: true);
        Resolved(context, losing).ShouldBe(quoted);
        Resolved(context, behind).ShouldBe(quotedBehind);
    }

    /// <summary>
    /// A meeting refused earlier in the run leaves the deferral standing for the meetings behind
    /// it, whose turns are cited by claims and cannot be replaced without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PRAGMA defer_foreign_keys</c> is set once, inside the transaction, and SQLite turns it off
    /// again at the end of one — so anything in a run that might count as that end would turn every
    /// meeting after it into a refusal of its own, but only in a corpus that had claims in it. Which
    /// is every corpus anybody has actually used.
    /// </para>
    /// <para>
    /// This is the refusal absorbed before the renderer takes a savepoint at all: the parser
    /// stopping, with a manifest already written and committed into the transaction ahead of it.
    /// <see cref="A_meeting_refused_with_cited_turns_costs_that_meeting_and_not_the_run"/> is the
    /// same question asked of a refusal that does take one, and the two are not interchangeable —
    /// each covers a point in the sequence the other never reaches.
    /// </para>
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

    /// <summary>
    /// Every turn of the corpus as text, or of one meeting, for comparing a rebuild against the one
    /// before it.
    /// </summary>
    private static List<string> Turns(CorpusDbContext context, Guid? meeting = null) =>
    [
        .. context.Utterances
            .Where(turn => meeting == null || turn.MeetingId == meeting)
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

    /// <summary>
    /// Different bytes where a meeting's response already is, leaving the row that names it alone.
    /// </summary>
    /// <remarks>
    /// What a corpus rebuilt once and refused the next time looks like from here, and the only way
    /// to reach it: a meeting has turns because a readable response was rendered, and is refused
    /// because the response is no longer that. Nothing on the render path checks a response against
    /// the size and the hash its row carries — the reconciler is what does that, on its own
    /// command — so the swap is invisible to the rebuild, which is exactly the folder half restored
    /// from somewhere else that this is standing in for.
    /// </remarks>
    private static void Refiled(
        CorpusDbContext context, DirectoryInfo root, Guid meeting, Action<Stream> response)
    {
        var filed = context.Artifacts.Single(artifact =>
            artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.DeepgramResponse);

        using var bytes = CorpusFiles.Locate(root, filed.RelativePath).Create();
        response(bytes);
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

    /// <summary>A fixture whose confidences the corpus will not store.</summary>
    private static Action<Stream> WithConfidenceOffTheScale(
        string fixture = DeepgramFixtures.TwoChannelShort) =>
        stream => stream.Write(Encoding.UTF8.GetBytes(CorruptedResponses.WithConfidenceOffTheScale(
            File.ReadAllText(DeepgramFixtures.PathOf(fixture)))));

    /// <summary>
    /// A claim over one of this meeting's turns, and the words it was made from.
    /// </summary>
    /// <remarks>
    /// The fourth turn rather than the first, so a rebuild that shifted every ordinal by one would
    /// still be caught by <see cref="Resolved"/> reading different words back.
    /// </remarks>
    private static string Claimed(CorpusDbContext context, Guid meeting) => Claiming(
        context,
        context.Utterances
            .Where(row => row.MeetingId == meeting)
            .OrderBy(row => row.Ordinal)
            .Skip(3)
            .First());

    /// <summary>
    /// A claim over the last turn this meeting has, and the words it was made from.
    /// </summary>
    /// <remarks>
    /// The one position a shorter response cannot produce again, which is what a meeting whose
    /// response is no longer the one its claims came from looks like from the outside.
    /// </remarks>
    private static string ClaimedOnItsLastTurn(CorpusDbContext context, Guid meeting) => Claiming(
        context,
        context.Utterances
            .Where(row => row.MeetingId == meeting)
            .OrderByDescending(row => row.Ordinal)
            .First());

    /// <summary>An accepted extraction with a claim over that turn, and the words the claim cites.</summary>
    private static string Claiming(CorpusDbContext context, Utterance cited)
    {
        Claim(context, cited.MeetingId, Extracted(context, cited.MeetingId), cited);
        return cited.Text;
    }

    /// <summary>The words the turn a meeting's claim cites is holding now.</summary>
    private static string Resolved(CorpusDbContext context, Guid meeting)
    {
        var decision = context.Decisions.Single(row => row.MeetingId == meeting);

        return context.Utterances
            .Single(row => row.MeetingId == meeting && row.Ordinal == decision.Evidence.UtteranceOrdinal)
            .Text;
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
