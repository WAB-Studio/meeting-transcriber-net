using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Correcting a person's name and rendering again every meeting whose voices name them, in the same
/// transaction, so the transcripts say what the screens say.
/// </summary>
public class RenamingSomebodyTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    /// <summary>
    /// Two meetings named on by the same person and a third that does not name them: the rename
    /// reaches the first two transcripts and leaves the third's sha256 exactly where it was.
    /// </summary>
    [Fact]
    public void Correcting_a_name_reaches_every_transcript_their_voice_is_on()
    {
        using var corpus = new TemporaryCorpus();
        var first = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var firstLabel);
        var second = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var secondLabel);
        var untouched = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);

        Guid somebody;
        string untouchedTranscriptShaBefore;
        string untouchedResponseShaBefore;
        string untouchedTextBefore;

        using (var context = corpus.OpenMigrated())
        {
            var human = new HumanLayer(context, When);
            somebody = human.Add("Renata").Id;
            human.Assign(first, firstLabel, context.People.Single(person => person.Id == somebody));
            human.Assign(second, secondLabel, context.People.Single(person => person.Id == somebody));

            var response = context.Artifacts.Single(
                artifact => artifact.MeetingId == untouched && artifact.Kind == ArtifactKind.DeepgramResponse);
            var transcript = context.Artifacts.Single(
                artifact => artifact.MeetingId == untouched && artifact.Kind == ArtifactKind.Transcript);
            untouchedResponseShaBefore = CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, response.RelativePath));
            untouchedTranscriptShaBefore = transcript.Sha256;
            untouchedTextBefore = context.Utterances
                .Where(turn => turn.MeetingId == untouched)
                .OrderBy(turn => turn.Ordinal)
                .Select(turn => turn.Text)
                .First();
        }

        Person person;
        using (var context = corpus.OpenMigrated())
        {
            person = context.People.Single(row => row.Id == somebody);
        }

        var renamed = RenamingSomebody.Rename(corpus.Root, person, "Renata Corregida", TimeProvider.System);
        renamed.ShouldNotBeNull();

        using var reading = corpus.OpenMigrated();
        Transcript(reading, corpus.Root, first).ShouldContain("## Renata Corregida");
        Transcript(reading, corpus.Root, second).ShouldContain("## Renata Corregida");

        var untouchedResponse = reading.Artifacts.Single(
            artifact => artifact.MeetingId == untouched && artifact.Kind == ArtifactKind.DeepgramResponse);
        var untouchedTranscript = reading.Artifacts.Single(
            artifact => artifact.MeetingId == untouched && artifact.Kind == ArtifactKind.Transcript);
        CorpusFiles.Sha256Of(CorpusFiles.Locate(corpus.Root, untouchedResponse.RelativePath))
            .ShouldBe(untouchedResponseShaBefore);
        untouchedTranscript.Sha256.ShouldBe(untouchedTranscriptShaBefore);
        reading.Utterances
            .Where(turn => turn.MeetingId == untouched)
            .OrderBy(turn => turn.Ordinal)
            .Select(turn => turn.Text)
            .First()
            .ShouldBe(untouchedTextBefore);
    }

    /// <summary>
    /// A render that cannot happen takes the rename back with it: the person still reads by the old
    /// name once one of their meetings could not be rendered again.
    /// </summary>
    [Fact]
    public void A_render_that_is_refused_takes_the_rename_back_with_it()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var label);
        Guid somebody;

        using (var context = corpus.OpenMigrated())
        {
            var human = new HumanLayer(context, When);
            somebody = human.Add("Renata").Id;
            human.Assign(meeting, label, context.People.Single(row => row.Id == somebody));

            var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
            File.Delete(CorpusFiles.Locate(corpus.Root, response.RelativePath).FullName);
        }

        Person person;
        using (var context = corpus.OpenMigrated())
        {
            person = context.People.Single(row => row.Id == somebody);
        }

        Should.Throw<RenderException>(
            () => RenamingSomebody.Rename(corpus.Root, person, "Renata Corregida", TimeProvider.System));

        using var reading = corpus.OpenMigrated();
        reading.People.Single(row => row.Id == somebody).DisplayName.ShouldBe("Renata");
    }

    /// <summary>A person this corpus no longer holds is answered with nothing, and renders nothing.</summary>
    [Fact]
    public void A_person_this_corpus_no_longer_holds_is_answered_with_nothing()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var label);
        Guid somebodyId;

        using (var context = corpus.OpenMigrated())
        {
            var human = new HumanLayer(context, When);
            var somebody = human.Add("Renata");
            somebodyId = somebody.Id;
            human.Assign(meeting, label, somebody);
            context.People.Remove(context.People.Single(row => row.Id == somebodyId));
            context.SpeakerAssignments.RemoveRange(
                context.SpeakerAssignments.Where(row => row.PersonId == somebodyId));
            context.SaveChanges();
        }

        var gone = new Person { Id = somebodyId, DisplayName = "Renata" };

        RenamingSomebody.Rename(corpus.Root, gone, "Renata Corregida", TimeProvider.System)
            .ShouldBeNull();
    }

    private static string Transcript(CorpusDbContext context, DirectoryInfo root, Guid meeting)
    {
        var transcript = context.Artifacts.Single(
            artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Transcript);

        return File.ReadAllText(CorpusFiles.Locate(root, transcript.RelativePath).FullName);
    }
}
