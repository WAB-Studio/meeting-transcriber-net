namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// What <c>claude-live</c> refuses before it spends a run of the person's Claude plan. The run itself
/// is an opt-in command and never part of <c>dotnet test</c>.
/// </summary>
public class ClaudeLiveCommandTests : IDisposable
{
    private readonly TemporaryFolder temporary = new();

    public void Dispose() => temporary.Dispose();

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
        var mine = Path.Combine(temporary.Folder.FullName, "mine.txt");
        File.WriteAllText(mine, "keep");

        var run = CommandLine.Of("claude-live", "--out", temporary.Folder.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Output.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(temporary.Folder.FullName).ShouldBe([mine]);
    }
}
