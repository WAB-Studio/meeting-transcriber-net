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
        // this install. The window id goes with it, which is what #148's folder picker needs and a
        // UserControl has none of its own.
        window.ShouldContain("Settings.Open(corpus, AppWindow.Id)");

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

    /// <summary>
    /// A folder chosen on the settings screen reaches the application, through the window, the
    /// same three files the language wire above runs through.
    /// </summary>
    [Fact]
    public void A_folder_chosen_on_the_settings_screen_reaches_the_application()
    {
        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        var app = File.ReadAllText(AppSources.At(Path.Combine("MeetingTranscriber.App", "App.xaml.cs")).FullName);

        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("CorpusChosen?.Invoke(this, EventArgs.Empty)");
        window.ShouldContain("Settings.CorpusChosen += OnCorpusChosenInTheSettings");
        window.ShouldContain("CorpusChosen?.Invoke(this, EventArgs.Empty)");
        app.ShouldContain("window.CorpusChosen += OnCorpusChosen");
    }

    /// <summary>
    /// The press that changes the corpus folder is on the screen and says what the catalogue says.
    /// </summary>
    [Fact]
    public void The_press_that_changes_the_corpus_folder_is_on_the_screen_and_says_what_the_catalogue_says()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);

        markup.ShouldContain("x:Name=\"ChangeWhereItIsKept\"");
        markup.ShouldContain("Click=\"OnChangeWhereItIsKept\"");
        markup.ShouldContain("In(loc:UiTexts.ChangeWhereItIsKept)");
    }

    /// <summary>
    /// The press that changes the corpus folder is drawn only when the corpus was refused, which
    /// is the one state it exists to answer.
    /// </summary>
    [Fact]
    public void The_press_that_changes_the_corpus_folder_is_drawn_only_when_the_corpus_was_refused()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("ChangeWhereItIsKept.Visibility = Corpus().Refusal is null");
    }

    /// <summary>
    /// The folder picker this screen opens is the Windows App SDK one, which does not need a window
    /// handle the older <c>Windows.Storage.Pickers.FolderPicker</c> would.
    /// </summary>
    [Fact]
    public void The_folder_picker_is_the_one_that_does_not_need_a_window_handle()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("using Microsoft.Windows.Storage.Pickers;");

        // The naive substring check is red over a correct build: "Windows.Storage.Pickers" is
        // itself a substring of "Microsoft.Windows.Storage.Pickers". What is checked instead is the
        // `using` that would bring the older picker in, which this screen must never carry.
        screen.ShouldNotContain("using Windows.Storage.Pickers;");
    }

    /// <summary>
    /// Nothing escapes the press that opens a folder picker, because the handler is <c>async void</c>
    /// and anything that leaves one is the application going away.
    /// </summary>
    /// <remarks>
    /// The picker itself is a call into Windows and not this application's own write, so it is
    /// guarded by a bare <c>catch</c> of its own rather than by widening
    /// <c>ScreenFailures.Reportable</c> — that list has one owner, and a picker's refusal is not a
    /// thing every screen should report. Writing the chosen folder into the setting is this
    /// application's own file write and is caught separately, through that same list unwidened,
    /// which the assertion below does not have to see to know the picker's own catch is not built
    /// by widening it: it names the filter written beside it instead.
    /// </remarks>
    [Fact]
    public void Nothing_escapes_the_press_that_opens_a_folder_picker()
    {
        var handler = Handler("private async void OnChangeWhereItIsKept(");

        handler.ShouldContain(
            "catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)");
    }

    /// <summary>
    /// The handler's own body, from its signature to the closing brace that balances it, so a
    /// negative assertion over it says nothing about the rest of the screen.
    /// </summary>
    private static string Handler(string signature)
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        var start = screen.IndexOf(signature, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{Screen} no longer has '{signature}'.");

        var opens = screen.IndexOf('{', start);
        var depth = 0;
        for (var at = opens; at < screen.Length; at++)
        {
            if (screen[at] == '{')
            {
                depth++;
            }
            else if (screen[at] == '}' && --depth == 0)
            {
                return screen[start..(at + 1)];
            }
        }

        throw new InvalidOperationException($"'{signature}' in {Screen} never closes.");
    }
}
