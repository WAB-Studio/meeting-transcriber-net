namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-153. What the application opens in, and the one thing that decides it.
/// </summary>
public class UiLanguagesTests
{
    [Fact]
    public void Follows_windows_when_nobody_has_chosen()
    {
        UiLanguages.Resolve(chosen: null, ["es-AR"]).ShouldBe(UiLanguage.Spanish);
        UiLanguages.Resolve(chosen: null, ["en-US"]).ShouldBe(UiLanguage.English);
    }

    [Fact]
    public void Reads_the_language_and_not_the_region()
    {
        // The application is written in Spanish and English, not in es-AR and en-GB. A Windows
        // set to any Spanish is a Windows set to Spanish.
        foreach (var tag in new[] { "es", "es-ES", "es-419", "es-MX" })
        {
            UiLanguages.Resolve(chosen: null, [tag]).ShouldBe(UiLanguage.Spanish);
        }
    }

    [Fact]
    public void Takes_the_first_of_windows_languages_it_is_written_in()
    {
        // The list is in the order the user put it in: Galician first is not a reason to skip
        // past Spanish to English.
        UiLanguages.Resolve(chosen: null, ["gl-ES", "es-ES", "en-US"]).ShouldBe(UiLanguage.Spanish);
        UiLanguages.Resolve(chosen: null, ["fr-FR", "en-GB", "es-ES"]).ShouldBe(UiLanguage.English);
    }

    [Fact]
    public void Opens_in_english_when_windows_speaks_neither()
    {
        UiLanguages.Resolve(chosen: null, ["fr-FR", "de-DE"]).ShouldBe(UiLanguage.English);
        UiLanguages.Resolve(chosen: null, []).ShouldBe(UiLanguage.English);
    }

    [Fact]
    public void A_choice_beats_windows()
    {
        // The whole of the "unless somebody chose another": Windows saying Spanish loudly and
        // twice does not move an application somebody set to English.
        UiLanguages.Resolve(UiLanguage.English, ["es-ES", "es-AR"]).ShouldBe(UiLanguage.English);
        UiLanguages.Resolve(UiLanguage.Spanish, ["en-US", "en-GB"]).ShouldBe(UiLanguage.Spanish);
    }

    [Fact]
    public void A_tag_that_names_no_language_of_this_application_names_none()
    {
        UiLanguages.Parse("fr-FR").ShouldBeNull();
        UiLanguages.Parse("").ShouldBeNull();
        UiLanguages.Parse("   ").ShouldBeNull();
        UiLanguages.Parse("-").ShouldBeNull();
        UiLanguages.Parse(null).ShouldBeNull();
    }

    [Fact]
    public void Every_language_survives_being_written_down_and_read_back()
    {
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            UiLanguages.Parse(UiLanguages.Tag(language)).ShouldBe(language);
            UiLanguages.Parse(UiLanguages.Tag(language).ToUpperInvariant()).ShouldBe(language);
        }
    }

    [Fact]
    public void Every_language_is_named_in_itself()
    {
        UiLanguages.Endonym(UiLanguage.Spanish).Spanish.ShouldBe("Español");
        UiLanguages.Endonym(UiLanguage.English).English.ShouldBe("English");

        // Never equal, or the picker would offer the same thing twice.
        Enum.GetValues<UiLanguage>()
            .Select(language => UiLanguages.Endonym(language).Spanish)
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(Enum.GetValues<UiLanguage>().Length);
    }

    [Fact]
    public void A_languages_name_reads_the_same_whatever_the_application_is_being_read_in()
    {
        // The one place a switch deliberately leaves a word where it was, and it is a claim
        // rather than an oversight: somebody who opened the application in a language they cannot
        // read finds their way out by the one word on screen they recognise. Stated in the
        // catalogue, so the walk over it sees these two like every other word a person reads.
        foreach (var named in Enum.GetValues<UiLanguage>())
        {
            foreach (var reading in Enum.GetValues<UiLanguage>())
            {
                UiLanguages.Endonym(named).In(reading).ShouldBe(UiLanguages.Endonym(named).Spanish);
            }
        }
    }

    [Fact]
    public void Every_language_writes_its_own_numbers()
    {
        UiLanguages.Culture(UiLanguage.Spanish).TwoLetterISOLanguageName.ShouldBe("es");
        UiLanguages.Culture(UiLanguage.English).TwoLetterISOLanguageName.ShouldBe("en");
    }
}
