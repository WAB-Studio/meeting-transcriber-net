using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Saving the names somebody puts on a meeting's voices, and rendering that meeting's files again
/// in the same transaction, so a name saved is a name the transcript already shows.
/// </summary>
public class NamingTheVoicesTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    /// <summary>
    /// The card's own proof: naming a voice reaches the transcript, and every source — the paid
    /// response and the turns it was projected from — stays exactly as it was.
    /// </summary>
    [Fact]
    public void Naming_a_voice_reaches_the_transcript_and_leaves_every_source_alone()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var label);

        string responseSha;
        List<string> texts;
        using (var context = corpus.OpenMigrated())
        {
            responseSha = CorpusFiles.Sha256Of(CorpusFiles.Locate(
                corpus.Root,
                context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse).RelativePath));
            texts = [.. context.Utterances.Where(turn => turn.MeetingId == meeting)
                .OrderBy(turn => turn.Ordinal)
                .Select(turn => turn.Text)];
        }

        var somebody = Added(corpus, "Renata");

        var changed = NamingTheVoices.Save(
            corpus.Root, meeting, [new VoiceAnswer(label, somebody)], TimeProvider.System);

        changed.ShouldBe(1);

        using var reading = corpus.OpenMigrated();
        var transcript = reading.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.Transcript);
        File.ReadAllText(CorpusFiles.Locate(corpus.Root, transcript.RelativePath).FullName)
            .ShouldContain("## Renata");

        var response = reading.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, response.RelativePath)).ShouldBe(responseSha);

        reading.Utterances.Where(turn => turn.MeetingId == meeting)
            .OrderBy(turn => turn.Ordinal)
            .Select(turn => turn.Text)
            .ShouldBe(texts);
    }

    /// <summary>
    /// A render that cannot happen takes the names back with it: the assignment and the render land
    /// together or not at all.
    /// </summary>
    [Fact]
    public void A_render_that_is_refused_takes_the_names_back_with_it()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var label);
        var somebody = Added(corpus, "Renata");

        using (var context = corpus.OpenMigrated())
        {
            var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
            File.Delete(CorpusFiles.Locate(corpus.Root, response.RelativePath).FullName);
        }

        Should.Throw<RenderException>(() => NamingTheVoices.Save(
            corpus.Root, meeting, [new VoiceAnswer(label, somebody)], TimeProvider.System));

        using var reading = corpus.OpenMigrated();
        reading.SpeakerAssignments.ShouldBeEmpty();
    }

    /// <summary>
    /// Saving exactly what is already there changes nothing, so it renders nothing — a meeting whose
    /// response is gone is not disturbed by a save that named nobody new.
    /// </summary>
    [Fact]
    public void Saving_what_is_already_there_renders_nothing()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var label);
        var somebody = Added(corpus, "Renata");
        NamingTheVoices.Save(corpus.Root, meeting, [new VoiceAnswer(label, somebody)], TimeProvider.System);

        using (var context = corpus.OpenMigrated())
        {
            var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
            File.Delete(CorpusFiles.Locate(corpus.Root, response.RelativePath).FullName);
        }

        var changed = NamingTheVoices.Save(
            corpus.Root, meeting, [new VoiceAnswer(label, somebody)], TimeProvider.System);

        changed.ShouldBe(0);
    }

    private static Guid Added(TemporaryCorpus corpus, string displayName)
    {
        using var context = corpus.OpenMigrated();
        return new HumanLayer(context, When).Add(displayName).Id;
    }
}
