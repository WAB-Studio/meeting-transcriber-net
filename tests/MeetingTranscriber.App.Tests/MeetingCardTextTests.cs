namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-82's half that lives on the screen: a meeting's card names every stage a meeting can be at
/// and every standing that stage can be in, and substitutes for none of them.
/// </summary>
/// <remarks>
/// <para>
/// The rule itself is `MeetingStageTests` and `MeetingWorkTests`, which run. What no probe of
/// theirs can reach is whether the window has a word for what they answer, and a stage with no
/// word is a meeting that says the wrong thing about itself — one with no audio reading as one
/// ready to be paid for.
/// </para>
/// <para>
/// How the tables are read out of source, and why they have to be, is <see cref="EnumTable"/>'s.
/// </para>
/// </remarks>
public class MeetingCardTextTests
{
    private static readonly string Screen = Path.Combine("MeetingTranscriber.App", "MeetingsDrawer.xaml.cs");

    private static readonly string StagesDeclaredIn =
        Path.Combine("MeetingTranscriber.Domain", "Meetings", "MeetingStage.cs");

    /// <summary>
    /// The two tables held to naming their whole enum, by the enum's name.
    /// </summary>
    /// <remarks>
    /// The lookup throws on a name it does not have rather than falling back, because a third
    /// table added to the data and not to the map would otherwise check the second one twice and
    /// report three green tests.
    /// </remarks>
    private static readonly Dictionary<string, Func<EnumTable>> Held = new(StringComparer.Ordinal)
    {
        ["MeetingStage"] = Stages,
        ["StageStanding"] = Standings,
    };

    public static TheoryData<string> Tables() => [.. Held.Keys];

    [Fact]
    public void There_are_tables_and_enums_to_check()
    {
        // Both sides of each are found by pattern over source, so both can quietly find nothing —
        // which is how a file that moved reads exactly like a screen with nothing wrong in it.
        foreach (var table in new[] { Stages(), Standings(), Actions() })
        {
            table.Declared.ShouldNotBeEmpty();
            table.Named.ShouldNotBeEmpty();
        }
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void Every_stage_and_every_standing_has_a_word_on_the_card(string enumeration)
    {
        var table = Held[enumeration]();
        var unnamed = table.Declared.Except(table.Named).ToArray();

        unnamed.ShouldBeEmpty(
            $"MeetingsDrawer has no text for these members of {enumeration}, so a meeting at one "
            + "of them says the wrong thing about itself or nothing at all: "
            + string.Join("; ", unnamed));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void What_the_card_names_is_something_a_meeting_can_actually_be(string enumeration)
    {
        // An arm left behind by a renamed member still compiles as long as some member has that
        // name, and reads exactly like a table that is complete.
        var table = Held[enumeration]();
        var stale = table.Named.Except(table.Declared).ToArray();

        stale.ShouldBeEmpty(
            $"MeetingsDrawer answers for members {enumeration} does not have: " + string.Join("; ", stale));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void A_stage_or_standing_it_has_no_word_for_stops_rather_than_reading_as_another(string enumeration)
    {
        var table = Held[enumeration]();

        table.Fallthrough.ShouldBe(
            "throw",
            customMessage: $"MeetingsDrawer's table over {enumeration} answers an unknown member "
            + "with a text instead of throwing, so a member added later is shown to somebody as a "
            + "different one.");
    }

    [Fact]
    public void The_button_is_only_ever_offered_for_work_that_spends_something()
    {
        // The card's second sentence, on the screen's side of it. The rendered files cost nothing
        // and can be made again, so they are never a press — and a window that had a word for a
        // render button would be one edit away from showing it.
        //
        // Which kinds a stage can actually offer is pinned in MeetingStageTests, over the ladder
        // itself. This pins the same pair here, so a rung added there and not here fails on both
        // sides rather than reaching the throw on somebody's screen.
        var offered = Actions();

        offered.Named.ShouldBe(["Transcribe", "Extract"], ignoreOrder: true);
        offered.Named.Except(offered.Declared).ShouldBeEmpty();
        offered.Fallthrough.ShouldBe("throw");
    }

    private static EnumTable Stages() => EnumTable.Read(Screen, "stage", "MeetingStage", StagesDeclaredIn);

    private static EnumTable Standings() =>
        EnumTable.Read(Screen, "standing", "StageStanding", StagesDeclaredIn);

    private static EnumTable Actions() => EnumTable.Read(
        Screen, "kind", "JobKind", Path.Combine("MeetingTranscriber.Domain", "Jobs", "JobKind.cs"));
}
