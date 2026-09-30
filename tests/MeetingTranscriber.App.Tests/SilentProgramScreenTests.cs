namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The window's half of the silent-program notice: that it says which program nothing came from,
/// that channel 0's meter reads <em>sin señal</em>, and that choosing in channel 0's picker moves
/// the channel through the recording.
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
    /// A pick in channel 0's picker while the notice has it open is the recording's to carry out,
    /// off the UI thread, and only when the screen allows it.
    /// </summary>
    [Fact]
    public void Choosing_in_channel_0s_picker_mid_meeting_moves_it_through_the_recording()
    {
        var window = File.ReadAllText(AppSources.At(Window).FullName);

        window.ShouldContain("Task.Run(() => recording.FollowAnotherProgram(");
        window.ShouldContain("Screen().Allows(RecorderPress.FollowAnotherProgram)");
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
    /// Two presses named <em>Cambiar</em> read the same to somebody who cannot see which row they
    /// are beside, so this one says what it changes.
    /// </summary>
    [Fact]
    public void Cambiar_on_the_notice_is_named_for_what_it_changes()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        markup.ShouldContain("AutomationProperties.Name=\"{x:Bind In(loc:UiTexts.ChangeWhatChannel0Follows)}\"");
    }
}
