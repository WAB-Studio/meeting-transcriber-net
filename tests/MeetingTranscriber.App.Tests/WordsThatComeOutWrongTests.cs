using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The screen that corrects words that come out wrong, held to what no rule of its own can reach:
/// that it has a style for everything it builds, that the meeting screen has a way to reach it,
/// and that saving goes through the one door that renders what a correction touches.
/// </summary>
/// <remarks>
/// What it decides is <c>WordsScreenTests</c>, <c>FindingCorrectionsTests</c> and
/// <c>CorrectingWordsTests</c>, which run. What none of them can see is whether the window opens
/// it, or whether it writes through the act that keeps a saved correction and the transcripts it
/// promised in step. Read out of source because opening a window is what it would otherwise take,
/// and no build agent has one.
/// </remarks>
public class WordsThatComeOutWrongTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "WordsThatComeOutWrong.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "WordsThatComeOutWrong.xaml");

    private static readonly string Window =
        Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs");

    /// <summary>Every style this screen looks up by name is one it declares.</summary>
    /// <remarks>
    /// <c>SayingWhoIsWhoTests</c>' own form and for its reason: <c>Chrome</c> is an indexer over
    /// this screen's own resources and does not walk up to the application's.
    /// </remarks>
    [Fact]
    public void Every_style_this_screen_names_is_one_it_declares()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        Regex.Matches(source, @"Chrome\((?<taking>[^)]*)\)")
            .Select(match => match.Groups["taking"].Value.Trim())
            .Where(taking => !Regex.IsMatch(taking, @"^""\w+""$") && taking != "string named")
            .ToArray()
            .ShouldBeEmpty("these lookups do not name their style as a literal, so nothing checks it.");

        var named = Regex.Matches(source, @"Chrome\(""(?<key>\w+)""\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = Regex.Matches(
                File.ReadAllText(AppSources.At(Markup).FullName),
                @"x:Key=""(?<key>\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        named.ShouldNotBeEmpty("WordsThatComeOutWrong.xaml.cs names no style, so this check reads nothing.");
        declared.ShouldNotBeEmpty("WordsThatComeOutWrong.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "WordsThatComeOutWrong.xaml declares no style by these names, so the screen throws where "
            + "it is drawn.");
    }

    /// <summary>
    /// The meeting screen asks to correct words and the window opens the screen that does — the
    /// one thing that makes this screen reachable at all.
    /// </summary>
    [Fact]
    public void The_meeting_screen_has_a_way_to_reach_it()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("Reading.CorrectWords += OnCorrectWords");
        window.ShouldContain("Corrections.Show(asked.Meeting, asked.AsWritten)");
        window.ShouldContain("Voices.CorrectWords += OnCorrectWords");
        window.ShouldContain("Corrections.Left += OnLeftTheCorrections");

        // And the window lets go of it, so a window that shut over a screen still waiting on the
        // corpus does not draw into itself afterwards.
        window.ShouldContain("Corrections.Close()");
    }

    /// <summary>
    /// Saving goes through the one act that renders every meeting the correction touches, and not
    /// through a write of this screen's own.
    /// </summary>
    /// <remarks>
    /// A screen that wrote the correction itself would promise a transcript it did not change:
    /// <c>CorrectingWords.Correct</c> is what reads which meetings the correction reaches and
    /// renders each of them again, and what leaves the rest to the next launch.
    /// </remarks>
    [Fact]
    public void Saving_goes_through_the_one_door_that_renders_what_it_touches()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        SourceLines.Occurrences(source, "CorrectingWords.Correct(").Count().ShouldBe(
            1,
            "this screen no longer saves through CorrectingWords.Correct exactly once, which is "
            + "the one door that renders the meetings a correction touches.");

        SourceLines.Occurrences(source, "HumanLayer").ShouldBeEmpty(
            "this screen names HumanLayer, which writes a correction without rendering anything.");

        SourceLines.Occurrences(source, "TerminologyCorrections.Add").ShouldBeEmpty(
            "this screen adds a correction itself, which promises a transcript nothing rendered.");
    }

    /// <summary>
    /// What was not shown is read off the exception's list of meetings, never matched out of its
    /// message: rewording the message would otherwise make the screen say no meetings.
    /// </summary>
    [Fact]
    public void What_was_not_shown_is_read_off_the_answer_and_never_off_a_message()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldNotContain("Regex");
        source.ShouldContain(".Meetings.Count");
    }

    /// <summary>
    /// The places are read off the one read both screens offer them from, and the corrections' paths off the one tree
    /// walk, and the screen walks no parent itself.
    /// </summary>
    [Fact]
    public void The_places_are_read_off_the_filing_and_never_walked_by_hand()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldNotContain("ParentId");
        source.ShouldNotContain(".Filing(");
        source.ShouldContain(".Places(");
        source.ShouldContain(".PathTo(");
    }


    /// <summary>
    /// Words brought from a selection open in the field, are pinned at the top of the forms and
    /// ticked whatever a later search returns, and are counted in what is saved.
    /// </summary>
    [Fact]
    public void Words_brought_from_a_selection_are_pinned_and_ticked()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldContain("public void Show(Guid meetingId, string? asWritten = null)");
        source.ShouldContain("TypedField.SelectAll()");

        // The search runs at once over them, and what it returns carries the pinned form first.
        Body(source, "private async Task SearchAsync()").ShouldContain("_forms = WithThePinned(found)");
        Body(source, "private async Task SearchAsync()").ShouldContain("form.Text == _pinned");

        var pinned = Body(source, "private IReadOnlyList<WrittenForm> WithThePinned(");

        pinned.ShouldContain("[theOne, .. found.Where(form => form.Text != pinned)]");

        // Counted in what is saved, because it is one of the forms: dropping it from the list
        // would drop it from the save with it.
        Body(source, "private string[] FormsToSave(").ShouldContain("_forms");

        // And forgotten with the meeting and after a save.
        Body(source, "private void Reset()").ShouldContain("_pinned = null");
    }

    /// <summary>
    /// Words that are the output of a correction already reaching the meeting are said to be
    /// corrected already and pinned nowhere: a correction keyed on them would leave the transcript
    /// as it is.
    /// </summary>
    [Fact]
    public void A_selection_already_corrected_is_said_and_not_pinned()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldContain("MeetingRenderer.CorrectionsReaching(context, meetingId)");

        var read = Body(source, "private async Task ReadAsync(");
        var said = read.IndexOf("held.AlreadyWrittenByACorrection.Contains(asked)", StringComparison.Ordinal);

        said.ShouldBeGreaterThan(-1);
        read.IndexOf("UiTexts.AlreadyCorrected", StringComparison.Ordinal).ShouldBeGreaterThan(said);

        // Only the other arm pins.
        read.IndexOf("_pinned = asked", StringComparison.Ordinal)
            .ShouldBeGreaterThan(read.IndexOf("else", said, StringComparison.Ordinal));
    }

    /// <summary>
    /// One method's body, anchored on the closing brace at its own indentation.
    /// <c>SayingWhoIsWhoTests</c>' own helper, for the reason given there.
    /// </summary>
    private static string Body(string source, string signature)
    {
        var found = Regex.Match(
            source,
            Regex.Escape(signature) + @".*??
[ ]{4}\}",
            RegexOptions.Singleline);

        found.Success.ShouldBeTrue($"the file no longer has a `{signature}`.");
        return found.Value;
    }
}
