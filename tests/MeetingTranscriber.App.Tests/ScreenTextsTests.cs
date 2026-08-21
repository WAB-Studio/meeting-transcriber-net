using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-152, over the screens. The catalogue being complete says nothing about whether the screens
/// use it: a literal typed straight into a window exists in one language, and no walk of the
/// catalogue can see it. This reads the screens themselves.
/// </summary>
/// <remarks>
/// It reads source rather than running a window on purpose. A WinUI tree needs a UI thread and a
/// packaged host, neither of which a build agent has, so the check that would need one is the
/// check that would never run.
/// <para>
/// What it reaches is what a screen writes down: an attribute, an element's own text, and a word
/// assigned to a property a person reads. What it does not reach is a word handed to a method —
/// `Show(&quot;Done&quot;)` — because telling that from `Path.Combine(&quot;Local&quot;, ...)` is
/// a question about types, not about text, and the tool for it is an analyser rather than a
/// longer pattern. That gap is named here rather than papered over.
/// </para>
/// </remarks>
public partial class ScreenTextsTests
{
    /// <summary>
    /// The properties that put words in front of a person. Each has to name an entry in the
    /// catalogue and never carry the words itself.
    /// </summary>
    private static readonly HashSet<string> Reads =
    [
        "AutomationProperties.HelpText",
        "AutomationProperties.Name",
        "CloseButtonText",
        "Content",
        "Description",
        "Header",
        "PlaceholderText",
        "PrimaryButtonText",
        "SecondaryButtonText",
        "Text",
        "Title",
        "ToolTipService.ToolTip",
    ];

    public static TheoryData<string> Screens() => [.. AppSources.With(".xaml").Select(file => file.FullName)];

    public static TheoryData<string> CodeBehind() => [.. AppSources.With(".cs").Select(file => file.FullName)];

    [Fact]
    public void There_are_screens_to_check()
    {
        // Without this the three below pass by finding nothing, which is how a path that stopped
        // resolving reads exactly like a codebase with nothing wrong in it.
        AppSources.With(".xaml").ShouldNotBeEmpty();
        AppSources.With(".cs").ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_names_anything_but_an_entry_in_the_catalogue(string path)
    {
        // `{x:Bind ...}` and nothing else, deliberately narrower than "some markup extension":
        // `{StaticResource Greeting}` is a literal one indirection away, and a resource holding
        // one language's words would sit behind a check that had waved it through. The day a
        // second form is legitimate, this is one edit and the edit is somebody's decision.
        var carried = XDocument
            .Load(path)
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Where(attribute => Reads.Contains(attribute.Name.LocalName))
            .Where(attribute => !attribute.Value.TrimStart().StartsWith("{x:Bind", StringComparison.Ordinal))
            .Select(attribute => $"{attribute.Name.LocalName}=\"{attribute.Value}\"")
            .ToArray();

        carried.ShouldBeEmpty(
            $"{Path.GetFileName(path)} says these itself instead of binding an entry of UiTexts, "
            + "so they exist in one language: " + string.Join("; ", carried));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_writes_words_between_its_tags(string path)
    {
        // The same literal as above wearing the other syntax: `<TextBlock>Listo</TextBlock>` sets
        // the very property the attribute would have, and no attribute check can see it.
        var written = XDocument
            .Load(path)
            .Descendants()
            .SelectMany(element => element.Nodes().OfType<XText>().Select(text => (element, text)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.text.Value))
            .Select(pair => $"<{pair.element.Name.LocalName}>{pair.text.Value.Trim()}")
            .ToArray();

        written.ShouldBeEmpty(
            $"{Path.GetFileName(path)} writes words between its tags instead of binding an entry "
            + "of UiTexts: " + string.Join("; ", written));
    }

    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void No_screen_puts_a_literal_where_a_person_reads_it(string path)
    {
        var assigned = LiteralOnScreen()
            .Matches(File.ReadAllText(path))
            .Select(match => match.Value)
            .ToArray();

        assigned.ShouldBeEmpty(
            $"{Path.GetFileName(path)} assigns words a person reads instead of a text from "
            + "UiTexts: " + string.Join("; ", assigned));
    }

    /// <summary>
    /// ISC-152's other half on a screen that prints what a machine said. A path, a device's own
    /// name or a message off an exception is data and goes in the report untranslated — but never
    /// on a line of its own, where English reads as the application talking to somebody who chose
    /// Spanish. So a sentence from the catalogue says what happened first, and the words go under
    /// it.
    /// </summary>
    /// <remarks>
    /// The rule was written on <c>Dump</c> and enforced by nothing, and two sites had already got
    /// past it — one of them added by the pass that quoted the rule while breaking it. Read off the
    /// source for the reason the rest of this class is: a WinUI tree needs a UI thread and a
    /// packaged host, so a check that ran a window would never run.
    /// <para>
    /// What it reads is the method the call sits in: somewhere above it, and before the signature
    /// of whatever method that is, a sentence has to have gone into the same report. Not the line
    /// immediately above, because the report is written in order and a heading with a loop of lines
    /// under it is the shape half of these take. The method is the boundary because it is where a
    /// report line stops being reachable from what was said before it.
    /// </para>
    /// <para>
    /// A blank line is not a machine's words and is not asked for one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void No_screen_prints_what_a_machine_said_on_a_line_of_its_own(string path)
    {
        var lines = File.ReadAllLines(path);
        var bare = new List<string>();

        for (var at = 0; at < lines.Length; at++)
        {
            if (!DumpsWhatAMachineSaid().IsMatch(lines[at]))
            {
                continue;
            }

            var said = false;
            for (var back = at - 1; back >= 0; back--)
            {
                if (SaysSomethingFirst().IsMatch(lines[back]))
                {
                    said = true;
                    break;
                }

                if (OpensAMember().IsMatch(lines[back]))
                {
                    break;
                }
            }

            if (!said)
            {
                bare.Add($"line {at + 1}: {lines[at].Trim()}");
            }
        }

        bare.ShouldBeEmpty(
            $"{Path.GetFileName(path)} puts what a machine said in the report with no sentence "
            + "from the catalogue over it: " + string.Join("; ", bare));
    }

    /// <summary>
    /// A call that puts a machine's own words in the report — not the method itself, and not the
    /// blank line that separates two parts of one.
    /// </summary>
    [GeneratedRegex(@"(?<![\w.])Dump\((?!string )(?!string\.Empty\))")]
    private static partial Regex DumpsWhatAMachineSaid();

    /// <summary>A call that puts a sentence from the catalogue in the report.</summary>
    [GeneratedRegex(@"(?<![\w.])(Say|Report)\(")]
    private static partial Regex SaysSomethingFirst();

    /// <summary>
    /// The signature of a member of the class, which is as far back as a sentence already said
    /// can reach.
    /// </summary>
    [GeneratedRegex(@"^ {4}(public|private|protected|internal)\b[^;]*\(")]
    private static partial Regex OpensAMember();

    /// <summary>
    /// A quoted string — plain, verbatim or interpolated — landing on one of the properties a
    /// person reads, or on one of the automation properties whose setter is a method call.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\w.])(Text|Content|Header|Title|PlaceholderText|Description|PrimaryButtonText"
        + @"|SecondaryButtonText|CloseButtonText)\s*=\s*[$@]*""[^""]*""|"
        + @"AutomationProperties\.Set\w+\([^)]*""[^""]*""")]
    private static partial Regex LiteralOnScreen();
}
