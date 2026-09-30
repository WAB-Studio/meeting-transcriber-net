using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// Which executable this user chose as their own Claude Code, kept the way <c>CorpusLocation</c>
/// keeps where the corpus is.
/// </summary>
public class ClaudeCodeLocationTests
{
    [Fact]
    public void Nobody_having_chosen_one_is_nothing_chosen()
    {
        using var folder = new TemporaryFolder();
        var location = new ClaudeCodeLocation(Setting(folder));

        location.Chosen().ShouldBeNull();
    }

    [Fact]
    public void Choosing_writes_the_executable_and_reads_it_back()
    {
        using var folder = new TemporaryFolder();
        var location = new ClaudeCodeLocation(Setting(folder));
        var executable = new FileInfo(Path.Combine(folder.Folder.FullName, "claude.exe"));

        location.Choose(executable);

        location.Chosen()!.FullName.ShouldBe(executable.FullName);
    }

    /// <summary>Goes red with a later choice appended to the setting file instead of replacing it.</summary>
    [Fact]
    public void Choosing_again_replaces_what_was_chosen_before()
    {
        using var folder = new TemporaryFolder();
        var location = new ClaudeCodeLocation(Setting(folder));
        var first = new FileInfo(Path.Combine(folder.Folder.FullName, "claude.exe"));
        var second = new FileInfo(Path.Combine(folder.Folder.FullName, "other", "claude.cmd"));

        location.Choose(first);
        location.Choose(second);

        location.Chosen()!.FullName.ShouldBe(second.FullName);
    }

    /// <summary>
    /// A setting file naming something other than a full path — the same refusal
    /// <c>CorpusLocation</c>'s own read gives a relative or malformed one — is nothing chosen
    /// rather than a path resolved against a working directory this process did not pick.
    /// </summary>
    [Fact]
    public void A_setting_file_that_names_no_full_path_reads_as_nothing_chosen()
    {
        using var folder = new TemporaryFolder();
        var setting = Setting(folder);
        File.WriteAllText(setting.FullName, "claude.exe");

        new ClaudeCodeLocation(setting).Chosen().ShouldBeNull();
    }

    private static FileInfo Setting(TemporaryFolder folder) =>
        new(Path.Combine(folder.Folder.FullName, ClaudeCodeLocation.SettingName));
}
