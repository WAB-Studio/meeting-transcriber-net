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
    private EnumTable(
        string screen,
        IReadOnlyDictionary<string, string> answers,
        IReadOnlyList<string> declared,
        string? fallthrough)
    {
        Screen = screen;
        Answers = answers;
        Declared = declared;
        Fallthrough = fallthrough;
    }

    /// <summary>
    /// The file the table was read out of, which is where a failure has to send somebody. Kept here
    /// rather than named again by whoever asserts: the path is already an argument to
    /// <see cref="Read"/>, and a second hand-written copy of it goes stale on its own — as one did,
    /// reporting every table as <c>MeetingWords</c> while two of them were on the drawer.
    /// </summary>
    public string Screen { get; }

    /// <summary>
    /// What each arm answers with, by the member it answers for — the rest of the line after its
    /// <c>=&gt;</c>, whatever shape that is.
    /// </summary>
    /// <remarks>
    /// Here rather than in whoever wants it, and it is why the arms are read with one pattern
    /// instead of one per question. A second pattern over the same switch is a second place for the
    /// worked examples below to be forgotten in — that is what this class is for — and it is also a
    /// pattern with no <c>{over} switch</c> around it, so it would go on finding arms after the
    /// table it was written for stopped existing.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Answers { get; }

    /// <summary>The members the table has an arm for.</summary>
    public IReadOnlyList<string> Named => [.. Answers.Keys];

    /// <summary>The members the enum actually declares.</summary>
    public IReadOnlyList<string> Declared { get; }

    /// <summary>
    /// The first word the arm for anything the table does not name answers with, or nothing when
    /// there is no such arm.
    /// </summary>
    public string? Fallthrough { get; }

    /// <summary>
    /// The three things every one of these tables has to be true of, in one call.
    /// </summary>
    /// <remarks>
    /// Here for the reason the reading below is: one mechanism and not one per screen. The
    /// assertions were three theories on a class, and a second screen wanting them copied the
    /// class rather than the mechanism — the harness, the wording and a verbatim comment. What
    /// each table is over stays with the screen that declares it; what has to be true of all of
    /// them is this.
    /// <para>
    /// The three are one call and not three because they fail together and mean one thing: a table
    /// that has fallen behind the enum it is over. Split, the second and third are read as
    /// separate faults and one of them gets waved through as a consequence of the first.
    /// </para>
    /// </remarks>
    /// <param name="over">What the table is a table of, said the way somebody would say it.</param>
    public void ShouldNameItsWholeEnum(string over)
    {
        Declared.ShouldNotBeEmpty(
            $"nothing was found declaring {over}, so this check is reading nothing.");
        Named.ShouldNotBeEmpty(
            $"{Screen} was found holding no table over {over}, so this check is reading nothing.");

        Declared.Except(Named).ShouldBeEmpty(
            $"{Screen} has no answer for these members of {over}, so one of them is shown to "
            + "somebody as another or as nothing at all.");

        Named.Except(Declared).ShouldBeEmpty(
            $"{Screen} answers for members {over} does not have, so an arm left behind by a rename "
            + "is reading as a table that is complete.");

        Fallthrough.ShouldBe(
            "throw",
            customMessage: $"{Screen}'s table over {over} answers an unknown member with something "
            + "rather than throwing, so a member added later is shown to somebody as a different "
            + "one.");
    }

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

        // Every table over that expression, and not the first one. `Match` — singular — meant a
        // file could hold only one switch per variable name, and what a file gets for holding two
        // is not a failure that says so: the second read finds the first table, no arms for the
        // enum it asked about, and a message claiming the screen has no table at all. That is a
        // limitation of this reader, and it was about to be paid for by a rule on every future
        // screen — "no two switches in one file may share a variable name", enforced by a test of
        // its own. One word here is the whole of it instead.
        var tables = Regex.Matches(
            source,
            $@"{Regex.Escape(over)} switch\r?\n\s*\{{.*?\r?\n[ ]*\}};",
            RegexOptions.Singleline);

        tables.Count.ShouldBeGreaterThan(
            0,
            $"{name} no longer holds a `{over} switch`, so this test is reading nothing. Whatever "
            + "replaced it needs holding to the same rule.");

        // Distinct before the dictionary, and only so that a repeated member is a failed assertion
        // rather than a crash inside a collection. C# refuses two arms for one constant, so the
        // reading that would produce one is a pattern that has gone wrong, not a screen that has.
        var read = tables
            .Select(table => (Table: table, Answers: Arms(table.Value, enumeration)))
            .ToArray();

        // The one that answers for this enum. Where none of them does — the table was renamed, or
        // the enum was — the last is taken, so what comes back is an empty table over a real switch
        // and `ShouldNameItsWholeEnum` reports the members that have no answer rather than passing.
        var found = read.FirstOrDefault(table => table.Answers.Count > 0, read[^1]);
        var fallthrough = Fallthroughs().Match(found.Table.Value);

        return new EnumTable(
            name,
            found.Answers,
            Members(enumeration, declaredIn),
            fallthrough.Success ? fallthrough.Groups["answer"].Value : null);
    }

    /// <summary>What one table answers for each member of <paramref name="enumeration"/>.</summary>
    private static Dictionary<string, string> Arms(string table, string enumeration) => Regex
        .Matches(table, $@"^[ ]*{Regex.Escape(enumeration)}\.(?<name>\w+)\s*=>(?<answer>[^\r\n]*)", RegexOptions.Multiline)
        .DistinctBy(arm => arm.Groups["name"].Value, StringComparer.Ordinal)
        .ToDictionary(
            arm => arm.Groups["name"].Value,
            arm => arm.Groups["answer"].Value.Trim(),
            StringComparer.Ordinal);

    /// <summary>Every member of an enum, read where it is declared.</summary>
    private static IReadOnlyList<string> Members(string enumeration, string declaredIn)
    {
        var declared = File.ReadAllText(Source(declaredIn));

        var body = Regex.Match(declared, $@"enum {Regex.Escape(enumeration)}\r?\n\{{(?<body>.*?)\r?\n\}}", RegexOptions.Singleline);

        body.Success.ShouldBeTrue($"{Path.GetFileName(declaredIn)} no longer declares `enum {enumeration}`.");

        return [.. Member().Matches(body.Groups["body"].Value).Select(match => match.Groups["name"].Value)];
    }

    /// <summary>One file under <c>src/</c>, wherever this repo is checked out.</summary>
    /// <remarks>
    /// Asked of <see cref="AppSources"/> rather than worked out here. Two derivations of the same
    /// path in one project are two things to correct when the layout moves, and only one of them
    /// would be found.
    /// </remarks>
    private static string Source(string relative) => AppSources.At(relative).FullName;

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
