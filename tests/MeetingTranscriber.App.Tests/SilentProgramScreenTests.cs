namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The window's half of the silent-program notice: that it says which program nothing came from,
/// that channel 0's meter reads <em>sin señal</em>, and that choosing in channel 0's picker — live
/// for the whole meeting, paused or not — moves the channel through the recording.
/// </summary>
/// <remarks>
/// Read off source, like its neighbours, because a window needs a UI thread and a packaged host
/// that a build agent has not. What the rules are is <c>RecorderScreenTests</c>'; what is held
/// here is that the controls are wired to them. Nothing here reaches a control wired to the right
/// question that is drawn wrongly, and the only thing that does is the walk of #102 on a real
/// device.
/// </remarks>
public class SilentProgramScreenTests
{
    private static readonly string Window = Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs");

    private static readonly string Markup = Path.Combine("MeetingTranscriber.App", "MainWindow.xaml");

    /// <summary>
    /// A pick in channel 0's picker mid-meeting is the recording's to carry out, off the UI thread,
    /// and only when the screen allows it — onto a program, or onto the whole machine, which is
    /// somebody choosing it.
    /// </summary>
    [Fact]
    public void Choosing_in_channel_0s_picker_mid_meeting_moves_it_through_the_recording()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("await Task.Run(() =>");
        window.ShouldContain("recording.FollowAnotherProgram(program)");
        window.ShouldContain("recording.RecordTheWholeMachine()");
        window.ShouldContain("screen.Allows(RecorderPress.ChangeTheSource)");
        window.ShouldContain("TheOthers.PickerIsLive = choosing || screen.Allows(RecorderPress.ChangeTheSource)");
    }

    /// <summary>
    /// <em>Cambiar</em> on the notice opens the one picker, which is live through the meeting, and
    /// nothing else: there is no second list of programs to move onto for a pick to be a position in.
    /// </summary>
    [Fact]
    public void Cambiar_opens_the_one_source_picker_and_there_is_no_second_list()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("TheOthers.Open();");
        window.ShouldNotContain("_programsToMoveTo");
    }

    [Fact]
    public void The_notice_names_the_program_and_the_meter_says_no_signal()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("OthersSentNothing,");
        window.ShouldContain("UiTexts.NothingCameFromThatProgram");
        window.ShouldContain("In(UiTexts.NoSignal)");
    }

    /// <summary>
    /// The notice has a second sentence for the program having gone, and the window asks the
    /// recording for it on the tick, paused or not, and hands it to the screen.
    /// </summary>
    [Fact]
    public void The_notice_says_when_the_program_went_away_and_the_window_asks_the_recording()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("UiTexts.ThatProgramWentAway");
        window.ShouldContain("recording.TheProgramWentAway()");
        window.ShouldContain("TheProgramWentAway = _wentAway");
    }

    /// <summary>
    /// The source picker lists programs in the order the move list does, because it is one picker
    /// and the two must agree: one place decides that order and this window asks it, once for each
    /// of the two lists.
    /// </summary>
    [Fact]
    public void The_source_picker_lists_programs_in_the_order_the_move_list_does()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);
        var start = window.IndexOf("private void ReadThePrograms()", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "MainWindow no longer has ReadThePrograms.");
        var body = window[start..window.IndexOf("private string NameOf(", start, StringComparison.Ordinal)];

        body.ShouldContain("RecorderScreen.ProgramsOnOffer(");
        body.ShouldNotContain("OrderBy(", customMessage: "ReadThePrograms orders the programs itself again.");

        // There is one list now, so there is nothing for it to disagree with: every offer of
        // programs, before a meeting and during one, is `_sources`, built here.
        window.ShouldNotContain(
            "ProgramsOnOffer(offered, recording",
            customMessage: "a second list of programs is ordered where the picker's is not.");
    }

    /// <summary>
    /// Two presses named <em>Cambiar</em> read the same to somebody who cannot see which row they
    /// are beside, so this one says what it changes.
    /// </summary>
    [Fact]
    public void Cambiar_on_the_notice_is_named_for_what_it_changes()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        markup.ShouldContain("AutomationProperties.Name=\"{x:Bind In(loc:UiTexts.ChangeWhatTheOthersFollow)}\"");
    }
}
