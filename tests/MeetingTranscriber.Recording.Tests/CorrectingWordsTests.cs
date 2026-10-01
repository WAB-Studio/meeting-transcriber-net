using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// Saving the forms a word came out wrong in and rendering every meeting they touch, so the
/// transcripts say what the screen that saved them promised and the stored turns say what they
/// always did.
/// </summary>
public class CorrectingWordsTests
{
    private static readonly UtcTimestamp When = UtcTimestamp.Parse("2026-09-25T09:00:00.000Z");

    [Fact]
    public void A_word_corrected_everywhere_reaches_every_transcript_that_says_it_and_no_stored_turn()
    {
        using var corpus = new TemporaryCorpus();
        var first = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        var second = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        string word;
        string[] turnsBefore;
        string[] responsesBefore;

        using (var context = corpus.OpenMigrated())
        {
            word = FirstWord(context, first);
            turnsBefore = TurnTexts(context);
            responsesBefore = ResponseHashes(context, corpus.Root);
        }

        var corrected = CorrectingWords.Correct(corpus.Root, "Corregida", [word], under: null, TimeProvider.System);

        corrected.Rendered.ShouldBe([first, second], ignoreOrder: true);

        using var reading = corpus.OpenMigrated();
        Transcript(reading, corpus.Root, first).ShouldContain("Corregida");
        Transcript(reading, corpus.Root, second).ShouldContain("Corregida");
        TurnTexts(reading).ShouldBe(turnsBefore);
        ResponseHashes(reading, corpus.Root).ShouldBe(responsesBefore);
    }

    [Fact]
    public void A_word_corrected_under_one_node_reaches_only_the_meetings_filed_there()
    {
        using var corpus = new TemporaryCorpus();
        var onA = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        var underA = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        var onB = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        var unfiled = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        Guid a;
        string word;

        using (var context = corpus.OpenMigrated())
        {
            var human = new HumanLayer(context, When);
            var organizationA = human.Root(NodeKind.Organization, "A");
            var project = human.Under(organizationA, NodeKind.Initiative, "Proyecto");
            var organizationB = human.Root(NodeKind.Organization, "B");
            human.Link(onA, organizationA, MeetingNodeRole.WorkOf);
            human.Link(underA, project, MeetingNodeRole.WorkOf);
            human.Link(onB, organizationB, MeetingNodeRole.WorkOf);
            a = organizationA.Id;
            word = FirstWord(context, onA);
        }

        CorrectingWords.Correct(corpus.Root, "Corregida", [word], a, TimeProvider.System);

        using (var reading = corpus.OpenMigrated())
        {
            Transcript(reading, corpus.Root, onA).ShouldContain("Corregida");
            Transcript(reading, corpus.Root, underA).ShouldContain("Corregida");
            Transcript(reading, corpus.Root, onB).ShouldNotContain("Corregida");
            Transcript(reading, corpus.Root, unfiled).ShouldNotContain("Corregida");
        }

        CorrectingWords.Correct(corpus.Root, "Corregida", [word], under: null, TimeProvider.System);

        using var again = corpus.OpenMigrated();
        Transcript(again, corpus.Root, unfiled).ShouldContain("Corregida");
        Transcript(again, corpus.Root, onB).ShouldContain("Corregida");
    }

    /// <summary>
    /// What the screen hands over is lower-cased and a turn opens with a capital, so a correction
    /// that matched exactly would never reach the word it was found from.
    /// </summary>
    [Fact]
    public void What_is_corrected_is_matched_whatever_its_case()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        string word;

        using (var context = corpus.OpenMigrated())
        {
            word = FirstWord(context, meeting);
        }

        CorrectingWords.Correct(corpus.Root, "Corregida", [word.ToUpperInvariant()], under: null, TimeProvider.System);

