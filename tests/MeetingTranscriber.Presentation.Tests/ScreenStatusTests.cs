namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// The engine five screens now share for saying one line about what happened around them, and a
/// sixth is written against from the start.
/// </summary>
public class ScreenStatusTests
{
    private static readonly UiText Finished = new("Terminado", "Finished");

    private static readonly UiText Failed = new("Falló: {0}", "Failed: {0}");

    [Fact]
    public void A_line_says_the_same_thing_in_whichever_language_it_is_read()
    {
        var status = new ScreenStatus();
        status.Says(Failed, "algo");

        status.In(UiLanguage.Spanish).ShouldBe("Falló: algo");
        status.In(UiLanguage.English).ShouldBe("Failed: algo");
    }

    [Fact]
    public void Saying_nothing_reads_as_nothing_and_not_as_a_blank_sentence()
    {
        var status = new ScreenStatus();

        status.IsSaying.ShouldBeFalse();
        status.In(UiLanguage.Spanish).ShouldBe(string.Empty);
    }

    [Fact]
    public void Something_new_goes_over_whatever_was_being_said()
    {
        var status = new ScreenStatus();
        status.Says(Finished);
        status.Says(Failed, "otra cosa");

        status.In(UiLanguage.Spanish).ShouldBe("Falló: otra cosa");
    }

    [Fact]
    public void What_was_said_goes_back_only_where_nothing_is_being_said()
    {
        var said = TextLine.Says(Finished);

        var cleared = new ScreenStatus();
        cleared.Nothing();
        cleared.KeepsWhatWasSaid(said);
        cleared.In(UiLanguage.Spanish).ShouldBe("Terminado");

        var stillSaying = new ScreenStatus();
        stillSaying.Says(Failed, "algo");
        stillSaying.KeepsWhatWasSaid(said);
        stillSaying.In(UiLanguage.Spanish).ShouldBe("Falló: algo");
    }

    [Fact]
    public void Keeping_nothing_leaves_a_screen_saying_nothing()
    {
        var status = new ScreenStatus();
        status.KeepsWhatWasSaid(null);

        status.IsSaying.ShouldBeFalse();
    }
}
