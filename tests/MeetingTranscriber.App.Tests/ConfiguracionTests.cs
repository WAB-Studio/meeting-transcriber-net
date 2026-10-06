using System.Text.RegularExpressions;

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
    /// The word at the foot opens the settings, the way back closes them, and what is chosen on them goes where
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
    public void The_word_at_the_foot_opens_the_settings_screen_the_way_back_closes_it_and_the_language_goes_up()
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
        markup.ShouldContain("In(loc:UiTexts.Change)");
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
    /// Where Claude Code is is on the settings screen, with the press that changes it and the two
    /// data points that read it back.
    /// </summary>
    [Fact]
    public void Where_Claude_Code_is_is_on_the_settings_screen_with_the_press_that_changes_it()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        markup.ShouldContain("x:Name=\"ClaudeCodeText\"");
        markup.ShouldContain("x:Name=\"ClaudeCodeStatusText\"");
        markup.ShouldContain("x:Name=\"ChangeWhereClaudeCodeIs\"");
        markup.ShouldContain("Click=\"OnChangeWhereClaudeCodeIs\"");

        screen.ShouldContain("SummarisingOnThisMachine.WhereClaudeCodeIs()");
        screen.ShouldContain("SummarisingOnThisMachine.Provider().IsAvailableAsync(");
    }

    /// <summary>
    /// Nothing escapes the press that opens a file picker, for the same reason and the same shape
    /// <see cref="Nothing_escapes_the_press_that_opens_a_folder_picker"/> already holds the folder
    /// one to.
    /// </summary>
    [Fact]
    public void Nothing_escapes_the_press_that_opens_a_file_picker()
    {
        var handler = Handler("private async void OnChangeWhereClaudeCodeIs(");

        handler.ShouldContain(
            "catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)");
    }

    /// <summary>
    /// Each press that says <em>Cambiar</em> is named for what it changes, for a screen reader —
    /// D2. Two presses on this screen show the same word, and telling them apart is
    /// <c>AutomationProperties.Name</c>'s alone to do.
    /// </summary>
    [Fact]
    public void Each_press_that_says_Cambiar_is_named_for_what_it_changes()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);

        markup.ShouldContain(
            "AutomationProperties.Name=\"{x:Bind In(loc:UiTexts.ChangeWhereTheCorpusIsKept)}\"");
        markup.ShouldContain(
            "AutomationProperties.Name=\"{x:Bind In(loc:UiTexts.ChangeWhereClaudeCodeIs)}\"");
    }

    /// <summary>
    /// Every kind an export can carry has a tick on this screen, which is what makes it a thing a
    /// person can choose to take or leave.
    /// </summary>
    /// <remarks>
    /// The table is <c>Ticks</c>, which the screen reads both ways: what is drawn and what is
    /// exported come through it. A kind with no tick is one nobody can untick, so it would go in
    /// every export — and the one this is about is audio.
    /// </remarks>
    [Fact]
    public void Every_kind_an_export_can_carry_has_a_tick_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "kind",
                "ExportKind",
                Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "ExportKind.cs"))
            .ShouldNameItsWholeEnum("ExportKind");

    /// <summary>
    /// Every kind an export can carry is named in the line about the last one, so the line cannot
    /// say an export took less than it did.
    /// </summary>
    [Fact]
    public void Every_kind_an_export_can_carry_is_named_in_the_last_export_line() =>
        EnumTable.Read(
                Screen,
                "exported",
                "ExportKind",
                Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "ExportKind.cs"))
            .ShouldNameItsWholeEnum("ExportKind");

    /// <summary>
    /// The export block is on the settings screen and says what the catalogue says, and the press
    /// reaches the export.
    /// </summary>
    /// <remarks>
    /// A source fact and not a walk: the press opens the Windows folder picker, a system dialog
    /// the probe has not been shown to drive, so what is held here is that the block is wired and
    /// not that a person has seen it work.
    /// </remarks>
    [Fact]
    public void The_export_block_is_on_the_settings_screen_and_says_what_the_catalogue_says()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        foreach (var tick in new[] { "ExportAudio", "ExportTranscripts", "ExportSummaries", "ExportHandCorrections" })
        {
            markup.ShouldContain($"x:Name=\"{tick}\"");
        }

        markup.ShouldContain("Style=\"{StaticResource Tick}\"");
        markup.ShouldContain("Click=\"OnExport\"");
        markup.ShouldContain("In(loc:UiTexts.Export)");

        // The press is a glyph: its name and its tooltip are set where it is drawn, and say what it
        // exports and what an export is for.
        markup.ShouldContain("Style=\"{StaticResource TheExportGlyph}\"");
        Handler("private void ShowTheExport(").ShouldContain("UiTexts.ExportTheCorpusToAFolder");
        Handler("private void ShowTheExport(").ShouldContain("UiTexts.WhatAnExportIsFor");

        screen.ShouldContain("CorpusExport.Into(");

        // The line about the last export: drawn from a read of the corpus, and read whenever the
        // screen is shown, so what it says is what the corpus says and not what it said last time.
        markup.ShouldContain("x:Name=\"LastExportText\"");
        screen.ShouldContain("new CorpusSettings(context).LastExportMade()");
        screen.ShouldContain("UiTexts.LastExport.In(");
        Handler("public async void Show(").ShouldContain("ReadTheLastExport();");
    }

    /// <summary>
    /// Nothing escapes the press that exports: the folder picker is a call into Windows and is
    /// caught bare, as the two pickers above are.
    /// </summary>
    [Fact]
    public void Nothing_escapes_the_press_that_exports()
    {
        var handler = Handler("private async void OnExport(");

        handler.ShouldContain(
            "catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)");
    }

    /// <summary>
    /// The Deepgram key is on the settings screen — whether one is kept, a field to paste one, the
    /// press that keeps it and the press that takes it away — and every one of them reaches the
    /// store through the one file of the application that cannot read a key back.
    /// </summary>
    /// <remarks>
    /// Read out of source for the reason every check in this class is: the screen's own probe needs
    /// a packaged build on a desktop. What it holds is the wiring a build agent would otherwise
    /// never see — a press that called nothing, or a screen that reached the store some other way.
    /// </remarks>
    [Fact]
    public void The_Deepgram_key_is_kept_and_taken_away_from_the_settings_screen()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);

        markup.ShouldContain("x:Name=\"DeepgramKeyText\"");
        markup.ShouldContain("Click=\"OnKeepTheKey\"");
        markup.ShouldContain("Click=\"OnRemoveTheKey\"");
        markup.ShouldContain(
            "AutomationProperties.Name=\"{x:Bind In(loc:UiTexts.SaveTheDeepgramKey)}\"");

        Handler("public async void Show(").ShouldContain("_aKeyIsKept = WhetherAKeyIsKept();");
        Handler("private static bool? WhetherAKeyIsKept(").ShouldContain("KeepingThisMachinesKey.IsThere");
        Handler("private void OnKeepTheKey(")
            .ShouldContain("KeepingThisMachinesKey.Keep(DeepgramKeyBox.Password);");
        Handler("private void OnRemoveTheKey(").ShouldContain("KeepingThisMachinesKey.Forget();");
    }

    /// <summary>
    /// A key is never on screen as characters: the field is a secret field whose reveal is off, and
    /// it is emptied after every press, kept or refused, and when the screen closes.
    /// </summary>
    /// <remarks>
    /// The reveal lives in the style, so the style is what is read — a screen that took the field
    /// off <c>TypedSecret</c> is caught by the second assertion.
    /// </remarks>
    [Fact]
    public void A_pasted_key_is_never_shown_and_does_not_stay_in_the_field()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);
        var olivo = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Olivo.xaml")).FullName);

        var field = markup[markup.IndexOf("x:Name=\"DeepgramKeyBox\"", StringComparison.Ordinal)..];
        markup[..markup.IndexOf("x:Name=\"DeepgramKeyBox\"", StringComparison.Ordinal)]
            .TrimEnd().ShouldEndWith("<PasswordBox");
        field[..field.IndexOf("/>", StringComparison.Ordinal)]
            .ShouldContain("Style=\"{StaticResource TypedSecret}\"");
        olivo.ShouldContain("<Setter Property=\"PasswordRevealMode\" Value=\"Hidden\" />");

        var keeping = Handler("private void OnKeepTheKey(");
        var finallyBlock = keeping[keeping.IndexOf("finally", StringComparison.Ordinal)..];
        finallyBlock[..finallyBlock.IndexOf('}')].ShouldContain("DeepgramKeyBox.Password = string.Empty;");
        Handler("public void Close(").ShouldContain("DeepgramKeyBox.Password = string.Empty;");
    }

    /// <summary>
    /// A paste the machine will not keep is a sentence and not a crash: the press catches the key's
    /// own refusal and says it by its kind, never by its English message.
    /// </summary>
    [Fact]
    public void A_key_this_machine_will_not_keep_is_said_rather_than_thrown()
    {
        var keeping = Handler("private void OnKeepTheKey(");

        keeping.ShouldContain("catch (DeepgramKeyException refused)");
        keeping.ShouldContain("Refused(refused.Refusal)");
        keeping.ShouldNotContain(".Message");

        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        screen.ShouldContain("DeepgramKeyRefusal.NothingInIt => UiTexts.ThatIsNotADeepgramKey,");
        screen.ShouldContain("DeepgramKeyRefusal.NotKept => UiTexts.ThisMachineWouldNotKeepTheKey,");

        // Asking and taking away are refusals too, and neither may reach the dispatcher.
        Handler("private static bool? WhetherAKeyIsKept(").ShouldContain("catch (DeepgramKeyException)");
        Handler("private void OnRemoveTheKey(").ShouldContain("catch (DeepgramKeyException)");
    }

    /// <summary>
    /// The screen opens itself the first time: when the corpus is reachable and nobody has said who
    /// is using the application, which is the rule <c>WhoIsUsingThisRow</c> already carries.
    /// </summary>
    /// <remarks>
    /// Read out of source, since opening a window is what it would otherwise take. What is held is
    /// that <c>Open</c> reads the row the way the window's own read does, inside a catch for what a
    /// corpus that will not open throws — it runs in the window's constructor, where nothing may
    /// escape — and that it is the row that decides.
    /// </remarks>
    [Fact]
    public void Settings_opens_itself_the_first_time()
    {
        var open = Handler("public void Open(");

        open.ShouldContain("ReadWhoIsUsingThis();");
        open.ShouldContain("WhoIsUsingThis().IsAsking");
        open.ShouldNotContain("CheckClaudeCodeAsync");

        var read = Handler("private void ReadWhoIsUsingThis(");
        read.ShouldContain("new HumanLayer(context, TimeProvider.System).Me()");
        read.ShouldContain("catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))");
        read.ShouldContain("Say(UiTexts.WhoIsUsingThisCouldNotBeRead);");

        // The press at the foot always opens the whole of it.
        Handler("public async void Show(").ShouldContain("_firstStep = false;");

        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);
        markup.ShouldContain("x:Name=\"StartButton\"");
        markup.ShouldContain("In(loc:UiTexts.Start)");
        Handler("private void Arrange(").ShouldContain("UiTexts.GetStarted");
    }

    /// <summary>
    /// Choosing what happens after a recording never greys the choice: a write in flight is waited
    /// for and not refused.
    /// </summary>
    [Fact]
    public void Choosing_after_recording_never_disables_the_choice()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldNotContain("_writingTheAnswer");
        Handler("private void ShowWhatHappensWhenARecordingEnds(")
            .ShouldContain("AfterARecordingOptions.IsEnabled = live;");
    }

    /// <summary>
    /// Everything the screen writes to the corpus goes through the one line of writes, so two of
    /// them are never two migrations of a folder with no schema racing for one write lock.
    /// </summary>
    /// <remarks>
    /// The five writes are the name, what happens after a recording, the summary model, the
    /// summary effort and an export. The count of <c>OpenMigrated</c> is the other half: a sixth
    /// write added beside them that skipped the line would be found here and not by somebody's
    /// first install. The line itself is <c>WritesInTurn</c>'s, which has facts of its own.
    /// </remarks>
    [Fact]
    public void The_screen_writes_one_thing_at_a_time()
    {
        foreach (var handler in new[]
        {
            "private async void OnKeepWhoIsUsingThis(",
            "private async void OnAfterARecordingChosen(",
            "private async void OnSummaryModelChosen(",
            "private async void OnSummaryEffortChosen(",
            "private async void OnExport(",
        })
        {
            Handler(handler).ShouldContain("_writes.Run(", customMessage: handler);
        }

        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        screen.ShouldContain("private readonly WritesInTurn _writes = new();");
        screen.ShouldNotContain("OneWriteAtATime(");
        Regex.Matches(screen, Regex.Escape("CorpusDatabase.OpenMigrated(")).Count
            .ShouldBe(5, "a write to the corpus that does not go through the line of writes");
    }

    /// <summary>
    /// Claude Code is tested on a press, and the press asks the question opening the screen asks.
    /// </summary>
    [Fact]
    public void Claude_Code_is_tested_on_a_press()
    {
        var markup = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);

        markup.ShouldContain("x:Name=\"TestClaudeCodeButton\"");
        markup.ShouldContain("Click=\"OnTestClaudeCode\"");

        Handler("private async void OnTestClaudeCode(").ShouldContain("CheckClaudeCodeAsync(");
        Handler("private void ShowWhereClaudeCodeIs(").ShouldContain("UiTexts.ClaudeCodeAnswers");
    }

    /// <summary>
    /// Every model a summary can be asked of has a name on this screen, which is what makes it a
    /// thing a person can choose.
    /// </summary>
    [Fact]
    public void Every_summary_model_has_a_name_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "model",
                "SummaryModel",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "SummaryModel.cs"))
            .ShouldNameItsWholeEnum("SummaryModel");

    /// <summary>
    /// Every effort a summary can be asked with has a name on this screen, for the reason a model
    /// has.
    /// </summary>
    [Fact]
    public void Every_summary_effort_has_a_name_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "effort",
                "SummaryEffort",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "SummaryEffort.cs"))
            .ShouldNameItsWholeEnum("SummaryEffort");

    /// <summary>
    /// Every theme the application can be drawn in has a name on this screen, which is what makes
    /// it a thing a person can choose.
    /// </summary>
    [Fact]
    public void Every_theme_has_a_name_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "theme",
                "AppTheme",
                Path.Combine("MeetingTranscriber.Presentation", "ThemeChoice.cs"))
            .ShouldNameItsWholeEnum("AppTheme");

    /// <summary>
    /// The row about who is using the application holds the name and its press and nothing else:
    /// the language and the theme are on a card of their own, so a press beside a field is the
    /// field's height and the row is not two questions.
    /// </summary>
    [Fact]
    public void The_name_row_holds_the_name_and_its_save_only()
    {
        var markup = Markup();
        var name = markup.IndexOf("x:Name=\"WhoIsUsingThisBox\"", StringComparison.Ordinal);
        var card = markup.IndexOf("x:Name=\"AppCard\"", StringComparison.Ordinal);
        var language = markup.IndexOf("x:Name=\"LanguagePicker\"", StringComparison.Ordinal);
        var theme = markup.IndexOf("x:Name=\"ThemePicker\"", StringComparison.Ordinal);

        name.ShouldBeGreaterThan(-1);
        card.ShouldBeGreaterThan(name);
        language.ShouldBeGreaterThan(card, "the language picker is back on the name's card");
        theme.ShouldBeGreaterThan(card);

        markup[name..card].ShouldContain("Style=\"{StaticResource ButtonBesideAField}\"");
        markup[name..card].ShouldNotContain("<ComboBox");
        markup.ShouldContain("x:Key=\"ButtonBesideAField\"");
        markup.ShouldContain("<Setter Property=\"Height\" Value=\"{StaticResource ControlHeight}\" />");
    }

    /// <summary>
    /// The first step shows the two engine cards and what the second is paid with, so the answer
    /// about what happens after a recording is never given without the engines in view.
    /// </summary>
    [Fact]
    public void The_first_step_shows_the_engines()
    {
        Handler("private void Arrange(").ShouldNotContain("EnginesRow");
        Handler("public void Open(").ShouldContain("Arrange();");

        var markup = Markup();
        markup.ShouldContain("x:Name=\"EnginesRow\"");
        markup.ShouldContain("In(loc:UiTexts.OnYourClaudePlan)");
        markup.IndexOf("In(loc:UiTexts.OnYourClaudePlan)", StringComparison.Ordinal)
            .ShouldBeLessThan(markup.IndexOf("x:Name=\"SummaryModelPicker\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// The export press says what an export is for on hover, and the key's removal is drawn as a
    /// press with a rule rather than as a word.
    /// </summary>
    [Fact]
    public void Export_says_what_it_does_on_hover()
    {
        var markup = Markup();

        markup.ShouldContain("x:Key=\"TheExportGlyph\"");
        markup.ShouldContain("BasedOn=\"{StaticResource IconButton}\"");
        Handler("private void ShowTheExport(")
            .ShouldContain("ToolTipService.SetToolTip(ExportButton, In(UiTexts.WhatAnExportIsFor))");

        markup.ShouldContain("Style=\"{StaticResource TheKeysRemovePress}\"");
        markup.ShouldContain("<Setter Property=\"BorderBrush\" Value=\"{ThemeResource EmptyControlRingBrush}\" />");
    }

    /// <summary>
    /// A pick of the theme is applied to this window, written down, and never taken for a pick when
    /// the picker was only being filled.
    /// </summary>
    [Fact]
    public void A_theme_pick_is_applied_written_and_not_a_pick_when_the_picker_is_filled()
    {
        var pick = Handler("private void OnThemeChosen(");

        pick.ShouldContain("if (_filling");
        pick.ShouldContain("chosen == _theme");
        pick.ShouldContain("ShownInTheme.Apply(");
        pick.ShouldContain("ThemeChoice.OfThisUser().Write(chosen)");
    }

    private static string Markup() => File.ReadAllText(
        AppSources.At(Path.Combine("MeetingTranscriber.App", "Configuracion.xaml")).FullName);

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
