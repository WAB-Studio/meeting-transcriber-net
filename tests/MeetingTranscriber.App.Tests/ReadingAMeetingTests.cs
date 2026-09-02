using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The screen one meeting is read from, held to the two things no rule of its own can reach: that
/// it has a heading for every section a meeting can have things in, and that every style it names
/// at the moment it draws is one its own markup declares.
/// </summary>
/// <remarks>
/// What the screen decides is `MeetingScreenTests` and `MeetingReadingTests`, which run. What
/// neither of them can see is whether the window has a word for what they answer — and a section
/// with no heading is a thing drawn under another section's title, which puts an open question in
/// the list of things that were settled.
/// <para>
/// How the tables are read out of source, and why they have to be, is <see cref="EnumTable"/>'s.
/// </para>
/// </remarks>
public class ReadingAMeetingTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml");

    [Fact]
    public void Every_section_a_meeting_can_have_things_in_has_a_heading_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "kind",
                "LeftKind",
                Path.Combine("MeetingTranscriber.Domain", "Knowledge", "WhatTheAiLeft.cs"))
            .ShouldNameItsWholeEnum("ReadingAMeeting", "LeftKind");

    /// <summary>
    /// Every state a meeting's recording can be in has a sentence where the player would be.
    /// </summary>
    /// <remarks>
    /// The two that are not playable are a meeting with nothing recorded under it and a meeting
    /// whose recording the corpus records and cannot find, and they are not the same news: the
    /// second is a source gone, and a screen with no word for it would show it as the first.
    /// </remarks>
    [Fact]
    public void Every_state_a_recording_can_be_in_has_a_sentence_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "recording",
                "RecordedAudio",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "MeetingScreen.cs"))
            .ShouldNameItsWholeEnum("ReadingAMeeting", "RecordedAudio");

    /// <summary>
    /// Every style this screen looks up by name is one it declares.
    /// </summary>
    /// <remarks>
    /// The half no compiler reaches: a key is a string handed to a resource lookup at the moment a
    /// card is drawn, so one renamed in the markup and not here throws out of a draw — on a screen
    /// nothing but a running window builds. So it is read out of the two files instead, the same
    /// way <c>MeetingCardTextTests</c> reads the list's.
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

        named.ShouldNotBeEmpty("ReadingAMeeting.xaml.cs names no style, so this check reads nothing.");
        declared.ShouldNotBeEmpty("ReadingAMeeting.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "ReadingAMeeting.xaml declares no style by these names, so the screen throws where it "
            + "is drawn.");
    }

    /// <summary>
    /// The list's press and the screen it opens are wired to each other.
    /// </summary>
    /// <remarks>
    /// The one thing that makes a meeting reachable at all, and the one thing no other check here
    /// covers: the tables above would all pass over a screen nothing can open. Read out of source
    /// because opening a window is what it would otherwise take, and no build agent has one.
    /// </remarks>
    [Fact]
    public void The_list_says_a_meeting_was_chosen_and_the_window_opens_it()
    {
        var list = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MeetingsDrawer.xaml.cs")).FullName);

        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        list.ShouldContain("MeetingChosen?.Invoke");
        window.ShouldContain("Meetings.MeetingChosen += OnMeetingChosen");
        window.ShouldContain("Reading.Show(meeting)");

        // And the way back, which is the other half of a sub-screen: it is reached from one place
        // and returns to it.
        window.ShouldContain("Reading.Left += OnLeftTheMeeting");

        // The player and the file it holds are let go of when the window closes. A window that
        // shut over one leaves the recording coming out of the machine.
        window.ShouldContain("Reading.Close()");
    }
}
