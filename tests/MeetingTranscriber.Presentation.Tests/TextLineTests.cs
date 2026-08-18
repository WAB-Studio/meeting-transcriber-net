namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-152, the part a screen produces after it opened. What XAML bound re-reads itself; a result
/// or a status the screen wrote while running only does if it was kept as what it says.
/// </summary>
public class TextLineTests
{
    private static readonly UiText Finished = new("Terminado", "Finished");

    private static readonly UiText Found = new("Encontrados: {0}", "Found: {0}");

    private static readonly UiText Present = new("Está ahí: {0}", "Is there: {0}");

    private static readonly UiText Yes = new("sí", "yes");

    [Fact]
    public void A_line_written_in_one_language_reads_in_the_other()
    {
        var line = TextLine.Says(Finished);

        line.In(UiLanguage.Spanish).ShouldBe("Terminado");
        line.In(UiLanguage.English).ShouldBe("Finished");
    }

    [Fact]
    public void A_line_keeps_its_values_across_the_switch()
    {
        var line = TextLine.Says(Found, 3);

        line.In(UiLanguage.Spanish).ShouldBe("Encontrados: 3");
        line.In(UiLanguage.English).ShouldBe("Found: 3");
    }

    [Fact]
    public void A_value_that_is_itself_a_text_follows_the_line_carrying_it()
    {
        // Otherwise the sentence switches and the word inside it does not, which is a line half
        // in each language — worse than one honestly left behind.
        var line = TextLine.Says(Present, Yes);

        line.In(UiLanguage.Spanish).ShouldBe("Está ahí: sí");
        line.In(UiLanguage.English).ShouldBe("Is there: yes");
    }

    [Fact]
    public void Data_is_not_translated()
    {
        var line = TextLine.Data("  PATH=C:\\Windows");

        line.In(UiLanguage.Spanish).ShouldBe("  PATH=C:\\Windows");
        line.In(UiLanguage.English).ShouldBe(line.In(UiLanguage.Spanish));
    }

    [Fact]
    public void A_blank_line_stays_blank()
    {
        TextLine.Data(string.Empty).In(UiLanguage.English).ShouldBeEmpty();
    }

    [Fact]
    public void A_line_that_says_nothing_is_not_a_line()
    {
        Should.Throw<ArgumentNullException>(() => { _ = TextLine.Says(null!); });
        Should.Throw<ArgumentNullException>(() => { _ = TextLine.Data(null!); });
    }

    [Fact]
    public void A_whole_report_re_reads_rather_than_being_rewritten()
    {
        // What the window does when somebody picks the other language: the same lines, asked
        // again. Nothing on the list is a string that was chosen when it was written.
        List<TextLine> report =
        [
            TextLine.Says(Finished),
            TextLine.Data("  KEY=value"),
            TextLine.Says(Found, 2),
        ];

        report.Select(line => line.In(UiLanguage.Spanish))
            .ShouldBe(["Terminado", "  KEY=value", "Encontrados: 2"]);

        report.Select(line => line.In(UiLanguage.English))
            .ShouldBe(["Finished", "  KEY=value", "Found: 2"]);
    }
}
