using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The screen a node's story is read from, held to the two things no rule of its own can reach:
/// that it has a bullet for every section a statement can be in, and that every style it names at
/// the moment it draws is one its own markup declares.
/// </summary>
/// <remarks>
/// The same two checks <see cref="ReadingAMeetingTests"/> holds its screen to, and for the same
/// reason — <see cref="EnumTable"/>'s own remarks say why a build agent has to read source for
/// both.
/// </remarks>
public class ReadingANodeTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "ReadingANode.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "ReadingANode.xaml");

    [Fact]
    public void Every_section_a_statement_can_be_in_has_a_bullet_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "kind",
                "LeftKind",
                Path.Combine("MeetingTranscriber.Domain", "Knowledge", "WhatTheAiLeft.cs"))
            .ShouldNameItsWholeEnum("LeftKind");

    /// <summary>
    /// Every style this screen looks up by name is one it declares.
    /// </summary>
    /// <remarks>
    /// <c>Chrome</c> reads this screen's own dictionary and never Olivo's directly — a screen
    /// naming Olivo's own key by its own name would throw the moment it drew, which is why the
    /// path pills go through <c>LastNode</c>/<c>PathNode</c> rather than through <c>Pill</c>/
    /// <c>PillButton</c> by name.
    /// </remarks>
    [Fact]
    public void Every_style_this_screen_names_is_one_it_declares()
    {
        var named = Regex.Matches(
                File.ReadAllText(AppSources.At(Screen).FullName),
                @"Chrome\(""(?<key>\w+)""\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = Regex.Matches(
                File.ReadAllText(AppSources.At(Markup).FullName),
                @"x:Key=""(?<key>\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        named.ShouldNotBeEmpty("ReadingANode.xaml.cs names no style, so this check reads nothing.");
        declared.ShouldNotBeEmpty("ReadingANode.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "ReadingANode.xaml declares no style by these names, so the screen throws where it is "
            + "drawn.");
    }

    /// <summary>
    /// The filing chip's press and the screen it opens are wired to each other, there and back.
    /// </summary>
    /// <remarks>
    /// The one thing that makes a node's story reachable at all, and the one thing no other check
    /// here covers: the tables above would all pass over a screen nothing can open. Read out of
    /// source because opening a window is what it would otherwise take, and no build agent has
    /// one.
    /// </remarks>
    [Fact]
    public void The_filing_chip_says_a_node_was_chosen_and_the_window_opens_its_story()
    {
        var chip = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs")).FullName);

        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        chip.ShouldContain("NodeChosen?.Invoke");
        window.ShouldContain("Reading.NodeChosen += OnNodeChosen");
        window.ShouldContain("NodeStory.Show(node)");

        // And the two ways back: one to the meeting the story was opened from, and one into a
        // meeting the story itself lists.
        window.ShouldContain("NodeStory.Left += OnLeftTheNode");
        window.ShouldContain("NodeStory.Close()");
        window.ShouldContain("NodeStory.MeetingChosen += OnMeetingChosenFromANode");
    }
}
