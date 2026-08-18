namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-153, the "unless somebody chose another" half: picking a language once has to be enough.
/// </summary>
public class LanguageChoiceTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ui-language-");

    [Fact]
    public void Nobody_has_chosen_until_somebody_does()
    {
        Choice().Read().ShouldBeNull();
    }

    [Fact]
    public void What_was_chosen_is_what_comes_back()
    {
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            var choice = Choice();
            choice.Write(language);

            // A second reader, because the point is what is on disk and not what is in memory.
            Choice().Read().ShouldBe(language);
        }
    }

    [Fact]
    public void Choosing_again_replaces_the_choice_rather_than_adding_to_it()
    {
        var choice = Choice();

        choice.Write(UiLanguage.Spanish);
        choice.Write(UiLanguage.English);

        choice.Read().ShouldBe(UiLanguage.English);
    }

    [Fact]
    public void The_first_choice_makes_the_place_it_is_kept()
    {
        var file = new FileInfo(Path.Combine(_folder.FullName, "never", "made", "ui-language"));

        new LanguageChoice(file).Write(UiLanguage.Spanish);

        new LanguageChoice(file).Read().ShouldBe(UiLanguage.Spanish);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr")]
    [InlineData("klingon")]
    public void Something_that_names_no_language_of_this_application_reads_as_no_choice(string written)
    {
        // The only thing that writes this file is Write, so anything else in it was put there by
        // hand. Following Windows costs nothing; refusing to open over a hand-edited preference
        // would cost somebody their application.
        var file = new FileInfo(Path.Combine(_folder.FullName, "ui-language"));
        File.WriteAllText(file.FullName, written);

        new LanguageChoice(file).Read().ShouldBeNull();
    }

    [Fact]
    public void A_choice_and_windows_together_settle_what_opens()
    {
        var choice = Choice();

        // Nobody has chosen: Windows decides.
        UiLanguages.Resolve(choice.Read(), ["es-ES"]).ShouldBe(UiLanguage.Spanish);

        // Somebody chose: Windows stops mattering, and still does after a restart.
        choice.Write(UiLanguage.English);
        UiLanguages.Resolve(Choice().Read(), ["es-ES"]).ShouldBe(UiLanguage.English);
    }

    [Fact]
    public void This_users_choice_is_kept_outside_the_corpus()
    {
        // It has to be readable before there is a corpus to read it from: the window that asks
        // for one is itself a screen, and it has to be written in something.
        var location = LanguageChoice.OfThisUser().Location;

        location.FullName.ShouldStartWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        location.Name.ShouldBe("ui-language");
        location.Directory!.Name.ShouldBe("MeetingTranscriber");
    }

    public void Dispose()
    {
        _folder.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private LanguageChoice Choice() => new(new FileInfo(Path.Combine(_folder.FullName, "ui-language")));
}
