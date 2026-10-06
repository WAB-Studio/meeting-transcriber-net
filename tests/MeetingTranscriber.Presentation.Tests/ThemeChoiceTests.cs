namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-203.4: a theme somebody chose is kept, so choosing it once is enough, and nobody having
/// chosen follows Windows.
/// </summary>
public class ThemeChoiceTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ui-theme-");

    [Fact]
    public void Nobody_has_chosen_until_somebody_does_and_that_follows_Windows()
    {
        Choice().Read().ShouldBe(AppTheme.System);
    }

    [Fact]
    public void What_was_chosen_is_what_comes_back()
    {
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            Choice().Write(theme);

            // A second reader, because the point is what is on disk and not what is in memory.
            Choice().Read().ShouldBe(theme);
        }
    }

    [Fact]
    public void Choosing_again_replaces_the_choice_rather_than_adding_to_it()
    {
        var choice = Choice();

        choice.Write(AppTheme.Dark);
        choice.Write(AppTheme.Light);

        choice.Read().ShouldBe(AppTheme.Light);
        File.ReadAllText(choice.Location.FullName).ShouldBe("light");
    }

    [Fact]
    public void The_first_choice_makes_the_place_it_is_kept()
    {
        var file = new FileInfo(Path.Combine(_folder.FullName, "never", "made", "ui-theme"));

        new ThemeChoice(file).Write(AppTheme.Dark);

        new ThemeChoice(file).Read().ShouldBe(AppTheme.Dark);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sepia")]
    [InlineData("3")]
    public void Something_that_names_no_theme_reads_as_following_Windows(string written)
    {
        var file = new FileInfo(Path.Combine(_folder.FullName, "ui-theme"));
        File.WriteAllText(file.FullName, written);

        new ThemeChoice(file).Read().ShouldBe(AppTheme.System);
    }

    [Fact]
    public void Every_theme_is_stored_as_a_word_of_its_own()
    {
        Enum.GetValues<AppTheme>().Select(ThemeChoice.Tag).Distinct().Count()
            .ShouldBe(Enum.GetValues<AppTheme>().Length);
    }

    [Fact]
    public void This_users_choice_is_kept_beside_the_language_and_outside_the_corpus()
    {
        var location = ThemeChoice.OfThisUser().Location;

        location.FullName.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        location.Name.ShouldBe("ui-theme");
        location.Directory!.FullName.ShouldBe(LanguageChoice.OfThisUser().Location.Directory!.FullName);
    }

    public void Dispose()
    {
        _folder.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private ThemeChoice Choice() => new(new FileInfo(Path.Combine(_folder.FullName, "ui-theme")));
}
