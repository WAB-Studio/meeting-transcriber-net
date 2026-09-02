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
/// Everything derived from a meeting's paid response, end to end against a committed fixture: the
/// turns a citation anchors on, and the two files on disk.
/// </summary>
public class MeetingRendererTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_meeting_renders_its_turns_its_transcript_and_its_jsonl()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);

        var rendered = MeetingRenderer.Render(context, meeting, When);

        rendered.Turns.ShouldBeGreaterThan(0);
        context.Utterances.Count(turn => turn.MeetingId == meeting).ShouldBe(rendered.Turns);

        rendered.Transcript.Kind.ShouldBe(ArtifactKind.Transcript);
        rendered.Transcript.Origin.ShouldBe(ArtifactOrigin.Derived);
        rendered.Utterances.Kind.ShouldBe(ArtifactKind.Utterances);
        CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).Exists.ShouldBeTrue();
        CorpusFiles.Locate(corpus.Root, rendered.Utterances.RelativePath).Exists.ShouldBeTrue();

        // One line of jsonl per turn, and per stored row.
        File.ReadAllLines(CorpusFiles.Locate(corpus.Root, rendered.Utterances.RelativePath).FullName)
            .Length.ShouldBe(rendered.Turns);
    }

    /// <summary>
    /// The turns are numbered from zero with no gaps, because the number is what a citation points
    /// at rather than a label on a list.
    /// </summary>
    [Fact]
    public void The_stored_turns_are_the_positions_a_citation_anchors_on()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);

        MeetingRenderer.Render(context, meeting, When);

        var ordinals = context.Utterances
            .Where(turn => turn.MeetingId == meeting)
            .OrderBy(turn => turn.Ordinal)
            .Select(turn => turn.Ordinal)
            .ToArray();

        ordinals.ShouldBe([.. Enumerable.Range(0, ordinals.Length)]);
    }

    /// <summary>
    /// The whole done condition of the task: running it again touches no source and lands the same
    /// bytes. The response is checked by its hash rather than its timestamp — a source is never
    /// rewritten, and this is the test that would notice if it were.
    /// </summary>
    [Fact]
    public void Rendering_again_leaves_the_sources_alone_and_produces_the_same_files()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);

        var first = MeetingRenderer.Render(context, meeting, When);
        var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
        var responseSha = CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, response.RelativePath));

        var second = MeetingRenderer.Render(context, meeting, When + Duration.FromMilliseconds(60_000));

        second.Transcript.Sha256.ShouldBe(first.Transcript.Sha256);
        second.Utterances.Sha256.ShouldBe(first.Utterances.Sha256);
        second.Turns.ShouldBe(first.Turns);

        // A derivative is replaced and stays one row, and the source is exactly as it was.
        context.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.Transcript).ShouldBe(1);
        context.Artifacts.Count(artifact => artifact.Kind == ArtifactKind.Utterances).ShouldBe(1);
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, response.RelativePath)).ShouldBe(responseSha);
        context.Utterances.Count(turn => turn.MeetingId == meeting).ShouldBe(first.Turns);
    }

    /// <summary>
    /// The human layer reaches the rendered files and never the stored turns. A name resolved today
    /// changes today's transcript of a meeting recorded last year, and the row a claim is checked
    /// against still holds what the provider said.
    /// </summary>
    [Fact]
    public void A_name_and_a_correction_reach_the_transcript_and_not_the_stored_turns()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        MeetingRenderer.Render(context, meeting, When);

        var spoken = context.Utterances.First(turn => turn.MeetingId == meeting);
        var word = spoken.Text.Split(' ')[0];
        var human = new HumanLayer(context, TimeProvider.System);
        var somebody = human.Add("Renata");
        human.Assign(meeting, spoken.SpeakerLabel, somebody);
        human.Correct(word, "CORREGIDO");

        var rendered = MeetingRenderer.Render(context, meeting, When);
        var markdown = File.ReadAllText(
            CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName);

        markdown.ShouldContain("## Renata");
        markdown.ShouldContain("CORREGIDO");

        // The evidence is untouched: the row still says what the provider returned.
        context.Utterances.First(turn => turn.MeetingId == meeting).Text.ShouldBe(spoken.Text);
        context.Utterances.ShouldAllBe(turn => !turn.Text.Contains("CORREGIDO"));
    }

    /// <summary>
    /// A correction written against an organization reaches a meeting hanging off a project inside
    /// it. Upwards through the tree, which is the direction that needs saying out loud.
    /// </summary>
    [Fact]
    public void A_correction_on_an_organization_reaches_a_meeting_under_it()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        MeetingRenderer.Render(context, meeting, When);
        var word = context.Utterances.First(turn => turn.MeetingId == meeting).Text.Split(' ')[0];

        var human = new HumanLayer(context, TimeProvider.System);
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var coati = human.Under(techsed, NodeKind.Initiative, "Coati");
        human.Link(meeting, coati, MeetingNodeRole.WorkOf);
        human.Correct(word, "DESDE ARRIBA", under: techsed);

        var rendered = MeetingRenderer.Render(context, meeting, When);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldContain("DESDE ARRIBA");
    }

    /// <summary>
    /// A correction scoped to another meeting is another meeting's. Without the scope check every
    /// correction anybody ever wrote would apply to everything.
    /// </summary>
    [Fact]
    public void A_correction_scoped_to_another_meeting_does_not_reach_this_one()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        var elsewhere = Recorded(context, corpus.Root, DeepgramFixtures.TwoChannelShort);
        MeetingRenderer.Render(context, meeting, When);
        var word = context.Utterances.First(turn => turn.MeetingId == meeting).Text.Split(' ')[0];

        new HumanLayer(context, TimeProvider.System).Correct(word, "AJENO", meetingId: elsewhere);

        var rendered = MeetingRenderer.Render(context, meeting, When);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldNotContain("AJENO");
    }

    [Fact]
    public void A_meeting_with_no_response_says_so_rather_than_rendering_nothing()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Meeting(context, SourceProfile.Multichannel);

        var refused = Should.Throw<RenderException>(
            () => MeetingRenderer.Render(context, meeting, When));

        refused.Message.ShouldContain(meeting.ToString());
    }

    [Fact]
    public void A_meeting_that_is_not_there_says_so()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<RenderException>(
            () => MeetingRenderer.Render(context, Guid.NewGuid(), When));
    }

    /// <summary>
    /// A render nobody wrapped in a transaction leaves a meeting the turns it already had when the
    /// corpus refuses the new ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>render</c> from the command line, and a filing through <c>MeetingIntake</c>: both
    /// call in with no transaction open, so dropping the turns and saving the new ones are two
    /// separately committed statements and a refusal in between is not something any caller can
    /// undo. A rebuild is what somebody runs over a corpus and a single render is what they run
    /// over one meeting, so the promise cannot hold only on the first.
    /// </para>
    /// <para>
    /// Rendered once first, because a meeting with no turns has nothing to lose and the state worth
    /// probing is the second render of a meeting that has them. The response is then swapped
    /// underneath for one whose confidences <c>ck_utterances_confidence</c> refuses: the parser
    /// carries that number exactly as sent, so it is the one refusal that arrives from inside the
    /// save with the meeting's turns already staged.
    /// </para>
    /// <para>
    /// Named down to that constraint rather than left at <c>Should.Throw&lt;Exception&gt;</c>, which
    /// any refusal satisfies. The point of this probe is the window between the delete and the save,
    /// and a refusal that started arriving earlier — the response file gone, the parser stopping —
    /// would keep it green while never reaching that window at all, since a render refused before
    /// the delete leaves the turns alone for free. So it is a <c>DbUpdateException</c> naming the
    /// check the corpus refused on, which is the only way in.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_render_outside_a_transaction_leaves_a_refused_meeting_the_turns_it_had()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        MeetingRenderer.Render(context, meeting, When);

        var had = Turns(context, meeting);
        had.ShouldNotBeEmpty();
        context.Database.CurrentTransaction.ShouldBeNull();
        OffTheScale(context, corpus.Root, meeting);

        var refused = Should.Throw<DbUpdateException>(
            () => MeetingRenderer.Render(context, meeting, When));

        refused.InnerException.ShouldNotBeNull().Message.ShouldContain("ck_utterances_confidence");
        Turns(context, meeting).ShouldBe(had);
    }

    /// <summary>
    /// Every kind of row the model hangs off a turn is one <c>MeetingRenderer.Cited</c> asks about
    /// before it replaces that meeting's turns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the model rather than off the schema, for the reason
    /// <c>ExtractionPositionTests</c> gives about its own closed set: what makes a row one of these
    /// is a decision somebody takes in the domain, and the mapping is where it lands.
    /// </para>
    /// <para>
    /// It is a test rather than a comment because drift here is silent and expensive. A fourth
    /// projection carrying evidence, not added to <c>Cited</c>, compiles and passes everything: its
    /// dangling citation is then found where it used to be found, at the rebuild's corpus-wide
    /// commit, outside every per-meeting guard — taking every meeting the run had rebuilt and the
    /// report naming the one it could not. A silent regression to the whole-run cost is the worst
    /// shape drift could take, so the set is held rather than described.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_kind_of_claim_the_model_hangs_off_a_turn_is_one_the_renderer_asks_about()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        var citing = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(Utterance))
            .Select(key => key.DeclaringEntityType.GetTableName()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        citing.ShouldBe(
            ["action_items", "decisions", "open_questions"],
            "MeetingRenderer.Cited asks exactly these what they cite, so a kind it does not ask is "
            + "a claim left pointing at nothing until the rebuild's corpus-wide commit finds it — "
            + "outside every guard, taking the whole run and the report with it.");
    }

    /// <summary>
    /// A render that cannot write the second file leaves both of the meeting's files as they were,
    /// with nobody holding a transaction over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller with none of its own is <c>render</c> from the command line and the catch-up that
    /// produces what a meeting is owed. Until the pair was staged whole this render opened a
    /// transaction of its own here, and that never made the two files either-or: the transcript's
    /// file had moved and its row had been saved before the jsonl was written anywhere, so a
    /// refusal between them left one file of each generation whether or not a transaction was open.
    /// Rolling it back would not have helped and is the one thing the corpus may not do, because the
    /// row would go back under a file that has already moved.
    /// </para>
    /// <para>
    /// A resolved speaker is what tells the two renders apart: it reaches <c>transcript.md</c>,
    /// which is a person's file, and deliberately not the jsonl, which stays comparable to the raw
    /// response. So the assertion is not that nothing happened — it is that the transcript is still
    /// the one the jsonl beside it belongs to.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_render_that_cannot_write_the_second_file_leaves_both_of_them_as_they_were()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        var first = MeetingRenderer.Render(context, meeting, When);
        var transcript = CorpusFiles.Locate(corpus.Root, first.Transcript.RelativePath).FullName;
        var utterances = CorpusFiles.Locate(corpus.Root, first.Utterances.RelativePath).FullName;
        var had = (Transcript: File.ReadAllText(transcript), Utterances: File.ReadAllText(utterances));

        Resolved(context, meeting, "ch1:speaker_0", "Renata");

        context.Database.CurrentTransaction.ShouldBeNull();
        using (new FileStream(utterances, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // The filesystem's own refusal, unwrapped: what asks whether the jsonl can be taken is
            // the rename the replace would have done, so what comes back is what that rename said.
            Should.Throw<IOException>(() => MeetingRenderer.Render(context, meeting, When));
        }

        File.ReadAllText(transcript).ShouldBe(had.Transcript);
        File.ReadAllText(utterances).ShouldBe(had.Utterances);
        ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();

        // And the name the refused render would have written is one that would have shown, so the
        // two readings above are of two generations rather than of one twice.
        had.Transcript.ShouldNotContain("Renata");
        MeetingRenderer.Render(context, meeting, When);
        File.ReadAllText(transcript).ShouldContain("Renata");
    }

    /// <summary>Every turn of one meeting as text, for comparing a render against the one before.</summary>
    private static List<string> Turns(CorpusDbContext context, Guid meeting) =>
    [
        .. context.Utterances
            .Where(turn => turn.MeetingId == meeting)
            .OrderBy(turn => turn.Ordinal)
            .AsEnumerable()
            .Select(turn => $"{turn.Ordinal}|{turn.SpeakerLabel}|{turn.Confidence}|{turn.Text}"),
    ];

    /// <summary>
    /// The meeting's filed response replaced by one carrying confidences the corpus will not store,
    /// leaving the row that names it alone.
    /// </summary>
    /// <remarks>
    /// The row is left alone on purpose: nothing on the render path checks a response against the
    /// size and the hash its row carries, so the swap is invisible to a render, which is what a
    /// folder half restored from somewhere else comes to.
    /// </remarks>
    private static void OffTheScale(CorpusDbContext context, DirectoryInfo root, Guid meeting)
    {
        var filed = context.Artifacts.Single(artifact =>
            artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.DeepgramResponse);
        var path = CorpusFiles.Locate(root, filed.RelativePath).FullName;

        File.WriteAllText(
            path, CorruptedResponses.WithConfidenceOffTheScale(File.ReadAllText(path)));
    }

    /// <summary>
    /// The one assignment a render makes without being asked: the microphone caught one voice, and
    /// somebody has said whose microphone it is.
    /// </summary>
    /// <remarks>
    /// The three on the loopback are the control. They come through the same render and stay as
    /// the labels the provider wrote, because which of them is who is exactly what the recording
    /// cannot know.
    /// </remarks>
    [Fact]
    public void The_microphones_own_voice_reads_as_whoever_said_they_are_using_this()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        var ada = new HumanLayer(context, When).ThisIsMe("Ada");

        var rendered = MeetingRenderer.Render(context, meeting, When);

        var settled = context.SpeakerAssignments.Single();
        settled.SpeakerLabel.ShouldBe("ch1:speaker_0");
        settled.PersonId.ShouldBe(ada.Id);
        settled.AssignedBy.ShouldBe(SpeakerAssignmentSource.Channel);

        var transcript = File.ReadAllText(
            CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName);

        transcript.ShouldContain("## Ada — ");
        transcript.ShouldNotContain("ch1:speaker_0");
        transcript.ShouldContain("## ch0:speaker_0 — ");
    }

    /// <summary>
    /// The same meeting in a corpus nobody has answered in. There is nobody to settle a voice
    /// onto, so every speaker reads as its label — which is the state the question on the opening
    /// screen exists to end.
    /// </summary>
    [Fact]
    public void A_meeting_rendered_before_anybody_said_who_is_using_this_names_nobody()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);

        var rendered = MeetingRenderer.Render(context, meeting, When);

        context.SpeakerAssignments.ShouldBeEmpty();
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldContain("## ch1:speaker_0 — ");
    }

    /// <summary>
    /// The answer arriving after the meeting did. Every door that derives a meeting's turns comes
    /// through here — filing a response, rendering one at a prompt, rebuilding, and the sweep the
    /// application runs at launch — so which of them somebody happens to run next cannot decide
    /// whether their own voice reads as a name.
    /// </summary>
    /// <remarks>
    /// Three contexts, closed between: the corpus is what carries this from one to the next, and a
    /// single warm one would let the change tracker answer where two commands on two days will not.
    /// </remarks>
    [Fact]
    public void Rendering_again_after_the_answer_arrives_names_a_meeting_that_had_nobody()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting;

        using (var filing = corpus.OpenMigrated())
        {
            meeting = Recorded(filing, corpus.Root);
            MeetingRenderer.Render(filing, meeting, When);
            filing.SpeakerAssignments.ShouldBeEmpty();
        }

        using (var answering = corpus.OpenMigrated())
        {
            new HumanLayer(answering, When).ThisIsMe("Ada");
        }

        using var rendering = corpus.OpenMigrated();
        var rendered = MeetingRenderer.Render(rendering, meeting, When);

        rendering.SpeakerAssignments.Single().SpeakerLabel.ShouldBe("ch1:speaker_0");
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldContain("## Ada — ");
    }

    /// <summary>
    /// A render derives what the response says and never overrules what somebody said. The label
    /// the microphone would have settled is already somebody else's answer, so it stays theirs.
    /// </summary>
    [Fact]
    public void What_the_microphone_would_settle_never_overrules_what_a_person_resolved()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        new HumanLayer(context, When).ThisIsMe("Ada");
        Resolved(context, meeting, "ch1:speaker_0", "Jo");

        var rendered = MeetingRenderer.Render(context, meeting, When);

        context.SpeakerAssignments.Single().AssignedBy.ShouldBe(SpeakerAssignmentSource.Person);
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldContain("## Jo — ");
    }

    /// <summary>Somebody saying who a speaker label is, which reaches the transcript and no row.</summary>
    private static void Resolved(CorpusDbContext context, Guid meeting, string label, string name)
    {
        var person = new Person
        {
            Id = Guid.NewGuid(),
            DisplayName = name,
            CreatedAt = When,
            UpdatedAt = When,
        };
        context.People.Add(person);
        context.SpeakerAssignments.Add(new SpeakerAssignment
        {
            MeetingId = meeting,
            SpeakerLabel = label,
            PersonId = person.Id,
            AssignedBy = SpeakerAssignmentSource.Person,
            AssignedAt = When,
        });
        context.SaveChanges();
    }

    /// <summary>A meeting with its paid response in the corpus, ready to be rendered from.</summary>
    private static Guid Recorded(
        CorpusDbContext context,
        DirectoryInfo root,
        string fixture = DeepgramFixtures.TwoChannelOneVoiceMe)
    {
        var meeting = Meeting(context, DeepgramFixtures.ProfileOf(fixture));

        DurableArtifact.Write(
            context,
            meeting,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting, "deepgram.json"),
            When,
            stream =>
            {
                using var response = File.OpenRead(DeepgramFixtures.PathOf(fixture));
                response.CopyTo(stream);
            });

        return meeting;
    }

    private static Guid Meeting(CorpusDbContext context, SourceProfile profile)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = When,
            SourceProfile = profile,
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();
        return meeting.Id;
    }
}
