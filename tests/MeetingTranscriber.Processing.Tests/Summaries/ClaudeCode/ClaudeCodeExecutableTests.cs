using MeetingTranscriber.Processing.Summaries.ClaudeCode;

namespace MeetingTranscriber.Processing.Tests.Summaries.ClaudeCode;

/// <summary>Where Claude Code is, when somebody chose and when nobody did.</summary>
public class ClaudeCodeExecutableTests : IDisposable
{
    private readonly TemporaryFolder _temporary = new();

    public void Dispose() => _temporary.Dispose();

    [Fact]
    public void The_one_somebody_chose_wins_over_the_path()
    {
        var onPath = File(Folder("on-path"), "claude.exe");
        var chosen = File(Folder("elsewhere"), "claude.exe");

        var found = ClaudeCodeExecutable.Find(chosen, onPath.DirectoryName);

        found.ShouldBe(chosen);
    }

    [Fact]
    public void The_chosen_one_wins_even_when_it_is_gone()
    {
        var chosen = new FileInfo(Path.Combine(_temporary.Folder.FullName, "gone", "claude.exe"));

        var found = ClaudeCodeExecutable.Find(chosen, null);

        found.ShouldBe(chosen);
    }

    [Fact]
    public void The_first_folder_on_the_path_that_has_it_wins()
    {
        var first = Folder("first");
        var second = Folder("second");
        File(second, "claude.exe");
        var wanted = File(first, "claude.exe");

        var found = ClaudeCodeExecutable.Find(null, $"{first.FullName};{second.FullName}");

        found!.FullName.ShouldBe(wanted.FullName);
    }

    [Fact]
    public void An_exe_is_taken_before_a_cmd_in_the_same_folder()
    {
        var folder = Folder("both");
        File(folder, "claude.cmd");
        var wanted = File(folder, "claude.exe");

        var found = ClaudeCodeExecutable.Find(null, folder.FullName);

        found!.FullName.ShouldBe(wanted.FullName);
    }

    [Fact]
    public void A_folder_with_neither_name_is_skipped_for_the_one_that_has_it()
    {
        var empty = Folder("empty");
        var folder = Folder("has-it");
        var wanted = File(folder, "claude.cmd");

        var found = ClaudeCodeExecutable.Find(null, $"{empty.FullName};{folder.FullName}");

        found!.FullName.ShouldBe(wanted.FullName);
    }

    [Fact]
    public void Nowhere_is_nothing()
    {
        var empty = Folder("empty");

        var found = ClaudeCodeExecutable.Find(null, empty.FullName);

        found.ShouldBeNull();
    }

    private DirectoryInfo Folder(string name)
    {
        var folder = new DirectoryInfo(Path.Combine(_temporary.Folder.FullName, name));
        folder.Create();
        return folder;
    }

    private static FileInfo File(DirectoryInfo folder, string name)
    {
        var file = new FileInfo(Path.Combine(folder.FullName, name));
        System.IO.File.WriteAllText(file.FullName, string.Empty);
        return file;
    }
}
