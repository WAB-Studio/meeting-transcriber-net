namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// How a name somebody typed becomes the one program channel 0 follows. Nothing here asks the
/// machine what is running: that is the machine's answer, and this is the rule applied to it.
/// </summary>
public class AudioProcessesTests
{
    private const int Shell = 900;

    private static readonly AudioProcess Browser = new(1000, "msedge", Shell);
    private static readonly AudioProcess Renderer = new(1001, "msedge", Browser.Id);
    private static readonly AudioProcess Audio = new(1002, "msedge", Browser.Id);
    private static readonly AudioProcess Teams = new(2000, "ms-teams", Shell);

    [Fact]
    public void A_process_id_finds_its_program()
    {
        AudioProcesses.Choose([Browser, Renderer, Teams], "1001").ShouldBe(Renderer);
    }

    [Fact]
    public void A_process_id_nothing_is_running_as_says_so()
    {
        Should.Throw<AudioCaptureException>(() => AudioProcesses.Choose([Browser, Teams], "4242"))
            .Message.ShouldContain("4242");
    }

    [Fact]
    public void A_whole_name_finds_its_program()
    {
        AudioProcesses.Choose([Browser, Teams], "ms-teams").ShouldBe(Teams);
    }

    [Fact]
    public void The_part_of_a_name_somebody_types_finds_its_program()
    {
        AudioProcesses.Choose([Browser, Teams], "teams").ShouldBe(Teams);
    }

    /// <summary>
    /// The rule the whole class is here for. A browser in a meeting is a dozen processes of one
    /// name and the audio comes out of a child, so the name has to resolve to the root of that
    /// tree — which is also the only process whose tree contains the rest of them.
    /// </summary>
    [Fact]
    public void A_name_that_is_a_whole_tree_finds_the_process_that_started_it()
    {
        AudioProcesses.Choose([Renderer, Audio, Browser], "msedge").ShouldBe(Browser);
    }

    /// <summary>
    /// An exact name wins over a part of another one, so a program cannot be made unreachable by
    /// another whose name contains its own.
    /// </summary>
    [Fact]
    public void A_name_that_is_also_part_of_another_still_finds_its_own()
    {
        var teams = new AudioProcess(3000, "Teams", Shell);
        var helper = new AudioProcess(3001, "TeamsMeetingAddin", Shell);

        AudioProcesses.Choose([helper, teams], "Teams").ShouldBe(teams);
    }

    /// <summary>
    /// Two windows of the same application really are two trees, and picking one of them is
    /// picking which meeting gets recorded.
    /// </summary>
    [Fact]
    public void Two_trees_of_one_name_are_refused_with_both_named()
    {
        var second = new AudioProcess(1100, "msedge", Shell);

        var refused = Should.Throw<AudioCaptureException>(
            () => AudioProcesses.Choose([Browser, Renderer, second], "msedge"));

        refused.Message.ShouldContain("2 programs");
        refused.Message.ShouldContain("pid 1000");
        refused.Message.ShouldContain("pid 1100");
    }

    [Fact]
    public void A_name_nothing_answers_to_says_so()
    {
        Should.Throw<AudioCaptureException>(() => AudioProcesses.Choose([Browser, Teams], "zoom"))
            .Message.ShouldContain("zoom");
    }

    [Fact]
    public void Naming_nothing_asks_for_a_name_rather_than_guessing()
    {
        Should.Throw<AudioCaptureException>(() => AudioProcesses.Choose([Browser, Teams], " "))
            .Message.ShouldContain("by name or by process id");
    }
}
