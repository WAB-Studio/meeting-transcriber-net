namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Which programs a person is offered for channel 0. Nothing here asks the machine what is
/// running, which windows are open or who plays: those are the machine's answers, and this is the
/// rule applied to them.
/// </summary>
public class AudioProgramsTests
{
    private const int Me = 10;
    private const int Shell = 900;

    private static readonly AudioProcess TheShell = new(Shell, "explorer", 4);
    private static readonly AudioProcess Browser = new(1000, "brave", Shell);
    private static readonly AudioProcess Teams = new(2000, "ms-teams", Shell);

    private static IReadOnlyList<OfferedProgram> Offered(
        IReadOnlyList<AudioProcess> running,
        IReadOnlyList<ProgramWindow> windows,
        params int[] playing) =>
        AudioPrograms.OnePerProgram(running, windows, playing.ToHashSet(), Me, Shell);

    [Fact]
    public void A_browser_of_twenty_processes_is_one_entry()
    {
        var helpers = Enumerable.Range(1, 19).Select(n => new AudioProcess(1000 + n, "brave", Browser.Id));
        AudioProcess[] running = [TheShell, Browser, .. helpers];
        ProgramWindow[] windows = [new(1000, "Inbox"), new(1005, "Docs")];

        var offered = Offered(running, windows, 1007);

        offered.ShouldBe([new OfferedProgram(Browser, "Inbox")]);
    }

    [Fact]
    public void A_session_played_by_a_child_of_a_windowed_application_is_that_application()
    {
        var webView = new AudioProcess(2100, "msedgewebview2", Teams.Id);
        var playsFromTheView = new AudioProcess(2101, "msedgewebview2", webView.Id);

        var offered = Offered(
            [TheShell, Teams, webView, playsFromTheView], [new ProgramWindow(2000, "Standup")], 2101);

        offered.ShouldBe([new OfferedProgram(Teams, "Standup")]);
    }

    [Fact]
    public void A_program_that_only_plays_is_offered_under_its_own_name()
    {
        var player = new AudioProcess(3000, "spotify", 4);

        Offered([TheShell, Browser, player], [new ProgramWindow(1000, "Inbox")], 3000)
            .ShouldBe([new OfferedProgram(Browser, "Inbox"), new OfferedProgram(player, string.Empty)]);
    }

    [Fact]
    public void A_program_that_only_plays_started_by_the_shell_is_offered_under_its_own_name()
    {
        var tray = new AudioProcess(3000, "winamp", Shell);

        // The shell owns visible windows and is the tray player's parent: it is never what the
        // player folds into, or the player would be offered nowhere.
        Offered([TheShell, tray], [new ProgramWindow(Shell, "Program Manager")], 3000)
            .ShouldBe([new OfferedProgram(tray, string.Empty)]);
    }

    [Fact]
    public void A_store_applications_frame_is_not_offered_and_the_application_is_offered_by_its_session()
    {
        var frame = new AudioProcess(4000, "ApplicationFrameHost", 4);
        var player = new AudioProcess(4100, "Music", 4);

        Offered([TheShell, frame, player], [new ProgramWindow(4000, "Music")], 4100)
            .ShouldBe([new OfferedProgram(player, string.Empty)]);
    }

    [Fact]
    public void This_application_and_the_shell_are_never_offered()
    {
        var me = new AudioProcess(Me, "MeetingTranscriber.App", Shell);

        Offered(
            [TheShell, me],
            [new ProgramWindow(Shell, "Program Manager"), new ProgramWindow(Me, "Meeting Transcriber")],
            Me,
            Shell)
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_title_is_the_first_window_of_the_tree()
    {
        var helper = new AudioProcess(1001, "brave", Browser.Id);

        Offered([TheShell, Browser, helper], [new ProgramWindow(1001, "Front"), new ProgramWindow(1000, "Behind")])
            .Single().Title.ShouldBe("Front");
    }
}
