namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The recording window's line about where the corpus is names every refusal the corpus can give,
/// and substitutes for none of them.
/// </summary>
/// <remarks>
/// How this is read out of source, and why it has to be, is <see cref="EnumTable"/>'s. What is
/// here is what the answer has to be.
/// </remarks>
public class CorpusTextTests
{
    private static EnumTable Table() => EnumTable.Read(
        Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs"),
        "_corpus.Refusal",
        "CorpusRefusal",
        Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "CorpusLocation.cs"));

    [Fact]
    public void Every_refusal_the_corpus_can_give_has_a_text_on_the_recording_window()
    {
        var table = Table();
        var unnamed = table.Declared.Except(table.Named).ToArray();

        unnamed.ShouldBeEmpty(
            "MainWindow.SayWhereTheCorpusIs has no text for these refusals, so somebody "
            + "meeting one would be told the wrong reason or nothing at all: "
            + string.Join("; ", unnamed));
    }

    [Fact]
    public void The_refusals_it_names_are_ones_the_corpus_can_actually_give()
    {
        // The other direction, and not symmetry for its own sake: an arm left behind by a renamed
        // member still compiles as long as some member has that name, and reads exactly like a
        // table that is complete. Without this, the check above passes over a table half of whose
        // arms are unreachable.
        var table = Table();
        var stale = table.Named.Except(table.Declared).ToArray();

        stale.ShouldBeEmpty(
            "MainWindow.SayWhereTheCorpusIs answers for refusals CorpusRefusal does not "
            + "have: " + string.Join("; ", stale));
    }

    [Fact]
    public void A_refusal_it_has_no_text_for_stops_rather_than_being_shown_as_another_one()
    {
        // RecorderStates.Reaches is this same rule on the other table, and the reason is the same:
        // a screen that says the wrong reason confidently sends somebody to check a folder that is
        // fine. The two tables have to agree about what an unknown key does.
        //
        // The whole word and not what it starts with: `_ => throwawayText` begins with those five
        // letters and substitutes a value like any other arm.
        Table().Fallthrough.ShouldBe(
            "throw",
            customMessage: "The arm for an unknown refusal answers with a text instead of "
            + "throwing, so a refusal added later is shown to somebody as a different one.");
    }

    [Fact]
    public void There_is_a_table_and_an_enum_to_check()
    {
        // Both sides are found by pattern over source, so both can quietly find nothing — which is
        // how a file that moved reads exactly like a screen with nothing wrong in it.
        var table = Table();

        table.Declared.ShouldNotBeEmpty();
        table.Named.ShouldNotBeEmpty();
    }
}