        using var reading = corpus.OpenMigrated();
        Transcript(reading, corpus.Root, meeting).ShouldContain("Corregida");
        reading.TerminologyCorrections.Single().MatchMode.ShouldBe(TerminologyMatchMode.IgnoreCase);
    }

    /// <summary>
    /// <c>deepgram</c> to <c>Deepgram</c> is a correction, and it is the one the screen's lower-cased
    /// forms exist to make. Only a form identical to the right word is nothing to save.
    /// </summary>
    [Fact]
    public void A_form_that_differs_from_the_right_word_by_case_alone_is_saved()
    {
        using var corpus = new TemporaryCorpus();
        ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);

        var corrected = CorrectingWords.Correct(corpus.Root, "Deepgram", ["deepgram", "Deepgram"], under: null, TimeProvider.System);

        corrected.Saved.Select(saved => saved.WrongText).ShouldBe(["deepgram"]);
    }

    [Fact]
    public void A_form_already_corrected_in_that_place_is_replaced_and_not_added_twice()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        string word;

        using (var context = corpus.OpenMigrated())
        {
            word = FirstWord(context, meeting);
        }

        CorrectingWords.Correct(corpus.Root, "Primera", [word], under: null, TimeProvider.System);
        CorrectingWords.Correct(corpus.Root, "Segunda", [word.ToUpperInvariant()], under: null, TimeProvider.System);

        using var reading = corpus.OpenMigrated();
        reading.TerminologyCorrections.Single().CorrectText.ShouldBe("Segunda");
        Transcript(reading, corpus.Root, meeting).ShouldContain("Segunda");
        Transcript(reading, corpus.Root, meeting).ShouldNotContain("Primera");
    }

    [Fact]
    public void A_meeting_that_does_not_say_the_word_is_not_rendered_again()
    {
        using var corpus = new TemporaryCorpus();
        var says = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        var silent = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        string word;
        string shaBefore;

        using (var context = corpus.OpenMigrated())
        {
            word = FirstWord(context, says);

            // The stored turns are what the rule reads, so this stands for a meeting that never said it.
            foreach (var turn in context.Utterances.Where(turn => turn.MeetingId == silent))
            {
                turn.Text = "nada de eso";
            }

            context.SaveChanges();
            shaBefore = context.Artifacts.Single(
                artifact => artifact.MeetingId == silent && artifact.Kind == ArtifactKind.Transcript).Sha256;
        }

        var corrected = CorrectingWords.Correct(corpus.Root, "Corregida", [word], under: null, TimeProvider.System);

        corrected.Rendered.ShouldBe([says]);

        using var reading = corpus.OpenMigrated();
        reading.Artifacts.Single(
            artifact => artifact.MeetingId == silent && artifact.Kind == ArtifactKind.Transcript)
            .Sha256.ShouldBe(shaBefore);
    }

    [Fact]
    public void A_render_that_is_refused_leaves_the_correction_standing_and_names_the_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var meeting = ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);
        string word;

        using (var context = corpus.OpenMigrated())
        {
            word = FirstWord(context, meeting);
            var response = context.Artifacts.Single(artifact => artifact.Kind == ArtifactKind.DeepgramResponse);
            File.Delete(CorpusFiles.Locate(corpus.Root, response.RelativePath).FullName);
        }

        var thrown = Should.Throw<RenderException>(
            () => CorrectingWords.Correct(corpus.Root, "Corregida", [word], under: null, TimeProvider.System));
        thrown.Message.ShouldContain(meeting.ToString());

        using var reading = corpus.OpenMigrated();
        reading.TerminologyCorrections.Single().CorrectText.ShouldBe("Corregida");
    }

    [Fact]
    public void A_screen_defect_is_refused_before_anything_is_written()
    {
        using var corpus = new TemporaryCorpus();
        ARenderedVoiceMeeting.RenderedIn(corpus, When, out _);

        Should.Throw<ArgumentException>(
            () => CorrectingWords.Correct(corpus.Root, " ", ["hola"], under: null, TimeProvider.System));
        Should.Throw<ArgumentException>(
            () => CorrectingWords.Correct(corpus.Root, "Hola", [], under: null, TimeProvider.System));
        Should.Throw<ArgumentException>(
            () => CorrectingWords.Correct(corpus.Root, "Hola", ["hola", " "], under: null, TimeProvider.System));
        Should.Throw<ArgumentException>(
            () => CorrectingWords.Correct(corpus.Root, "Hola", ["hola"], Guid.NewGuid(), TimeProvider.System));

        using var reading = corpus.OpenMigrated();
        reading.TerminologyCorrections.ShouldBeEmpty();
    }

    private static string FirstWord(CorpusDbContext context, Guid meeting) => Spellings.WordsOf(
        context.Utterances
            .Where(turn => turn.MeetingId == meeting)
            .OrderBy(turn => turn.Ordinal)
            .Select(turn => turn.Text)
            .First())[0].ToLowerInvariant();

    private static string[] TurnTexts(CorpusDbContext context) => context.Utterances
        .OrderBy(turn => turn.MeetingId)
        .ThenBy(turn => turn.Ordinal)
        .Select(turn => turn.Text)
        .ToArray();

    private static string[] ResponseHashes(CorpusDbContext context, DirectoryInfo root) => context.Artifacts
        .Where(artifact => artifact.Kind == ArtifactKind.DeepgramResponse)
        .OrderBy(artifact => artifact.MeetingId)
        .ToArray()
        .Select(artifact => CorpusFiles.Sha256Of(CorpusFiles.Locate(root, artifact.RelativePath)))
        .ToArray();

    private static string Transcript(CorpusDbContext context, DirectoryInfo root, Guid meeting)
    {
        var transcript = context.Artifacts.Single(
            artifact => artifact.MeetingId == meeting && artifact.Kind == ArtifactKind.Transcript);

        return File.ReadAllText(CorpusFiles.Locate(root, transcript.RelativePath).FullName);
    }
}
