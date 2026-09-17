namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The settings screen, held to the two things no rule of its own can reach: that every answer a
/// recording can be given when it ends is on it, and that the one control anywhere that reaches
/// this screen really reaches it.
/// </summary>
/// <remarks>
/// What the screen decides is <c>CorpusSettings</c> and <c>WhatStoppingStarts</c>, which run. What
/// neither of them can see is whether the window offers the answer they are about — an option
/// missing from the screen is a preference nobody can set, and one substituting for another is an
/// application that spends money it was not asked to.
/// <para>
/// How the table is read out of source, and why it has to be, is <see cref="EnumTable"/>'s. The
/// line about where the corpus is has a class of its own, <see cref="CorpusTextTests"/>, because it
/// is over a different enum and came here from another screen.
/// </para>
/// </remarks>
public class ConfiguracionTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "Configuracion.xaml.cs");

    /// <summary>
    /// Every answer a recording can be given when it ends is one of the options on this screen.
    /// </summary>
    /// <remarks>
    /// The table is <c>Offers</c>, which the screen reads both ways — it is what ticks an option
    /// when the corpus is read and what a press is turned back into — so a member with no option
    /// and an arm left behind by a rename are both found here. The first is the one that matters: a
    /// preference nobody can reach is one settled by whatever the corpus happens to hold, and two
    /// of the three answers spend the user's own credit.
    /// </remarks>
    [Fact]
    public void Every_answer_a_recording_can_be_given_when_it_ends_is_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "answer",
                "AfterARecording",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "AfterARecording.cs"))
            .ShouldNameItsWholeEnum("AfterARecording");

    /// <summary>
    /// The gear opens the settings, the way back closes them, and what is chosen on them goes where
    /// it is answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing that makes this screen reachable at all, and the one thing no other check here
    /// covers: the table above would pass over a screen nothing can open. It is the fourth member of
    /// the family <c>ReadingAMeetingTests.The_list_says_a_meeting_was_chosen_and_the_window_opens_it</c>
    /// started, written for the same reason and read out of source because opening a window is what
    /// it would otherwise take, and no build agent has one.
    /// </para>
    /// <para>
    /// The language is here rather than in a second check of the same shape, because it is the same
    /// fact: the picker moved onto this screen off the front door and the answer did not move with
    /// it. <c>App</c> subscribes to the window's <c>LanguageChosen</c> and is what writes the choice
    /// down, so a screen that handled it itself would leave the application opening in Windows'
    /// language for ever after — and the wire that stops it runs through the same three files as
    /// the wire above.
    /// </para>
    /// <para>
    /// This card adds the only control anywhere in the application that reaches a fourth sub-screen,
    /// and the screen's own probe needs a packaged build on a logged-in desktop — so without this,
    /// nothing a build agent runs says the screen is reachable.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_gear_opens_the_settings_screen_the_way_back_closes_it_and_the_language_goes_up()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml")).FullName);

        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        markup.ShouldContain("Click=\"OnOpenSettings\"");
        window.ShouldContain("Settings.Show()");

        // The corpus is handed over once, the way every other sub-screen is given one: without it
        // the screen throws the moment it is drawn, because everything on it is an answer about
        // this install.
        window.ShouldContain("Settings.Open(corpus)");

        // And the way back, which is the other half of a sub-screen: it is reached from one place
        // and returns to it. The handler by name and not `Settings.Close()`, which the window also
        // calls on the way out — an assertion satisfied by that copy would say nothing about
        // whether the way back closes anything.
        window.ShouldContain("Settings.Left += OnLeftTheSettings");
        window.ShouldContain("private void OnLeftTheSettings");

        // The language is raised on and never answered here.
        screen.ShouldContain("LanguageChosen?.Invoke(this, chosen)");
        window.ShouldContain("Settings.LanguageChosen += OnLanguageChosenInTheSettings");
        window.ShouldContain("LanguageChosen?.Invoke(this, language)");
    }
}
