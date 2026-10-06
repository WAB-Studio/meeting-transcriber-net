using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The dialogue that corrects one word where it is read, held to what no rule of its own can
/// reach: that it saves through the one door corrections go through, that what reaches it is one
/// word, that a word a correction already wrote offers no save, and that both screens open it.
/// </summary>
/// <remarks>
/// What a correction does to a corpus is <c>CorrectingWordsTests</c>, which runs. What it cannot see
/// is whether this dialogue writes through that door and nowhere else. Read out of source because
/// opening a window is what it would otherwise take, and no build agent has one.
/// </remarks>
public class CorrectingAWordTests
{
    private static readonly string Dialogue =
        Path.Combine("MeetingTranscriber.App", "CorrectingAWord.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "CorrectingAWord.xaml");

    private static readonly string[] Screens =
    [
        Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs"),
        Path.Combine("MeetingTranscriber.App", "SayingWhoIsWho.xaml.cs"),
    ];

    /// <summary>
    /// A correction is saved through <c>CorrectingWords.Correct</c> exactly once and nothing else,
    /// because that is the door that commits it and renders again every meeting it touches.
    /// </summary>
    [Fact]
    public void Saving_goes_through_the_one_door_corrections_go_through()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        SourceLines.Occurrences(dialogue, "CorrectingWords.Correct(").Count().ShouldBe(
            1,
            "the dialogue no longer saves through CorrectingWords.Correct exactly once.");

        SourceLines.Occurrences(dialogue, "HumanLayer").ShouldBeEmpty(
            "this dialogue names HumanLayer, which writes a correction without rendering anything.");

        SourceLines.Occurrences(dialogue, "TerminologyCorrections.Add").ShouldBeEmpty(
            "this dialogue adds a correction itself, which promises a transcript nothing rendered.");

        // Off the UI thread: a render of every meeting a word appears in is not a frame's work.
        dialogue.ShouldContain("await Task.Run(() => CorrectingWords.Correct(");
    }

    /// <summary>
    /// What reaches the dialogue is one word, found by the rule corrections are applied with, so a
    /// word with its comma, or a stretch across a space, never becomes a form.
    /// </summary>
    [Fact]
    public void The_selection_is_trimmed_to_one_word_by_the_rule_corrections_are_applied_with()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        dialogue.ShouldContain("Spellings.WordsOf(selection)");

        foreach (var path in Screens)
        {
            var screen = File.ReadAllText(AppSources.At(path).FullName);

            screen.ShouldContain(
                "CorrectingAWord.TheWordIn(",
                customMessage: $"{path} hands the dialogue a selection it did not trim to a word.");
        }
    }

    /// <summary>
    /// A word that a correction already reaching the meeting wrote offers no save: one keyed on the
    /// corrected form would leave the transcript as it is and close as though it had saved.
    /// </summary>
    [Fact]
    public void A_word_that_a_reaching_correction_produced_offers_no_save()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        dialogue.ShouldContain("MeetingRenderer.CorrectionsReaching(context, meeting)");
        dialogue.ShouldContain("correction.CorrectText");
        dialogue.ShouldContain("PrimaryButtonText = string.Empty");

        File.ReadAllText(AppSources.At(Markup).FullName).ShouldContain("UiTexts.AlreadyCorrected");
    }

    /// <summary>
    /// That some meetings are not yet showing a correction is read off the exception's own list of
    /// meetings, and never off its message: rewording the message would otherwise make the dialogue
    /// report a failure for a correction that landed.
    /// </summary>
    [Fact]
    public void A_render_that_could_not_finish_is_read_off_its_type_and_never_its_message()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        dialogue.ShouldContain("catch (RenderException late)");
        dialogue.ShouldContain("late.Meetings.Count");
        dialogue.ShouldNotContain("late.Message");
        dialogue.ShouldNotContain("Regex");

        // It closes as saved: the corrections always land before this is thrown.
        Regex.Match(dialogue, @"catch \(RenderException late\).*?return true;", RegexOptions.Singleline)
            .Success.ShouldBeTrue("a render that could not finish no longer closes the dialogue as saved.");
    }

    /// <summary>The dialogue takes the theme's own notice and not the platform's.</summary>
    [Fact]
    public void The_dialogue_takes_the_notice_style()
    {
        File.ReadAllText(AppSources.At(Markup).FullName)
            .ShouldContain("Style=\"{StaticResource Notice}\"");
    }

    /// <summary>
    /// Both screens that show words somebody may find wrong declare the dialogue and open it, so a
    /// word is corrected where it is read and not only from the screen of every word.
    /// </summary>
    [Fact]
    public void Both_screens_open_it()
    {
        foreach (var path in Screens)
        {
            var screen = File.ReadAllText(AppSources.At(path).FullName);
            var markup = File.ReadAllText(AppSources.At(path[..^3]).FullName);

            markup.ShouldContain(
                "local:CorrectingAWord",
                customMessage: $"{path[..^3]} no longer declares the dialogue that corrects a word.");

            screen.ShouldContain(
                "AskingHowAWordGoes.AskAsync(",
                customMessage: $"{path} never opens the dialogue that corrects a word.");

            screen.ShouldContain("CorrectingAWord.OfferedOver(");
        }
    }
}
