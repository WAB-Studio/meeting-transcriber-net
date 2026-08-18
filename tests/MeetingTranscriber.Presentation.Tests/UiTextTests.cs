using System.Globalization;

namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-152, at the type. A text that exists in one language and not the other is what this is
/// here to make unbuildable — the catalogue is only safe because nothing in it can be.
/// </summary>
public class UiTextTests
{
    [Fact]
    public void A_text_says_what_the_language_being_read_in_says()
    {
        var text = new UiText("Grabar", "Record");

        text.In(UiLanguage.Spanish).ShouldBe("Grabar");
        text.In(UiLanguage.English).ShouldBe("Record");
    }

    [Theory]
    [InlineData(null, "Record")]
    [InlineData("Grabar", null)]
    [InlineData("", "Record")]
    [InlineData("Grabar", "   ")]
    public void A_text_missing_a_language_is_not_a_text(string? spanish, string? english) =>
        Should.Throw<ArgumentException>(() => { _ = new UiText(spanish!, english!); });

    [Fact]
    public void Both_versions_leave_room_for_the_same_values()
    {
        // The failure this stops: a line that reads fine until somebody switches language, and
        // then either loses the value or throws mid-screen.
        Should.Throw<ArgumentException>(() => { _ = new UiText("{0} reuniones", "meetings"); });
        Should.Throw<ArgumentException>(() => { _ = new UiText("{0} de {1}", "{0} of them"); });

        Should.NotThrow(() => { _ = new UiText("{1} de {0}", "{0}'s {1}"); });
        Should.NotThrow(() => { _ = new UiText("{0} y {0}", "{0} and {0}"); });
    }

    [Fact]
    public void A_version_the_formatter_cannot_read_is_refused_where_it_was_written()
    {
        // Counting the values a version asks for says nothing about whether it is a format string
        // at all: `"Valor: {0"` asks for none by that reading, matches an English version that
        // also asks for none, and would throw the first time somebody read the screen.
        Should.Throw<ArgumentException>(() => { _ = new UiText("Valor: {0", "Value: {0"); });
        Should.Throw<ArgumentException>(() => { _ = new UiText("Listo}", "Done}"); });
        Should.Throw<ArgumentException>(() => { _ = new UiText("{0} de {1", "{0} of {1"); });
    }

    [Fact]
    public void An_escaped_brace_is_not_a_value()
    {
        Should.NotThrow(() => { _ = new UiText("{{sin valores}}", "{{no values}}"); });
    }

    [Fact]
    public void Values_are_put_in_the_way_that_language_writes_them()
    {
        var text = new UiText("Duración: {0:N2} s", "Length: {0:N2} s");

        // Not a flourish: 1.5 read as "1,5" by somebody and as "1.5" by the same person in the
        // other language is the same number, and 1,500 in one is 1.5 in the other if it is not.
        text.In(UiLanguage.Spanish, 1.5).ShouldBe("Duración: 1,50 s");
        text.In(UiLanguage.English, 1.5).ShouldBe("Length: 1.50 s");
    }

    [Fact]
    public void A_text_with_no_room_for_values_reads_the_same_either_way()
    {
        var text = new UiText("Listo", "Done");

        text.In(UiLanguage.Spanish).ShouldBe(text.In(UiLanguage.Spanish, []));
    }

    [Fact]
    public void The_two_versions_of_a_text_are_told_apart()
    {
        var text = new UiText("Sí", "Yes");

        text.In(UiLanguage.Spanish).ShouldNotBe(text.In(UiLanguage.English));
        text.Spanish.ShouldBe("Sí");
        text.English.ShouldBe("Yes");
    }

    [Fact]
    public void A_language_that_does_not_exist_is_refused()
    {
        var text = new UiText("Listo", "Done");

        Should.Throw<ArgumentOutOfRangeException>(() => { _ = text.In((UiLanguage)42); });
        Should.Throw<ArgumentOutOfRangeException>(() => { _ = UiLanguages.Tag((UiLanguage)42); });
        Should.Throw<ArgumentOutOfRangeException>(() => { _ = UiLanguages.Endonym((UiLanguage)42); });
    }

    [Fact]
    public void The_culture_a_text_is_formatted_with_is_the_language_and_not_the_machines()
    {
        // The test host's own culture has no say: a Spanish text written on an English machine
        // still writes Spanish numbers.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            new UiText("{0:N1}", "{0:N1}").In(UiLanguage.Spanish, 2.5).ShouldBe("2,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
