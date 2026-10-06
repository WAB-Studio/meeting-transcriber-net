using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// What the window the application opens on says and does not say: the report under the card, the
/// bar's way back, the channels without their numbers, and the meters read often.
/// </summary>
/// <remarks>
/// Source and not a running window, for the reason every check in this project is: the window needs
/// a UI thread and a packaged host. What is held is the shape the walk of the installed build found
/// wrong — a report that said what worked and could be selected into a text cursor, a screen with
/// no way out that did not go through the screen's own door, a channel named by Deepgram's index —
/// so that none of them comes back without a test going red. What the screen then looks like is the
/// UI probe's.
/// </remarks>
public partial class MainScreenTests
{
    private static readonly string Markup = Path.Combine("MeetingTranscriber.App", "MainWindow.xaml");

    private static readonly string Code = Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs");

    /// <summary>
    /// The report is something to read and never something to select: a cursor that turns into a
    /// text cursor over a line nobody asked to copy is the cursor saying the screen is editable.
    /// </summary>
    [Fact]
    public void The_report_is_never_selectable() =>
        Read(Markup).ShouldNotContain(
            "IsTextSelectionEnabled=\"True\"",
            customMessage: "something on the main window is selectable again, so the cursor turns into a text cursor over it.");

    /// <summary>
    /// The report takes no room while it holds no line: it starts collapsed, and the code that
    /// writes a line is what shows it.
    /// </summary>
    [Fact]
    public void The_report_takes_no_room_while_it_holds_nothing()
    {
        Regex.IsMatch(Read(Markup), @"<ScrollViewer x:Name=""TheReport""[^>]*Visibility=""Collapsed""")
            .ShouldBeTrue("the report's scroller no longer starts collapsed.");

        Read(Code).ShouldContain("TheReport.Visibility = _report.Count > 0");
    }

    /// <summary>
    /// A stop that worked says nothing: the meeting is in the list and that is the answer. The only
    /// sentence <c>OnStop</c> may say is the one for a stop that failed.
    /// </summary>
    [Fact]
    public void A_stop_says_nothing_when_it_worked()
    {
        var stop = Method("private async void OnStop(");
        var said = Regex.Matches(stop, @"\bSay\(\s*(?<what>[\w.]+)").Select(match => match.Groups["what"].Value).ToArray();

        said.ShouldBe(
            ["UiTexts.TheMeetingCouldNotBeMade"],
            "stopping says something besides that it failed. The meeting is on the list, and what the stop "
            + "queued is its status there; a sentence here is a second account that can disagree.");

        // And the other presses that move a recording say nothing when they worked either, except the
        // one a narrator would otherwise never hear.
        Read(Code).ShouldNotContain("Say(UiTexts.NowFollowingAnotherProgram");
        Read(Code).ShouldNotContain("Say(UiTexts.NowRecordingTheWholeMachine");
    }

    /// <summary>
    /// The bar's back button leaves whichever of the six sub-screens has the room, through that
    /// screen's own call. The refusal to be left lives in the call, so the button and the keyboard
    /// reach the same door.
    /// </summary>
    [Fact]
    public void The_back_button_goes_back_through_the_screen_that_has_the_room()
    {
        // One ordered list, read by the three questions about the sub-screens. The corrections come
        // before the voices because they open over them and win while they are open.
        var list = Method("private SubScreen[] TheSubScreens(");
        var order = new[] { "Settings", "Classifying", "Corrections", "Voices", "NodeStory", "Reading" };

        foreach (var screen in order)
        {
            list.ShouldContain(screen + ".GoBack", customMessage: $"the way back never leaves {screen}.");
        }

        order.Select(screen => list.IndexOf("new(" + screen + ",", StringComparison.Ordinal))
            .ShouldBe(order.Select(screen => list.IndexOf("new(" + screen + ",", StringComparison.Ordinal)).Order());

        Method("private FrameworkElement? TheSubScreenWithTheRoom(").ShouldContain("TheSubScreens()");
        Method("private void GoBack(").ShouldContain("TheSubScreens()");
        Method("private void ShowWhatTheRoomIsShowing(").ShouldContain("TheSubScreens()");
        Method("private void PlaceTheSubScreens(").ShouldContain("TheSubScreens()");

        var window = Read(Code);

        window.ShouldContain("Key = Windows.System.VirtualKey.Left");
        window.ShouldContain("Modifiers = Windows.System.VirtualKeyModifiers.Menu");
        Read(Markup).ShouldContain("Click=\"OnBack\"");
    }

