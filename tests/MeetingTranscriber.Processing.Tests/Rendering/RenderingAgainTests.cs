using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// Rendering a meeting that has already been rendered: the check every claim still lands on a turn
/// is deferred to the commit rather than asked at the delete, which is what makes producing the
/// file possible at all.
/// </summary>
public class RenderingAgainTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// A meeting that has a summary — so a plain <see cref="MeetingRenderer.Render"/> would refuse at
    /// the delete of its turns — renders again once the foreign keys are deferred, and its claims
    /// land back on the turns they cited.
    /// </summary>
    [Fact]
    public void A_summarised_meeting_is_rendered_again_with_its_claims_where_they_were()
    {
        using var corpus = new TemporaryCorpus();
        using var context = corpus.OpenMigrated();
        var meeting = Recorded(context, corpus.Root);
        MeetingRenderer.Render(context, meeting, When);
        MeetingRows.Extracted(context, meeting, When, accepted: When, "lo que se dijo");

        var spoken = context.Utterances.First(turn => turn.MeetingId == meeting);
        var human = new HumanLayer(context, When);
        var somebody = human.Add("Renata");
        human.Assign(meeting, spoken.SpeakerLabel, somebody);

        var rendered = RenderingAgain.OneMeeting(context, meeting, When);

        File.ReadAllText(CorpusFiles.Locate(corpus.Root, rendered.Transcript.RelativePath).FullName)
            .ShouldContain("## Renata");
        context.Decisions.Count(row => row.MeetingId == meeting).ShouldBe(1);
        context.ActionItems.Count(row => row.MeetingId == meeting).ShouldBe(1);
        context.OpenQuestions.Count(row => row.MeetingId == meeting).ShouldBe(1);
    }

    /// <summary>
    /// A caller with no transaction of its own still gets one committed: the pragma is a no-op
    /// outside a transaction, so without a commit here the render would either fail on the claim or
    /// leave the turns it wrote undone the moment the connection let the transaction go.
    /// </summary>
    [Fact]
    public void A_render_again_outside_a_transaction_commits_what_it_wrote()
    {
        using var corpus = new TemporaryCorpus();
        Guid meeting;
        List<Guid> firstIds;

        using (var context = corpus.OpenMigrated())
        {
            meeting = Recorded(context, corpus.Root);
            MeetingRenderer.Render(context, meeting, When);
            firstIds = [.. context.Utterances.Where(turn => turn.MeetingId == meeting).Select(turn => turn.Id)];
        }

        using (var context = corpus.OpenMigrated())
        {
            context.Database.CurrentTransaction.ShouldBeNull();
            RenderingAgain.OneMeeting(context, meeting, When + Duration.FromMilliseconds(60_000));
        }

        using var reading = corpus.OpenMigrated();
        var secondIds = reading.Utterances
            .Where(turn => turn.MeetingId == meeting)
            .Select(turn => turn.Id)
            .ToList();

        // A committed transaction leaves the swap's fresh ids on disk; a rolled-back one puts the
        // first render's ids back, which is indistinguishable from the render never having happened.
        secondIds.ShouldNotBe(firstIds);
    }

    private static Guid Recorded(CorpusDbContext context, DirectoryInfo root)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = When,
            SourceProfile = DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe),
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
            stream =>
            {
                using var response = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));
                response.CopyTo(stream);
            });

        return meeting.Id;
    }
}
