using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// A switch on a screen that turns a member of an enum into something a person reads, and the
/// enum it is over — both read out of source so they can be held to agreeing.
/// </summary>
/// <remarks>
/// <para>
/// This reads source for the reason the whole project does — there is no <c>ProjectReference</c>
/// to the application and not by preference, so nothing here can call one of these switches to see
/// what it answers. What it holds instead is the one thing that can go wrong without anybody
/// noticing: the enum grows a member and the screen's table does not. The throw in each table is
/// the backstop for a build this never ran over; this is what stops the member reaching a person
/// at all.
/// </para>
/// <para>
/// It is deliberately about a table falling behind its enum and not about which words each member
/// gets, which is <see cref="ScreenTextsTests"/>' half — every arm has to reach <c>UiTexts</c>, and
/// whether the entry it reaches is the apt one is a question no pattern answers.
/// </para>
/// <para>
/// One mechanism and not one per screen. Two tables are already held to this rule, they are held to
/// it for the same reason, and the regexes below are where every worked example of what a pattern
/// over C# gets wrong is written down. A second copy of them is a second place for those examples
/// to be forgotten in.
/// </para>
/// </remarks>
internal sealed partial class EnumTable
{
    private EnumTable(IReadOnlyList<string> named, IReadOnlyList<string> declared, string? fallthrough)
    {
        Named = named;
        Declared = declared;
        Fallthrough = fallthrough;
    }

    /// <summary>The members the table has an arm for.</summary>
    public IReadOnlyList<string> Named { get; }

    /// <summary>The members the enum actually declares.</summary>
    public IReadOnlyList<string> Declared { get; }

    /// <summary>
    /// The first word the arm for anything the table does not name answers with, or nothing when
    /// there is no such arm.
    /// </summary>
    public string? Fallthrough { get; }

    /// <summary>
    /// Reads one table and the enum it is over.
    /// </summary>
    /// <param name="screen">The source file holding the switch, relative to <c>src/</c>.</param>
    /// <param name="over">The expression the switch is on, exactly as it is written.</param>
    /// <param name="enumeration">The enum's name.</param>
    /// <param name="declaredIn">The source file declaring the enum, relative to <c>src/</c>.</param>
    public static EnumTable Read(string screen, string over, string enumeration, string declaredIn)
    {
        var source = File.ReadAllText(Source(screen));
        var name = Path.GetFileName(screen);

        var table = Regex.Match(source, $@"{Regex.Escape(over)} switch\r?\n\s*\{{.*?\r?\n[ ]*\}};", RegexOptions.Singleline);

        table.Success.ShouldBeTrue(
            $"{name} no longer holds a `{over} switch`, so this test is reading nothing. Whatever "
            + "replaced it needs holding to the same rule.");

        var fallthrough = Fallthroughs().Match(table.Value);

        return new EnumTable(
            [.. Regex.Matches(table.Value, $@"^[ ]*{Regex.Escape(enumeration)}\.(?<name>\w+)\s*=>", RegexOptions.Multiline)
                .Select(match => match.Groups["name"].Value)],
            Members(enumeration, declaredIn),
            fallthrough.Success ? fallthrough.Groups["answer"].Value : null);
    }

    /// <summary>Every member of an enum, read where it is declared.</summary>
    private static IReadOnlyList<string> Members(string enumeration, string declaredIn)
    {
        var declared = File.ReadAllText(Source(declaredIn));

        var body = Regex.Match(declared, $@"enum {Regex.Escape(enumeration)}\r?\n\{{(?<body>.*?)\r?\n\}}", RegexOptions.Singleline);

        body.Success.ShouldBeTrue($"{Path.GetFileName(declaredIn)} no longer declares `enum {enumeration}`.");

        return [.. Member().Matches(body.Groups["body"].Value).Select(match => match.Groups["name"].Value)];
    }

    private static string Source(string relative, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "src", relative));

    /// <summary>The arm for anything the table does not name, and what it answers with.</summary>
    /// <remarks>
    /// Anchored to the start of its line, which is what tells an arm from a comment about one:
    /// `// _ => something` left behind by somebody mid-edit would otherwise read as a live arm.
    /// Cheaper than parsing C# and enough for the one thing here that is not code.
    /// </remarks>
    [GeneratedRegex(@"^[ ]*_\s*=>\s*(?<answer>\w+)", RegexOptions.Multiline)]
    private static partial Regex Fallthroughs();

    /// <summary>
    /// A member of an enum: a name at the enum's own indentation, whatever it is or is not given
    /// as a value.
    /// </summary>
    /// <remarks>
    /// The value is optional and unread on purpose. A member added without a number, or as `0x5`,
    /// or as an expression, is a member this has to see — a pattern that only knew `= 4,` would
    /// leave it out, and leaving it out is exactly the green run this exists to prevent. What
    /// carries the check instead is the indentation: four spaces then a word, which a doc comment
    /// line cannot be because it starts with `/`.
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
