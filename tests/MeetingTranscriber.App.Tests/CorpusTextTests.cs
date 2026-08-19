using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The recording window's line about where the corpus is names every refusal the corpus can give,
/// and substitutes for none of them.
/// </summary>
/// <remarks>
/// This reads source for the reason the whole project does — there is no
/// <c>ProjectReference</c> to the application and not by preference, so nothing here can call
/// <c>SayWhereTheCorpusIs</c> to see what it answers. What it holds instead is the one thing that
/// can go wrong without anybody noticing: <c>CorpusRefusal</c> grows a member and the screen's
/// table does not. The throw in that table is the backstop for a build this never ran over; this
/// is what stops the refusal reaching a person at all.
/// <para>
/// It is deliberately about a table falling behind its enum and not about which words each
/// refusal gets, which is <c>ScreenTextsTests</c>' half — every arm has to reach
/// <c>UiTexts</c>, and whether the entry it reaches is the apt one is a question no pattern
/// answers.
/// </para>
/// </remarks>
public partial class CorpusTextTests
{
    [Fact]
    public void Every_refusal_the_corpus_can_give_has_a_text_on_the_recording_window()
    {
        var named = Arms().Matches(TheTable()).Select(match => match.Groups["refusal"].Value).ToHashSet(StringComparer.Ordinal);

        var unnamed = Refusals().Where(refusal => !named.Contains(refusal)).ToArray();

        unnamed.ShouldBeEmpty(
            "RecordingWindow.SayWhereTheCorpusIs has no text for these refusals, so somebody "
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
        var refusals = Refusals().ToHashSet(StringComparer.Ordinal);

        var stale = Arms()
            .Matches(TheTable())
            .Select(match => match.Groups["refusal"].Value)
            .Where(refusal => !refusals.Contains(refusal))
            .ToArray();

        stale.ShouldBeEmpty(
            "RecordingWindow.SayWhereTheCorpusIs answers for refusals CorpusRefusal does not "
            + "have: " + string.Join("; ", stale));
    }

    [Fact]
    public void A_refusal_it_has_no_text_for_stops_rather_than_being_shown_as_another_one()
    {
        // RecorderStates.Reaches is this same rule on the other table, and the reason is the same:
        // a screen that says the wrong reason confidently sends somebody to check a folder that is
        // fine. The two tables have to agree about what an unknown key does.
        var fallthrough = Fallthrough().Match(TheTable());

        fallthrough.Success.ShouldBeTrue(
            "RecordingWindow.SayWhereTheCorpusIs has no arm for a refusal it does not know, which "
            + "either means the switch stopped being one or that the arm was dropped.");

        // The whole word and not what it starts with: `_ => throwawayText` begins with those five
        // letters and substitutes a value like any other arm.
        fallthrough.Groups["answer"].Value.ShouldBe(
            "throw",
            customMessage: "The arm for an unknown refusal answers with a text instead of "
            + "throwing, so a refusal added later is shown to somebody as a different one.");
    }

    [Fact]
    public void There_is_a_table_and_an_enum_to_check()
    {
        // Both sides are found by pattern over source, so both can quietly find nothing — which is
        // how a file that moved reads exactly like a screen with nothing wrong in it.
        Refusals().ShouldNotBeEmpty();
        Arms().Matches(TheTable()).ShouldNotBeEmpty();
    }

    /// <summary>The switch that turns a refusal into what the window says.</summary>
    private static string TheTable()
    {
        var window = File.ReadAllText(Source(Path.Combine("MeetingTranscriber.App", "RecordingWindow.xaml.cs")));
        var table = Table().Match(window);

        table.Success.ShouldBeTrue(
            "RecordingWindow.xaml.cs no longer holds a `_corpus.Refusal switch`, so this test is "
            + "reading nothing. Whatever replaced it needs holding to the same rule.");

        return table.Value;
    }

    /// <summary>Every member of <c>CorpusRefusal</c>, read where it is declared.</summary>
    private static IReadOnlyList<string> Refusals()
    {
        var declared = File.ReadAllText(Source(Path.Combine(
            "MeetingTranscriber.Infrastructure", "Storage", "CorpusLocation.cs")));

        var enumeration = Enumeration().Match(declared);

        enumeration.Success.ShouldBeTrue("CorpusLocation.cs no longer declares `enum CorpusRefusal`.");

        return [.. Member().Matches(enumeration.Groups["body"].Value).Select(match => match.Groups["name"].Value)];
    }

    private static string Source(string relative, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src", relative));

    /// <summary>
    /// The switch itself, from the refusal it is over to the brace that closes it. Anchored on the
    /// closing `};` at the switch's own indentation so an arm spanning lines stays inside it.
    /// </summary>
    [GeneratedRegex(@"_corpus\.Refusal switch\r?\n\s*\{.*?\r?\n[ ]*\};", RegexOptions.Singleline)]
    private static partial Regex Table();

    /// <summary>An arm naming a refusal.</summary>
    /// <remarks>
    /// Anchored to the start of its line, which is what tells an arm from a comment about one:
    /// `// CorpusRefusal.GoesWhenThePackageDoes => ...` left behind by somebody mid-edit would
    /// otherwise read as a live arm, and the refusal it stopped answering would reach the throw.
    /// Cheaper than parsing C# and enough for the one thing here that is not code.
    /// </remarks>
    [GeneratedRegex(@"^[ ]*CorpusRefusal\.(?<refusal>\w+)\s*=>", RegexOptions.Multiline)]
    private static partial Regex Arms();

    /// <summary>The arm for anything the table does not name, and what it answers with.</summary>
    /// <remarks>Anchored for the reason <see cref="Arms"/> is, and the same worked example.</remarks>
    [GeneratedRegex(@"^[ ]*_\s*=>\s*(?<answer>\w+)", RegexOptions.Multiline)]
    private static partial Regex Fallthrough();

    [GeneratedRegex(@"enum CorpusRefusal\r?\n\{(?<body>.*?)\r?\n\}", RegexOptions.Singleline)]
    private static partial Regex Enumeration();

    /// <summary>
    /// A member of that enum: a name at the enum's own indentation, whatever it is or is not given
    /// as a value.
    /// </summary>
    /// <remarks>
    /// The value is optional and unread on purpose. `CorpusRefusal` numbers its members today, and
    /// a fifth added without a number, or as `0x5`, or as an expression, is a member this has to
    /// see — a pattern that only knew `= 4,` would leave it out, and leaving it out is exactly the
    /// green run this test exists to prevent. What carries the check instead is the indentation:
    /// four spaces then a word, which a doc comment line cannot be because it starts with `/`.
    /// <para>
    /// The `\r?` before the end of the line is load-bearing and this repo is checked out CRLF:
    /// `$` under <see cref="RegexOptions.Multiline"/> matches before the `\n` and leaves the `\r`
    /// to be matched, so a pattern ending `[ ]*$` silently finds only the last member — the one
    /// whose `\r` the enum body was cut on.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"^[ ]{4}(?<name>\w+)[ ]*(?:=[^,\r\n]*)?,?[ ]*\r?$", RegexOptions.Multiline)]
    private static partial Regex Member();
}
