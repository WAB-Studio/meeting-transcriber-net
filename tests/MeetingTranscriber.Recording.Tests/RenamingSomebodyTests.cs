using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Correcting a person's name, committed on its own, and rendering again every meeting whose voices
/// name them, one meeting and one transaction at a time, so the transcripts say what the screens
/// say.
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
    /// A render that cannot happen does not take the rename back with it: the rename already
    /// committed on its own before any render was attempted, and stands, and the exception names
    /// the meeting that is still showing the old name.
    /// </summary>
    [Fact]
    public void A_render_that_is_refused_leaves_the_rename_standing_and_names_the_meeting()
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

        var thrown = Should.Throw<RenderException>(
            () => RenamingSomebody.Rename(corpus.Root, person, "Renata Corregida", TimeProvider.System));
        thrown.Message.ShouldContain(meeting.ToString());
        thrown.Meetings.ShouldBe([meeting]);

        using var reading = corpus.OpenMigrated();
        reading.People.Single(row => row.Id == somebody).DisplayName.ShouldBe("Renata Corregida");
    }

    /// <summary>
    /// Two meetings named on by the same person, the older one refusing its render: the younger one
    /// is still rendered with the new name, and only the older is named in the exception. Proof by
    /// outcome and not by measuring the lock directly — a design that shared one context or one
    /// transaction across both renders would corrupt the younger meeting's save with the older
    /// one's failed change tracker and fail it too, exactly the failure this asserts does not
    /// happen. That corruption is what a lock held across both renders would also make possible, so
    /// a fact independent of that corruption is the fact reachable without a second connection
    /// blocking mid-loop.
    /// </summary>
    [Fact]
    public void Correcting_a_name_lets_go_of_the_lock_between_one_meeting_and_the_next()
    {
        using var corpus = new TemporaryCorpus();
        var failing = ARenderedVoiceMeeting.RenderedIn(corpus, When, out var failingLabel);
        var succeeding = ARenderedVoiceMeeting.RenderedIn(
            corpus, When + Duration.FromSeconds(60), out var succeedingLabel);
        Guid somebody;

        using (var context = corpus.OpenMigrated())
        {
            var human = new HumanLayer(context, When);
            somebody = human.Add("Renata").Id;
            human.Assign(failing, failingLabel, context.People.Single(row => row.Id == somebody));
            human.Assign(succeeding, succeedingLabel, context.People.Single(row => row.Id == somebody));

            var response = context.Artifacts.Single(
                artifact => artifact.MeetingId == failing && artifact.Kind == ArtifactKind.DeepgramResponse);
            File.Delete(CorpusFiles.Locate(corpus.Root, response.RelativePath).FullName);
        }

        Person person;
        using (var context = corpus.OpenMigrated())
        {
            person = context.People.Single(row => row.Id == somebody);
        }

        var thrown = Should.Throw<RenderException>(
            () => RenamingSomebody.Rename(corpus.Root, person, "Renata Corregida", TimeProvider.System));
        thrown.Message.ShouldContain(failing.ToString());
        thrown.Message.ShouldNotContain(succeeding.ToString());

        using var reading = corpus.OpenMigrated();
        reading.People.Single(row => row.Id == somebody).DisplayName.ShouldBe("Renata Corregida");
        Transcript(reading, corpus.Root, succeeding).ShouldContain("## Renata Corregida");
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

    /// <summary>
    /// A person may delete a meeting's transcript and keep the names they gave its voices. Renaming
    /// the person then has no transcript to say the new name on, and must not report one it could not
    /// render. Goes red with the turn filter taken out of <see cref="RenamingSomebody.Rename"/>: the
    /// render of a meeting with no response is refused.
    /// </summary>
    [Fact]
    public void Renaming_somebody_named_on_a_meeting_with_no_transcript_renders_nothing_there()
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

            // The recording this meeting would still have, which is what makes deleting only its
            // transcript something a person is offered.
            context.Add(new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Kind = ArtifactKind.Audio,
                Origin = ArtifactKind.Audio.OriginOf(),
                RelativePath = CorpusFiles.PathFor(meeting, "audio.wav"),
                ByteSize = 4,
                Sha256 = new string('a', 64),
                ConfirmedAt = When,
            });
            context.SaveChanges();

            new MeetingRemoval(context, When).Remove(meeting, MeetingPart.Transcript);
        }

        Person person;
        using (var context = corpus.OpenMigrated())
        {
            context.SpeakerAssignments.Count(row => row.MeetingId == meeting).ShouldBe(1);
            person = context.People.Single(row => row.Id == somebodyId);
        }

        RenamingSomebody.Rename(corpus.Root, person, "Renata Corregida", TimeProvider.System)
            .ShouldNotBeNull();

        using var reading = corpus.OpenMigrated();
        reading.People.Single(row => row.Id == somebodyId).DisplayName.ShouldBe("Renata Corregida");
        reading.Utterances.Any(turn => turn.MeetingId == meeting).ShouldBeFalse();
    }

    private static string Transcript(CorpusDbContext context, DirectoryInfo root, Guid meeting)
    {
        var transcript = context.Artifacts.Single(
            artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Transcript);

        return File.ReadAllText(CorpusFiles.Locate(root, transcript.RelativePath).FullName);
    }
}
