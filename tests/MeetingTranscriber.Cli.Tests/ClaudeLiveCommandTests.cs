namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// What <c>claude-live</c> refuses before it spends a run of the person's Claude plan. The run itself
/// is an opt-in command and never part of <c>dotnet test</c>.
/// </summary>
public class ClaudeLiveCommandTests : IDisposable
{
    private readonly DirectoryInfo scratch =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "claude-live-" + Guid.NewGuid().ToString("N")));

    public void Dispose() => scratch.Delete(recursive: true);

    [Fact]
    public void Without_an_out_folder_the_command_is_misused()
    {
        var run = CommandLine.Of("claude-live");

        run.Code.ShouldBe(Cli.Misused);
        run.Output.ShouldBeEmpty();
    }

    [Fact]
    public void An_out_folder_that_already_holds_something_is_refused_before_anything_runs()
    {
        var mine = Path.Combine(scratch.FullName, "mine.txt");
        File.WriteAllText(mine, "keep");

        var run = CommandLine.Of("claude-live", "--out", scratch.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Output.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(scratch.FullName).ShouldBe([mine]);
    }
}
