using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Processing.Tests.Deepgram;

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

        var rendered = MeetingRenderer.Render(context, corpus.Root, meeting, When);

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

        MeetingRenderer.Render(context, corpus.Root, meeting, When);

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

        var first = MeetingRenderer.Render(context, corpus.Root, meeting, When);
        var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
        var responseSha = CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, response.RelativePath));

        var second = MeetingRenderer.Render(context, corpus.Root, meeting, When + Duration.FromMilliseconds(60_000));

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
        MeetingRenderer.Render(context, corpus.Root, meeting, When);

        var spoken = context.Utterances.First(turn => turn.MeetingId == meeting);
        var word = spoken.Text.Split(' ')[0];
        var human = new HumanLayer(context, TimeProvider.System);
        var somebody = human.Add("Renata");
        human.Assign(meeting, spoken.SpeakerLabel, somebody);
        human.Correct(word, "CORREGIDO");

        var rendered = MeetingRenderer.Render(context, corpus.Root, meeting, When);
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
        MeetingRenderer.Render(context, corpus.Root, meeting, When);
        var word = context.Utterances.First(turn => turn.MeetingId == meeting).Text.Split(' ')[0];

        var human = new HumanLayer(context, TimeProvider.System);
        var techsed = human.Root(NodeKind.Organization, "TechSed");
        var coati = human.Under(techsed, NodeKind.Initiative, "Coati");
        human.Link(meeting, coati, MeetingNodeRole.WorkOf);
        human.Correct(word, "DESDE ARRIBA", under: techsed);

        var rendered = MeetingRenderer.Render(context, corpus.Root, meeting, When);

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
        MeetingRenderer.Render(context, corpus.Root, meeting, When);
        var word = context.Utterances.First(turn => turn.MeetingId == meeting).Text.Split(' ')[0];

        new HumanLayer(context, TimeProvider.System).Correct(word, "AJENO", meetingId: elsewhere);

        var rendered = MeetingRenderer.Render(context, corpus.Root, meeting, When);

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
            () => MeetingRenderer.Render(context, corpus.Root, meeting, When));

        refused.Message.ShouldContain(meeting.ToString());
    }

    [Fact]
    public void A_meeting_that_is_not_there_says_so()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();

        Should.Throw<RenderException>(
            () => MeetingRenderer.Render(context, corpus.Root, Guid.NewGuid(), When));
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
            root,
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
