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
            .ShouldNameItsWholeEnum("LeftKind");

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
            .ShouldNameItsWholeEnum("RecordedAudio");

    [Fact]
    public void Every_condition_a_summary_can_be_refused_for_has_a_sentence_on_this_screen() =>
        EnumTable.Read(
                Screen,
                "condition",
                "ExtractionCondition",
                Path.Combine("MeetingTranscriber.Domain", "Knowledge", "ExtractionCondition.cs"))
            .ShouldNameItsWholeEnum("ExtractionCondition");

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
    /// The stop press, drawn only over a summary that is running, calls the one method that stops
    /// it — read out of source for the reason above: nothing here can draw the screen and press it.
    /// </summary>
    [Fact]
    public void A_running_summary_is_stopped_through_the_one_call_that_stops_it()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("screen.TheSummaryMayBeStopped");
        screen.ShouldContain(".StopTheSummary(meeting)");
    }

    /// <summary>
    /// Another summary of a summarised meeting is asked for through the one call that queues it,
    /// read out of source for the reason above.
    /// </summary>
    [Fact]
    public void Another_summary_is_asked_for_through_the_one_call_that_queues_it()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("screen.TheSummaryMayBeAskedForAgain");
        screen.ShouldContain(".SummariseAgain(meeting)");
        screen.ShouldContain("UiTexts.SummariseAgain");
    }

    /// <summary>
    /// A summary is put back through the one call that records it, from a row's <c>Checked</c>,
    /// read out of source for the reason above.
    /// </summary>
    [Fact]
    public void A_summary_is_put_back_through_the_one_call_that_records_it()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        screen.ShouldContain("screen.ASummaryMayBeChosen");
        screen.ShouldContain(".ShowSummary(meeting, given.RunId)");
        markup.ShouldContain("UiTexts.ThisMeetingsSummaries");

        var section = Body(screen, "private void SummariesSection(MeetingScreen screen)");

        section.ShouldContain(".Checked +=");
        section.ShouldNotContain(".Click +=");
    }

    /// <summary>
    /// A summary row being drawn never writes, and nothing says so but the order it is built in:
    /// <c>IsChecked</c> is set in the initialiser, before <c>Checked +=</c> is subscribed, and the
    /// one row drawn checked is the one shown, which <c>ShowSummary</c> refuses on
    /// <c>given.IsShown</c>.
    /// </summary>
    /// <remarks>
    /// The flag that used to guard the drawing is gone for that reason (O-20261001-31): a field
    /// guarding against an event nothing raises is a remark that gives the wrong reason.
    /// </remarks>
    [Fact]
    public void A_drawn_row_never_writes()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldNotContain("_drawingTheSummaries");

        var section = Body(screen, "private void SummariesSection(MeetingScreen screen)");

        section.IndexOf("IsChecked = given.IsShown", StringComparison.Ordinal)
            .ShouldBeGreaterThan(-1, "a row is no longer drawn checked from the summary that is shown.");

        section.IndexOf("IsChecked = given.IsShown", StringComparison.Ordinal)
            .ShouldBeLessThan(
                section.IndexOf(".Checked +=", StringComparison.Ordinal),
                "the row is checked after its handler is subscribed, so drawing it would write.");

        Body(screen, "private void ShowSummary(GivenSummary given)").ShouldContain("given.IsShown");
    }

    /// <summary>
    /// After a put-back redraws the rows, the keyboard goes back to the one that is checked, and not
    /// to wherever the framework leaves it (O-20261001-32).
    /// </summary>
    [Fact]
    public void A_put_back_summary_hands_the_keyboard_back_to_its_row()
    {
        var put = Body(
            File.ReadAllText(AppSources.At(Screen).FullName),
            "private void ShowSummary(GivenSummary given)");

        put.ShouldContain("AfterWriting();");
        put.ShouldContain(".Focus(FocusState.Keyboard)");

        put.IndexOf("AfterWriting();", StringComparison.Ordinal)
            .ShouldBeLessThan(
                put.IndexOf(".Focus(FocusState.Keyboard)", StringComparison.Ordinal),
                "focus is handed back before the redraw that throws the row away.");
    }

    /// <summary>
    /// The transcript is every turn as the rendered files say it, through the render's own
    /// <c>MeetingRenderer.AsRead</c>, so this screen and <c>transcript.md</c> carry the same words.
    /// </summary>
    [Fact]
    public void The_transcript_is_read_through_the_render_s_own_words()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("MeetingRenderer.AsRead(context, meetingId)");
        screen.ShouldNotContain("EveryTurn");

        // The turns unfolded under a citation are the same corrected set, not a second read of the
        // stored words.
        var around = Body(screen, "private IReadOnlyList<UIElement> TheTranscriptAround(LeftThing thing)");

        around.ShouldContain("_turns");
        around.ShouldNotContain("CorpusDatabase");
    }

    /// <summary>
    /// The transcript is a repeater that draws the lines in view and never a panel holding an
    /// element per turn, so a long meeting costs no more than a short one.
    /// </summary>
    [Fact]
    public void The_transcript_is_virtualised()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        markup.ShouldContain("<ItemsRepeater x:Name=\"TheTranscriptLines\"");
        screen.ShouldContain("TheTranscriptLines.ItemTemplate = new LineFactory(");

        // Handed over as data: no line is added to a panel here, whatever the number of turns.
        var section = Body(screen, "private void TheTranscriptSection()");

        section.ShouldContain("TheTranscriptLines.ItemsSource");
        section.ShouldNotContain(".Children.Add(");
    }

    /// <summary>The summary's longer account is drawn under its abstract.</summary>
    [Fact]
    public void The_summary_s_body_is_drawn()
    {
        var left = Body(
            File.ReadAllText(AppSources.At(Screen).FullName),
            "private void WhatWasLeft(WhatTheAiLeft left)");

        left.ShouldContain("left.Body");
        left.ShouldContain("left.Abstract");
    }

    /// <summary>
    /// A meeting with work under way asks what it is doing every couple of seconds, off the UI
    /// thread, and draws again only when the status changed — without touching the player.
    /// </summary>
    [Fact]
    public void A_screen_with_work_under_way_watches_for_it_to_land()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("KeepAskingWhileWorkIsUnderWay(read.Screen.WorkIsUnderWay)");
        screen.ShouldContain("TimeSpan.FromSeconds(2)");

        var asking = Body(screen, "private async Task AskTheWorkAsync()");

        asking.ShouldContain("Task.Run(");
        asking.ShouldContain(".On(meeting)");
        asking.ShouldContain("(owed.Stage, owed.Status) != (read.Screen.Owed.Stage, read.Screen.Owed.Status)");
        asking.ShouldContain("Draw(theRecordingToo: false)");
        asking.ShouldNotContain("theRecordingToo: true");

        // It stops when the screen is let go of and when another screen takes the window.
        Body(screen, "public void Pause()").ShouldContain("_workWatch.Stop()");
        Body(screen, "public void Close()").ShouldContain("_workWatch.Stop()");
    }

    /// <summary>
    /// Who made what is a compact table and not a sentence per stage, and the right column scrolls
    /// apart from the left so a short window cuts nothing off.
    /// </summary>
    [Fact]
    public void The_right_column_is_a_table_that_scrolls_apart_from_the_left()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        markup.ShouldContain("x:Name=\"WhoMadeWhat\"");
        markup.ShouldContain("UiTexts.Transcribed)");
        markup.ShouldContain("UiTexts.Summarised)");

        Regex.Matches(markup, @"<ScrollViewer Grid\.Column=""\d""").Count.ShouldBe(
            2,
            "each column of the screen should scroll on its own.");
    }

    private static string Body(string source, string signature)
    {
        var found = Regex.Match(
            source,
            Regex.Escape(signature) + @".*?\r?\n[ ]{4}\}",
            RegexOptions.Singleline);

        found.Success.ShouldBeTrue($"the screen no longer has a `{signature}`.");
        return found.Value;
    }

    /// <summary>
    /// A refusal of a second summary is worded without denying the first one that is on screen.
    /// </summary>
    [Fact]
    public void A_second_summary_that_was_refused_is_not_told_there_is_no_summary()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("ThereIsASummary: false, WhyTheSummaryWasRefused");
        screen.ShouldContain("LastRefusedText(refusedAgain)");
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


    /// <summary>
    /// The name is a line of text with <em>Renombrar</em> beside it, which swaps it for the field
    /// holding the name; Enter and leaving commit and swap back, and Escape swaps back and writes
    /// nothing.
    /// </summary>
    [Fact]
    public void Renaming_swaps_the_title_for_the_field_and_back()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        markup.ShouldContain("x:Name=\"TitleText\"");
        markup.ShouldContain("x:Name=\"RenameButton\"");
        markup.ShouldContain("UiTexts.CorrectThisName");
        Regex.IsMatch(markup, @"<TextBox\s+x:Name=""NameBox""[^>]*Visibility=""Collapsed""")
            .ShouldBeTrue("the name field is no longer collapsed until *Renombrar* is pressed.");
        markup.ShouldNotContain("Header=\"{x:Bind In(loc:UiTexts.TheMeetingsName)}\"");

        var rename = Body(screen, "private void OnRename(");

        rename.ShouldContain("NameBox.Visibility = Visibility.Visible");
        rename.ShouldContain("NameBox.SelectAll()");

        var key = Body(screen, "private void OnNameKey(");
        var enter = key[..key.IndexOf("VirtualKey.Escape", StringComparison.Ordinal)];
        var escape = key[key.IndexOf("VirtualKey.Escape", StringComparison.Ordinal)..];

        enter.ShouldContain("CommitTheName()");
        enter.ShouldContain("SwapToTheTitle()");
        escape.ShouldContain("NameBox.Text = _nameAsRead");
        escape.ShouldContain("SwapToTheTitle()");
        escape.ShouldNotContain("CommitTheName()");

        Body(screen, "private void OnNameLeft(").ShouldContain("SwapToTheTitle()");
        Body(screen, "private bool CommitTheName(").ShouldContain("new MeetingReading(context, TimeProvider.System).Name(");
    }

    /// <summary>
    /// Selecting words on a line offers <em>Corregir</em> beside them, and pressing it asks the
    /// window to open the corrections screen with those words: the third dialogue is gone.
    /// </summary>
    [Fact]
    public void A_selection_is_corrected_on_the_corrections_screen()
    {
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        screen.ShouldContain("CorrectTheSelection.OfferOver(said, Chrome(\"TheSelectionsPress\")");
        screen.ShouldContain("public event EventHandler<WordsToCorrect>? CorrectWords");
        screen.ShouldContain("new WordsToCorrect(meeting, asWritten)");
        screen.ShouldNotContain("CorrectingAWord");
        screen.ShouldNotContain("ContextFlyout");
        screen.ShouldNotContain("AskingHowAWordGoes");

        var ask = Body(screen, "private void AskToCorrect(string? asWritten)");

        ask.IndexOf("CommitTheName()", StringComparison.Ordinal)
            .ShouldBeLessThan(ask.IndexOf("Pause();", StringComparison.Ordinal));

        File.ReadAllText(AppSources.At(Markup).FullName).ShouldNotContain("CorrectingAWord");
        File.Exists(AppSources.At(Path.Combine("MeetingTranscriber.App", "CorrectingAWord.xaml.cs")).FullName)
            .ShouldBeFalse("the dialogue that corrected one word came back.");
    }

    /// <summary>
    /// While work is queued or running the status word and a ring stand where the act was, and
    /// only there: <em>Detener</em> and <em>Resumir de nuevo</em> keep their own conditions.
    /// </summary>
    [Fact]
    public void Work_under_way_is_said_where_the_act_was()
    {
        var offer = Body(File.ReadAllText(AppSources.At(Screen).FullName), "private void TheActOnOffer(MeetingScreen screen)");
        var before = offer[..offer.IndexOf("if (screen.TheSummaryMayBeStopped)", StringComparison.Ordinal)];

        before.ShouldContain("if (screen.WorkIsUnderWay)");
        before.ShouldContain("WorkUnderWay(screen)");

        // The act and *Ignorar* are the other arm, and nothing else is.
        var otherArm = before[before.IndexOf("else", StringComparison.Ordinal)..];

        otherArm.ShouldContain("screen.TheActMayBeLeft");
        otherArm.ShouldContain("screen.TheActOffered");
        otherArm.ShouldContain("UiTexts.Ignore");

        File.ReadAllText(AppSources.At(Screen).FullName).ShouldContain("new ProgressRing { IsActive = true");
    }

    /// <summary>
    /// A running summary is still stopped from the same press as before: the stop and the second
    /// summary are not inside the arm that says work is under way.
    /// </summary>
    [Fact]
    public void A_running_summary_can_still_be_stopped_while_work_is_under_way()
    {
        var offer = Body(File.ReadAllText(AppSources.At(Screen).FullName), "private void TheActOnOffer(MeetingScreen screen)");
        var after = offer[offer.IndexOf("if (screen.TheSummaryMayBeStopped)", StringComparison.Ordinal)..];

        after.ShouldContain("UiTexts.Stop");
        after.ShouldContain("StopTheSummary()");
        after.ShouldContain("if (screen.TheSummaryMayBeAskedForAgain)");
        after.ShouldNotContain("WorkIsUnderWay");
        after.ShouldNotContain("else");
    }

    /// <summary>
    /// A transcription that lands while another is queued leaves the status at <em>queued</em>, so
    /// the poll compares the stage with it.
    /// </summary>
    [Fact]
    public void The_work_watch_compares_the_stage_too()
    {
        var asking = Body(File.ReadAllText(AppSources.At(Screen).FullName), "private async Task AskTheWorkAsync()");

        asking.ShouldContain("(owed.Stage, owed.Status) != (read.Screen.Owed.Stage, read.Screen.Owed.Status)");
        asking.ShouldNotContain("owed.Status != read.Screen.Owed.Status");
    }

    /// <summary>
    /// The player has a volume, a glyph for play and pause, and a track that says a time and never
    /// a number of milliseconds.
    /// </summary>
    [Fact]
    public void The_track_says_a_time()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);
        var converter = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "TrackTime.cs")).FullName);

        markup.ShouldContain("ThumbToolTipValueConverter=\"{StaticResource TrackTime}\"");
        markup.ShouldContain("x:Name=\"VolumeSlider\"");
        markup.ShouldContain("<FontIcon x:Name=\"PlayGlyph\"");
        converter.ShouldContain("ScreenNumbers.Long(");
        screen.ShouldContain("playing.Volume = (float)(e.NewValue / 100)");
        screen.ShouldContain("ToolTipService.SetToolTip(PlayButton");
    }

    /// <summary>
    /// The volume is a speaker glyph, and its slider opens when the pointer comes to it and reaches
    /// 200 %.
    /// </summary>
    [Fact]
    public void The_volume_opens_on_the_pointer_and_reaches_twice()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);
        var screen = File.ReadAllText(AppSources.At(Screen).FullName);

        markup.ShouldContain("Maximum=\"200\"");
        markup.ShouldNotContain("Maximum=\"100\"");
        markup.ShouldMatch(@"x:Name=""VolumeSlider""[^>]*Visibility=""Collapsed""");
        markup.ShouldContain("<FontIcon x:Name=\"VolumeGlyph\"");
        markup.ShouldContain("PointerEntered=\"OnVolumeAreaEntered\"");
        screen.ShouldMatch(@"OnVolumeAreaEntered\(.*?\)\s*\{[^}]*_pointerIsOverTheVolume = true;[^}]*ShowTheVolume\(\)");
        screen.ShouldContain("VolumeSlider.Visibility = open ? Visibility.Visible : Visibility.Collapsed;");
    }
}