    /// <summary>
    /// No channel number reaches the screen: the index is Deepgram's, and a strip is named by what it
    /// hears. Neither the entries nor the chip they were drawn in survive.
    /// </summary>
    [Fact]
    public void No_channel_number_reaches_the_screen()
    {
        var code = Read(Code);

        code.ShouldNotContain("UiTexts.Channel0");
        code.ShouldNotContain("UiTexts.Channel1");
        Read(Path.Combine("MeetingTranscriber.App", "ChannelStrip.xaml")).ShouldNotContain("x:Name=\"Chip\"");
        Read(Path.Combine("MeetingTranscriber.App", "ChannelStrip.xaml.cs")).ShouldNotContain("Chip");
    }

    /// <summary>
    /// The meters are read about twenty times a second and the clock once: a bar redrawn once a
    /// second cannot rise when somebody speaks, and the words are written from the loudest reading of
    /// a quarter of a second so that a pause between two words does not read as nothing arriving.
    /// </summary>
    [Fact]
    public void The_meters_are_read_often_and_the_second_does_not_read_them()
    {
        var code = Read(Code);

        code.ShouldContain("_meters = new() { Interval = TimeSpan.FromMilliseconds(50) }");

        Method("private void OnWatch(").ShouldNotContain("ReadTheDevices()");
        code.ShouldNotContain("ReadingsPerWriting");

        Method("private void ReadTheDevices(").ShouldContain("_words.Take(_channels,");
        Method("private void OnMeters(").ShouldContain("ReadTheDevices();");
    }

    /// <summary>
    /// The bar is the title bar: the platform's is not drawn, the window is dragged by a region the
    /// bar hands over that leaves the back button out, and the caption buttons' width is read off the
    /// window rather than guessed.
    /// </summary>
    [Fact]
    public void The_app_bar_is_the_title_bar()
    {
        var bar = Method("private void ExtendTheBarIntoTheTitleBar(");

        bar.ShouldContain("ExtendsContentIntoTitleBar = true;");
        bar.ShouldContain("TitleBarHeightOption.Tall");
        bar.ShouldContain("SetTitleBar(TheDragRegion);");
        Method("private void ReserveTheCaptionButtons(").ShouldContain("AppWindow.TitleBar.RightInset");

        // The region is behind the mark and the name, in the columns after the back button's, and
        // holds nothing pressable: a press inside a drag region is a drag. It is drawn with a fill
        // nobody sees and not left with none, because a press falls through an element that
        // has none and the bar then does not move the window (fb-106, ISC-225).
        var markup = Read(Markup);
        Regex.IsMatch(
                markup,
                @"<Border x:Name=""TheDragRegion"" Grid.Column=""1"" Grid.ColumnSpan=""2"" Style=""{StaticResource DragFill}"" />")
            .ShouldBeTrue("the drag region is no longer an element over the mark and the name, drawn with DragFill.");
        Read(Path.Combine("MeetingTranscriber.App", "Olivo.xaml")).ShouldMatch(
            "x:Key=\"DragFill\"[^>]*>\\s*<Setter Property=\"Background\" Value=\"Transparent\" />");
        markup.ShouldContain("x:Name=\"TitleText\"");
        markup.ShouldContain("x:Name=\"CaptionSpace\"");
    }

    [Fact]
    public void The_accelerator_shows_no_tooltip() =>
        Read(Code).ShouldContain(
            "Content.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;");

    /// <summary>
    /// Pausing and carrying on are one press, a glyph that is the state's: there is no second button
    /// for carrying on to be dead beside while the first is live.
    /// </summary>
    [Fact]
    public void Pause_and_carry_on_are_one_press()
    {
        Read(Markup).ShouldNotContain("ResumeButton");
        Read(Code).ShouldNotContain("ResumeButton");
        Read(Code).ShouldNotContain("OnResume");

        var press = Method("private void ShowThePausePress(");
        press.ShouldContain("screen.State == RecorderState.Paused");
        press.ShouldContain("UiTexts.Resume : UiTexts.Pause");
        press.ShouldContain("ToolTipService.SetToolTip(PauseButton");
        press.ShouldContain("AutomationProperties.SetName(PauseButton");

        // Which of the two it does is the screen's answer and never the glyph's.
        var pressed = Method("private void OnPause(");
        pressed.ShouldContain("screen.Allows(RecorderPress.Resume)");
        pressed.ShouldContain("recording.Resume();");
        pressed.ShouldContain("recording.Pause();");
    }

