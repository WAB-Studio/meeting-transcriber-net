using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// A meeting rendered once from <see cref="DeepgramFixtures.TwoChannelOneVoiceMe"/>, for
/// <c>NamingTheVoicesTests</c> and <c>RenamingSomebodyTests</c>: both need a meeting whose voices
/// are already there to name or to rename, and neither is the one that renders it for the first
/// time.
/// </summary>
internal static class ARenderedVoiceMeeting
{
    /// <summary>The meeting, and the label its first turn carries.</summary>
    internal static Guid RenderedIn(TemporaryCorpus corpus, UtcTimestamp when, out string label)
    {
        Guid meeting;
        string firstLabel;

        using (var context = corpus.OpenMigrated())
        {
            meeting = Guid.NewGuid();

            context.Meetings.Add(new Meeting
            {
                Id = meeting,
                StartedAt = when,
                SourceProfile = DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe),
                Language = "es",
                CreatedAt = when,
                UpdatedAt = when,
            });
            context.SaveChanges();

            DurableArtifact.Write(
                context,
                meeting,
                ArtifactKind.DeepgramResponse,
                CorpusFiles.PathFor(meeting, "deepgram.json"),
                when,
                stream =>
                {
                    using var response = File.OpenRead(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));
                    response.CopyTo(stream);
                });

            MeetingRenderer.Render(context, meeting, when);
            firstLabel = context.Utterances
                .Where(turn => turn.MeetingId == meeting)
                .OrderBy(turn => turn.Ordinal)
                .First()
                .SpeakerLabel;
        }

        label = firstLabel;
        return meeting;
    }
}
