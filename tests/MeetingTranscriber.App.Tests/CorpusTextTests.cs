namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The settings screen's two lines about a corpus refusal — the one under the corpus setting and
/// the one a folder picker can answer with — each name every refusal the corpus can give, and
/// substitute for none of them.
/// </summary>
/// <remarks>
/// How this is read out of source, and why it has to be, is <see cref="EnumTable"/>'s. What is
/// here is what the answer has to be.
/// <para>
/// It read the recording window until #108, and it is re-pointed rather than duplicated: there is
/// still exactly one place in the application that names the folder and says which refusal it was,
/// and it moved with the line. The meetings list's own sentence sends somebody to that place, so a
/// table that fell behind there is a person told to look somewhere that says the wrong thing.
/// </para>
/// <para>
/// Two tables and not one, since #148: <c>Configuracion.SayWhereTheCorpusIs</c> reads the corpus
/// this screen was given, and <c>Configuracion.RefusalOfAPickedFolder</c> reads a folder somebody
/// just picked. Both switch on <see cref="CorpusRefusal"/> and both are held to the same rule here,
/// each over its own table, so a refusal added later cannot drift one without the other catching
/// it.
/// </para>
/// </remarks>
public class CorpusTextTests
{
    private static readonly string Screen = Path.Combine("MeetingTranscriber.App", "Configuracion.xaml.cs");

    private static readonly string DeclaredIn =
        Path.Combine("MeetingTranscriber.Infrastructure", "Storage", "CorpusLocation.cs");

    /// <summary>The expression each of the two switches is over, exactly as it is written.</summary>
    public static TheoryData<string> Tables() => ["Corpus().Refusal", "refusal"];

    private static EnumTable Table(string over) => EnumTable.Read(Screen, over, "CorpusRefusal", DeclaredIn);

    [Theory]
    [MemberData(nameof(Tables))]
    public void Every_refusal_the_corpus_can_give_has_a_text_on_the_settings_screen(string over)
    {
        var table = Table(over);
        var unnamed = table.Declared.Except(table.Named).ToArray();

        unnamed.ShouldBeEmpty(
            $"The table over '{over}' in {table.Screen} has no text for these refusals, so "
            + "somebody meeting one would be told the wrong reason or nothing at all: "
            + string.Join("; ", unnamed));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void The_refusals_it_names_are_ones_the_corpus_can_actually_give(string over)
    {
        // The other direction, and not symmetry for its own sake: an arm left behind by a renamed
        // member still compiles as long as some member has that name, and reads exactly like a
        // table that is complete. Without this, the check above passes over a table half of whose
        // arms are unreachable.
        var table = Table(over);
        var stale = table.Named.Except(table.Declared).ToArray();

        stale.ShouldBeEmpty(
            $"The table over '{over}' in {table.Screen} answers for refusals CorpusRefusal does "
            + "not have: " + string.Join("; ", stale));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_refusal_it_has_no_text_for_stops_rather_than_being_shown_as_another_one(string over)
    {
        // RecorderStates.Reaches is this same rule on the other table, and the reason is the same:
        // a screen that says the wrong reason confidently sends somebody to check a folder that is
        // fine. Every table in this application's screens has to agree about what an unknown key
        // does.
        //
        // The whole word and not what it starts with: `_ => throwawayText` begins with those five
        // letters and substitutes a value like any other arm.
        Table(over).Fallthrough.ShouldBe(
            "throw",
            customMessage: $"The arm for an unknown refusal in the table over '{over}' answers "
            + "with a text instead of throwing, so a refusal added later is shown to somebody as "
            + "a different one.");
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void There_is_a_table_and_an_enum_to_check(string over)
    {
        // Both sides are found by pattern over source, so both can quietly find nothing — which is
        // how a file that moved reads exactly like a screen with nothing wrong in it.
        var table = Table(over);

        table.Declared.ShouldNotBeEmpty();
        table.Named.ShouldNotBeEmpty();
    }
}
