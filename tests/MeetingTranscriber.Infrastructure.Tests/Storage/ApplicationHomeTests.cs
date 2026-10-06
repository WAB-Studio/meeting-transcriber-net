using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// The folder the application keeps its two pointers and its first corpus in, which the UI probe
/// changes on the launch line so a walk never touches the owner's.
/// </summary>
public class ApplicationHomeTests
{
    [Fact]
    public void A_launch_with_no_home_is_this_users()
    {
        var expected = ApplicationHome.OfThisUser().Folder.FullName;

        ApplicationHome.FromLaunch(null).Folder.FullName.ShouldBe(expected);
        ApplicationHome.FromLaunch("").Folder.FullName.ShouldBe(expected);
        ApplicationHome.FromLaunch("something else").Folder.FullName.ShouldBe(expected);
        ApplicationHome.FromLaunch("--homeless").Folder.FullName.ShouldBe(expected);
    }

    [Fact]
    public void Every_pointer_of_this_user_is_kept_in_the_one_profile_folder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            CorpusLocation.ApplicationFolderName);

        ApplicationHome.ProfileFolder().FullName.ShouldBe(expected);
        ApplicationHome.OfThisUser().Folder.FullName.ShouldBe(expected);
        CorpusLocation.OfThisUser().Fallback.FullName.ShouldBe(expected);
        ClaudeCodeLocation.OfThisUser().Setting.Directory!.FullName.ShouldBe(expected);
    }

    [Fact]
    public void A_home_on_the_launch_line_holds_both_pointers_and_the_first_corpus()
    {
        using var folder = new TemporaryFolder();

        var home = ApplicationHome.FromLaunch($"--home {folder.Folder.FullName}");

        home.Folder.FullName.ShouldBe(folder.Folder.FullName);
        home.Corpus.Setting.FullName.ShouldBe(Path.Combine(folder.Folder.FullName, CorpusLocation.SettingName));
        home.Corpus.Fallback.FullName.ShouldBe(folder.Folder.FullName);
        home.ClaudeCode.Setting.FullName.ShouldBe(Path.Combine(folder.Folder.FullName, ClaudeCodeLocation.SettingName));
    }

    [Theory]
    [InlineData("--home")]
    [InlineData("--home \"\"")]
    [InlineData("--home \"unterminated")]
    [InlineData("--home relative\folder")]
    public void A_home_with_nothing_usable_after_it_is_refused(string line) =>
        Should.Throw<ArgumentException>(() => ApplicationHome.FromLaunch(line));

    /// <summary>
    /// The confirmation the probe refuses a start on. A launch that was told its home writes the
    /// report there; a launch whose line never arrived resolves the owner's home, writes nothing,
    /// and so is never confirmed.
    /// </summary>
    [Fact]
    public void A_launch_that_was_told_its_home_reports_it_and_one_that_was_not_does_not()
    {
        using var folder = new TemporaryFolder();
        var probeHome = ApplicationHome.Under(folder.Folder);
        probeHome.HasBeenReported().ShouldBeFalse();

        // The line arrives: the application reports the home it resolved.
        ApplicationHome.FromLaunch(ApplicationHome.LaunchArgumentsFor(folder.Folder)).ReportIfAsked();
        probeHome.HasBeenReported().ShouldBeTrue();

        // The line is dropped: a home nobody named is resolved (a stand-in folder here, never the
        // owner's) and nothing is written to it or to the probe's, so the start is refused.
        probeHome.ForgetReport();
        ApplicationHome.FromLaunch(string.Empty).Asked.ShouldBeFalse();
        using var standIn = new TemporaryFolder();
        var fellBack = ApplicationHome.Under(standIn.Folder);
        fellBack.ReportIfAsked();
        fellBack.Report.Exists.ShouldBeFalse("a launch nobody named a home for wrote a report");
        probeHome.HasBeenReported().ShouldBeFalse();
    }

    [Fact]
    public void A_report_naming_another_home_does_not_confirm_this_one()
    {
        using var folder = new TemporaryFolder();
        var home = ApplicationHome.Under(folder.Folder);
        File.WriteAllText(home.Report.FullName, Path.Combine(folder.Folder.FullName, "elsewhere"));

        home.HasBeenReported().ShouldBeFalse();
    }

    [Fact]
    public void What_the_probe_writes_is_what_the_application_reads()
    {
        using var folder = new TemporaryFolder();
        var withSpaces = new DirectoryInfo(Path.Combine(folder.Folder.FullName, "a home with spaces"));

        var read = ApplicationHome.FromLaunch(ApplicationHome.LaunchArgumentsFor(withSpaces));

        read.Folder.FullName.ShouldBe(withSpaces.FullName);
    }
}