    /// <summary>
    /// Both clocks on screen — the stopwatch and the strip — are the one <c>ClockAt</c>, which takes
    /// the paused time off the stretch since the devices opened (ISC-222.3).
    /// </summary>
    [Fact]
    public void The_clock_leaves_the_pause_out()
    {
        var clock = Method("private RecordingClock ClockAt(");

        clock.ShouldContain("_recording?.PausedFor(now)");
        clock.ShouldContain("RecordingClock.Of(");

        Regex.Matches(Comments().Replace(Read(Code), string.Empty), @"RecordingClock\.Of\(").Count
            .ShouldBe(1, "a second reading of the clock a line apart would be two numbers on one screen.");

        Method("private void Refresh(").ShouldContain("ClockAt(screen.State)");
        Method("private void OnWatch(").ShouldContain("ClockAt(screen.State)");
    }

    /// <summary>
    /// What the meters listen to before a meeting is let go of — and waited for — before the meeting
    /// opens the same devices, because two process loopbacks at once are not assumed to work.
    /// </summary>
    [Fact]
    public void Listening_stops_before_the_devices_open()
    {
        var record = Method("private async void OnRecord(");

        record.ShouldContain("await _listeningWork;");
        record.IndexOf("await _listeningWork;", StringComparison.Ordinal)
            .ShouldBeLessThan(
                record.IndexOf("MeetingRecording.Start(", StringComparison.Ordinal),
                "the meeting opens its devices before the listening has let go of them.");
        record.IndexOf("_step = RecorderStep.Starting;", StringComparison.Ordinal)
            .ShouldBeLessThan(
                record.IndexOf("await _listeningWork;", StringComparison.Ordinal),
                "the listening is waited on before the refresh that asks it to close.");

        // Asked for while the screen is choosing, the recorder card is up and the window is in front.
        var asks = Method("private void ListenAsTheScreenAsks(");
        asks.ShouldContain("screen.State == RecorderState.Choosing");
        asks.ShouldContain("screen.TheRecorderIsOnScreen");
        asks.ShouldContain("_windowIsActive");
        Method("private void Refresh(").ShouldContain("ListenAsTheScreenAsks(screen);");
        Read(Code).ShouldContain("Activated += OnActivated;");
    }

    /// <summary>
    /// Filing reads the meeting again before it opens the voices, so their way back returns to a
    /// meeting that already says what was filed (fb-63).
    /// </summary>
    [Fact]
    public void Filing_reads_the_meeting_again_before_the_voices_open()
    {
        var filed = Method("private void OnFiled(");

        filed.ShouldContain("Reading.ReadAgain();");
        filed.IndexOf("Reading.ReadAgain();", StringComparison.Ordinal)
            .ShouldBeLessThan(
                filed.IndexOf("Voices.Show(", StringComparison.Ordinal),
                "the voices open over a meeting that has not been read again.");
        Method("private bool SomebodyIsUnnamedOn(").ShouldContain("!voice.SettledByTheRecording");
    }

    private static string Read(string file) => File.ReadAllText(AppSources.At(file).FullName);

    /// <summary>The code of the one method that opens on <paramref name="opening"/>, comments left out.</summary>
    private static string Method(string opening)
    {
        var code = Read(Code);
        var start = code.IndexOf(opening, StringComparison.Ordinal);

        start.ShouldBeGreaterThan(-1, $"MainWindow has no `{opening}`.");

        var end = code.IndexOf("\n    }\r\n", start, StringComparison.Ordinal);

        if (end < 0)
        {
            end = code.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        }

        end.ShouldBeGreaterThan(start, $"the method `{opening}` has no end this can find.");

        return Comments().Replace(code[start..end], string.Empty);
    }

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex Comments();
}
