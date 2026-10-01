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
        window.ShouldContain("Corrections.Show(meeting)");
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
}
