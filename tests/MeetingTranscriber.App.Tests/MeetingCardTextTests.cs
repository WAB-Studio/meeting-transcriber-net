using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-82's half that lives on the screen, and the same rule over a recording nobody got to stop:
/// a meeting's card names every stage a meeting can be at, every standing that stage can be in and
/// every standing such a recording can be in, and substitutes for none of them.
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
    /// The tables held to naming their whole enum, by the enum's name.
    /// </summary>
    /// <remarks>
    /// The lookup throws on a name it does not have rather than falling back, because a table
    /// added to the data and not to the map would otherwise check another one twice and report a
    /// green test for each.
    /// </remarks>
    private static readonly Dictionary<string, Func<EnumTable>> Held = new(StringComparer.Ordinal)
    {
        ["MeetingStage"] = Stages,
        ["StageStanding"] = Standings,
        ["WaitingStanding"] = Waitings,
    };

    public static TheoryData<string> Tables() => [.. Held.Keys];

    [Fact]
    public void There_is_a_table_over_every_kind_a_card_offers()
    {
        // Both sides are found by pattern over source, so both can quietly find nothing — which is
        // how a file that moved reads exactly like a screen with nothing wrong in it. The other
        // two tables say this for themselves, inside the check below.
        var offered = Actions();

        offered.Declared.ShouldNotBeEmpty();
        offered.Named.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every stage a meeting can be at and every standing that stage can be in has a word on the
    /// card, the card answers for nothing a meeting cannot be, and one it has no word for stops
    /// rather than reading as another.
    /// </summary>
    /// <remarks>
    /// The three are one call on <see cref="EnumTable"/> rather than three theories here, because
    /// a second screen came to want the same three and copied this class to get them — the harness
    /// and its comments, not the mechanism. What is this class's is which tables exist and what
    /// they are over.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Tables))]
    public void Every_stage_and_every_standing_has_a_word_on_the_card(string enumeration) =>
        Held[enumeration]().ShouldNameItsWholeEnum("MeetingsDrawer", enumeration);

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

    /// <summary>
    /// Every surface the waiting table names is a style the screen actually declares.
    /// </summary>
    /// <remarks>
    /// The other half of that table, and the half no enum check reaches: it answers a resource key
    /// as a string, and the screen looks it up by name at the moment it draws a card. A key
    /// renamed in the markup and not here is a lookup that throws out of a draw, on a list nothing
    /// but a running window builds — so it is read out of the two files instead.
    /// </remarks>
    [Fact]
    public void Every_surface_a_waiting_recording_sits_on_is_one_the_screen_declares()
    {
        var named = Regex.Matches(
                File.ReadAllText(AppSources.At(Screen).FullName),
                @"WaitingStanding\.\w+ => \([^)]*""(?<surface>\w+)""\)")
            .Select(match => match.Groups["surface"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = Regex.Matches(
                File.ReadAllText(AppSources.At(
                    Path.Combine("MeetingTranscriber.App", "MeetingsDrawer.xaml")).FullName),
                @"x:Key=""(?<key>\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        named.ShouldNotBeEmpty("MeetingsDrawer.xaml.cs names no surface, so this check reads nothing.");
        declared.ShouldNotBeEmpty("MeetingsDrawer.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "MeetingsDrawer.xaml declares no style by these names, so a waiting recording's card "
            + "throws where it is drawn.");
    }

    private static EnumTable Stages() => EnumTable.Read(Screen, "stage", "MeetingStage", StagesDeclaredIn);

    private static EnumTable Standings() =>
        EnumTable.Read(Screen, "standing", "StageStanding", StagesDeclaredIn);

    /// <summary>
    /// The waiting recordings' table. It answers two things at once — the sentence and the
    /// surface it sits on — because a row saying it is waiting on somebody over a surface that
    /// says it is not would be the screen contradicting itself. A standing added to the rule and
    /// not here is a recording drawn as another one, which is the whole reason to read it.
    /// </summary>
    private static EnumTable Waitings() => EnumTable.Read(
        Screen,
        "waiting",
        "WaitingStanding",
        Path.Combine("MeetingTranscriber.Recording", "WaitingRows.cs"));

    private static EnumTable Actions() => EnumTable.Read(
        Screen, "kind", "JobKind", Path.Combine("MeetingTranscriber.Domain", "Jobs", "JobKind.cs"));
}
