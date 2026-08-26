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
/// longer pattern.
/// </para>
/// <para>
/// Both halves of that — what it reaches and what it does not — are rows of
/// <see cref="WordsAPersonWouldRead"/>, <see cref="WhatNobodyReads"/> and
/// <see cref="OutOfReachOnPurpose"/> rather than only a paragraph. The third shape a screen can say
/// something in spent as long as the recorder window has existed described here and caught by
/// nothing, which is exactly what a guard with no subject to find looks like from outside; a gap
/// named in a test is a gap that tells somebody the day it closes.
/// </para>
/// </remarks>
public partial class ScreenTextsTests
{
    /// <summary>
    /// The properties that put words in front of a person. Each has to name an entry in the
    /// catalogue and never carry the words itself.
    /// </summary>
    /// <remarks>
    /// This is the one list. Markup is held to it by name and code-behind by the assignment and the
    /// setter built out of it, so a property added here is caught in both places or in neither —
    /// which is what the second, hand-written copy of these names could not promise.
    /// <para>
    /// The attached ones are every automation property that carries a string, because a screen
    /// reader says every one of them out loud — which is this class's whole definition of a word a
    /// person reads. The rule is that and not a shorter list on purpose: deriving the setter half
    /// from this set replaced a pattern that had matched <c>AutomationProperties.Set</c> and any
    /// name after it, so anything a shorter list left out would have gone from caught to caught by
    /// neither half, silently, in an application that sets none of them today.
    /// </para>
    /// <para>
    /// <c>AutomationId</c> is the one string deliberately absent. Nobody hears it; it is how a test
    /// or a tool finds the element again, so it has to stay the same in every language, and holding
    /// it to the catalogue would have asked for a translation of <c>recordButton</c>. The automation
    /// properties that are not here carry no words at all — a live setting, a landmark type, a
    /// position in a set — and there is nothing in them to say twice.
    /// </para>
    /// <para>
    /// <c>AcceleratorKey</c> and <c>AccessKey</c> are the two worth arguing about, and the argument
    /// was had rather than skipped. Being in this list costs them the markup half as well: a screen
    /// writing <c>AutomationProperties.AcceleratorKey="Control+G"</c> has to bind a catalogue entry
    /// instead, and none of the screens still to be built has been written yet, so the cost lands on
    /// people who were not in this decision. They stay because the string is not the binding — a
    /// <c>KeyboardAccelerator</c> is — it is the sentence a screen reader reads out, and the letter
    /// in it follows the verb it abbreviates. <c>Control+G</c> for <em>grabar</em> is <c>Control+R</c>
    /// for <em>record</em>, so a screen that hard-codes one announces the wrong key to whoever is
    /// reading the other language. Where a screen does want the same keys in both, the catalogue
    /// already refuses two versions that are equal until somebody says which kind of entry it is,
    /// which is the conversation worth having and not a cost to avoid. Dropping them would buy back
    /// some ceremony on screens nobody has written and pay for it with a spoken string in one
    /// language, which is the silent failure of the two.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> Reads =
    [
        "AutomationProperties.AcceleratorKey",
        "AutomationProperties.AccessKey",
        "AutomationProperties.FullDescription",
        "AutomationProperties.HelpText",
        "AutomationProperties.ItemStatus",
        "AutomationProperties.ItemType",
        "AutomationProperties.LocalizedControlType",
        "AutomationProperties.LocalizedLandmarkType",
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
        var assigned = LiteralsIn(File.ReadAllText(path));

        assigned.ShouldBeEmpty(
            $"{Path.GetFileName(path)} assigns words a person reads instead of a text from "
            + "UiTexts: " + string.Join("; ", assigned));
    }

    /// <summary>
    /// The words a person reads that <paramref name="source"/> says itself, each with the line it
    /// is on. The line number because a screen is a thousand lines long and the sibling check below
    /// has always given one; on one line because a ternary is written over three as often as one,
    /// and a list joined with <c>;</c> stops reading as a list the moment an entry breaks.
    /// </summary>
    private static string[] LiteralsIn(string source) =>
    [
        .. LiteralOnScreen
            .Matches(source)
            .Where(match => !StandsInACommentedLine(source, match.Index))
            .Select(match => $"line {LineOf(source, match.Index)}: "
                + string.Join(' ', match.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))),
    ];

    /// <summary>Whether what was found at <paramref name="at"/> is on a line that is all comment.</summary>
    /// <remarks>
    /// The house style here is a paragraph of prose per member, and the rule this class enforces is
    /// one somebody will want to write down next to the code it governs — with the example that
    /// makes it clear, which is a literal. A comment cannot put a word in front of anybody, so
    /// breaking a build over one is noise, and noise is how a guard stops being believed.
    /// <para>
    /// What it reads is the line, not the language: a line that opens a comment, and not a comment
    /// opened after code on the same line. Finding that second one means telling <c>//</c> from the
    /// <c>//</c> inside <c>&quot;http://…&quot;</c>, which is a scanner and not a line test, and
    /// getting it wrong would silently drop a real literal rather than merely report a false one.
    /// So a trailing comment is still read as code, and that is the smaller mistake.
    /// </para>
    /// </remarks>
    private static bool StandsInACommentedLine(string source, int at)
    {
        var opens = at == 0 ? 0 : source.LastIndexOf('\n', at - 1) + 1;
        var before = source[opens..at].TrimStart();

        return before.StartsWith("//", StringComparison.Ordinal)
            || before.StartsWith("/*", StringComparison.Ordinal)
            || before.StartsWith('*');
    }

    private static int LineOf(string source, int at) => source.AsSpan(0, at).Count('\n') + 1;

    /// <summary>
    /// The shapes a word a person reads is written in. Held here and not only against the screens,
    /// for the reason <see cref="There_are_screens_to_check"/> already gives about the file list:
    /// this check ran green over eleven hundred lines of code-behind for as long as they existed,
    /// because the one shape it could not see was the one they were all written in and there was
    /// nothing else for it to find. A guard whose subject is absent reads exactly like a guard that
    /// works. These are the subject, kept where an edit to the pattern runs into them.
    /// </summary>
    public static TheoryData<string> WordsAPersonWouldRead() =>
    [
        @"StatusText.Text = ""hola"";",
        @"Text = ""hola"";",
        @"StatusText.Text = $""hola {count}"";",
        @"StatusText.Text += ""hola"";",
        @"StatusText.Text ??= ""hola"";",
        @"CountText.Text = ready ? ""listo"" : In(UiTexts.Waiting);",
        "CountText.Text = ready\n            ? In(UiTexts.Waiting)\n            : \"quedan dos\";",
        @"CountText.Text = _meetings.Count == 0 ? In(UiTexts.A) : UiTexts.B.In(_language, _meetings.Count(entry => entry.Owed.IsOwed)) + "" listas"";",
        @"Foo.Header = name ?? ""sin nombre"";",
        @"OutputText.Text = In(UiTexts.Corpus) + "" (sin reuniones)"";",
        @"AutomationProperties.SetName(box, ""hola"");",
        @"AutomationProperties.SetHelpText(box, ""hola"");",
        @"AutomationProperties.SetFullDescription(box, ""hola"");",
        @"AutomationProperties.SetItemStatus(list, ""hola"");",
        @"AutomationProperties.SetItemType(row, ""hola"");",
        @"AutomationProperties.SetLocalizedControlType(box, ""hola"");",
        @"AutomationProperties.SetLocalizedLandmarkType(panel, ""hola"");",
        @"AutomationProperties.SetAcceleratorKey(button, ""Control+G"");",
        @"AutomationProperties.SetAccessKey(button, ""G"");",
        @"ToolTipService.SetToolTip((UIElement)child, ""hola"");",
    ];

    /// <summary>
    /// What holds no word anybody reads, and what this must therefore leave alone. A guard that
    /// goes red over a resource key or a format specifier is a guard somebody edits around, so
    /// these are as load-bearing as the ones above.
    /// </summary>
    public static TheoryData<string> WhatNobodyReads() =>
    [
        @"StatusText.Text = _status?.In(_language) ?? string.Empty;",
        @"capturing.Text = string.Empty;",
        @"if (line.Text == words)",
        "CountText.Text = _meetings.Count == 0\n            ? In(UiTexts.A)\n            : UiTexts.B.In(_language, _meetings.Count(entry => entry.Owed.IsOwed));",
        @"var card = new Button { Content = icon, Tag = owed ? ""MeetingStoppedOnAPerson"" : ""MeetingLine"" };",
        @"var card = new Button { Content = icon, Tag = ""MeetingCard"" };",
        @"Header = Chrome(entry.Owed.WaitsOnSomebody ? ""MeetingStoppedOnAPerson"" : ""MeetingLine"");",
        @"CountText.Text = UiTexts.A.In(_language, count.ToString(many ? ""N0"" : ""N2"", Culture));",
        @"Title = Path.Combine(root, packaged ? ""corpus"" : ""cache"");",
        @"Title = UiTexts.Get(key: ""dialog.title"");",
        @"Root.Content = (UIElement)Resources[dark ? ""DarkPanel"" : ""LightPanel""];",
        @"AutomationProperties.SetAutomationId(box, ""recordButton"");",
        "AutomationProperties.SetName(box, spoken);\n        Trace(\"where the corpus is\");",
        @"var body = new StringContent(json);",
        @"var probe = Path.Combine(root, ""Local"", ""Packages"");",
        @"        // StatusText.Text = ""hola"";",
        @"    /// A screen never writes Title = ""Reuniones"" itself.",
        @"     * StatusText.Text = ""hola"";",
    ];

    /// <summary>
    /// What this is known not to reach, named rather than papered over — and named here rather than
    /// only in prose, so that widening the pattern to take one of them in turns this red and makes
    /// somebody say so in <c>ISA.md</c> instead of leaving the claim's evidence describing a check
    /// that stopped being the one it describes.
    /// </summary>
    /// <remarks>
    /// The first three are one gap wearing three faces: a word inside a call is a word handed to a
    /// method, and telling <c>Show("Listo")</c> from <c>Path.Combine("Local", …)</c> is a question
    /// about types that wants an analyser. A switch expression is the fourth, and a comment opened
    /// after code on the same line is the one false red left standing rather than a miss.
    /// </remarks>
    public static TheoryData<string> OutOfReachOnPurpose() =>
    [
        @"Show(""Listo"");",
        @"StatusText.Text = Fmt(""Listo"");",
        @"StatusText.Text = ready ? Fmt($""{count}"") : ""nada"";",
        @"private string Ready => ""Listo"";",
        @"StatusText.Text = state switch { RecorderState.Ready => ""listo"", _ => ""nada"" };",
    ];

    [Theory]
    [MemberData(nameof(WordsAPersonWouldRead))]
    public void A_word_a_person_reads_is_found_however_it_was_written(string written) =>
        LiteralsIn(written).ShouldNotBeEmpty(
            "this is a word somebody would read on a screen and nothing found it: " + written);

    [Theory]
    [MemberData(nameof(WhatNobodyReads))]
    public void What_nobody_reads_is_left_alone(string written) =>
        LiteralsIn(written).ShouldBeEmpty(
            "nobody reads this and it was reported anyway: " + written);

    [Theory]
    [MemberData(nameof(OutOfReachOnPurpose))]
    public void What_is_out_of_reach_is_still_out_of_reach(string written) =>
        LiteralsIn(written).ShouldBeEmpty(
            "this is recorded as out of reach in ISA.md and it was found, so the record is now "
            + "wrong about what the check holds: " + written);

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
    /// <remarks>
    /// An access modifier at exactly four spaces, which is every member of the four screens as
    /// they are written, and the limit of what this probe covers. Three shapes would scan past
    /// their own method and could be let through by a <c>Say</c> belonging to the one above it: a
    /// <c>Dump</c> inside a local function, one inside a nested type, and one in a member written
    /// with no modifier at all. None of the three exists in these files today, which is why the
    /// pattern is this and not a parse — but a green run over a screen that grows one proves
    /// nothing, so whoever writes the first one widens this in the same pass.
    /// </remarks>
    [GeneratedRegex(@"^ {4}(public|private|protected|internal)\b[^;]*\(")]
    private static partial Regex OpensAMember();

    /// <summary>
    /// A quoted string — plain, verbatim or interpolated — reaching one of <see cref="Reads"/>
    /// from code-behind, however the assignment is written.
    /// </summary>
    /// <remarks>
    /// It is a <c>Regex</c> and not a <c>[GeneratedRegex]</c> like the three above it because its
    /// property names are <see cref="Reads"/>, and a second copy of that list is a name added to
    /// one of them and not the other — the same words held to binding in markup and waved through
    /// in code-behind. The trade is that the pattern is no longer checked when this compiles, which
    /// is what the three theories over <see cref="LiteralsIn"/> pay for: a <see cref="Reads"/> entry
    /// that does not survive being made into a pattern fails them all rather than passing quietly.
    /// It reads <see cref="Reads"/> as a field initialiser, so it has to stand below it in the file.
    /// <para>
    /// The timeout is not for anything measured — nothing here comes close — but a pattern built at
    /// run time out of a list somebody can edit should fail as a red test naming a screen rather
    /// than as a build that never comes back.
    /// </para>
    /// </remarks>
    private static readonly Regex LiteralOnScreen = new(
        Assignment() + "|" + AttachedSetter(),
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// One of the plain properties taking a literal, whatever stands between the <c>=</c> and it.
    /// </summary>
    /// <remarks>
    /// The lookbehind is <c>(?&lt;!\w)</c> and not <c>(?&lt;![\w.])</c>. Excluding the dot was
    /// meant to stop <c>SomeText</c> matching on its own tail, which the word character already
    /// does on its own; what it actually excluded was every qualified assignment —
    /// <c>StatusText.Text = "hola"</c> — which is how a screen with named elements writes all of
    /// them, so the check ran green over the one shape it most needed to see.
    /// <para>
    /// <c>=</c>, <c>+=</c> and <c>??=</c> put a word on the property and are read as assignments.
    /// <c>==</c> does not, and neither does <c>=&gt;</c>: an expression-bodied member is out of
    /// reach whatever it returns, which is a thing that can be said in one sentence, where "out of
    /// reach unless a <c>?</c> happens to be in it" is not.
    /// </para>
    /// <para>
    /// Between the <c>=</c> and the literal it allows a run ending on <c>?</c>, <c>:</c> or
    /// <c>+</c>, which is what reaches either branch of a ternary — written on one line or on
    /// three — the right-hand side of a <c>??</c>, and a sentence built by concatenation. What
    /// bounds that run is <see cref="WithinOneAssignment"/>.
    /// </para>
    /// </remarks>
    private static string Assignment() =>
        @"(?<!\w)(?:" + Named(Plain, name => name) + @")"
        + @"\s*(?:\+|\?\?)?=(?![=>])\s*(?:" + WithinOneAssignment + @"[?:+]\s*)?[$@]*""[^""]*""";

    /// <summary>
    /// How far the value of one assignment reaches: not past a <c>;</c> or a brace, which end the
    /// statement or the initialiser it sits in; not past a quote, which is what it was looking for;
    /// not past a second <c>=</c>, which is the next assignment and whose words are not this
    /// property's; and never into a bracket it has not seen closed.
    /// </summary>
    /// <remarks>
    /// The bracket rule is the one that took two goes. Barring the comma instead left the false
    /// branch of <c>ok ? Some.Text(a, b) : "x"</c> unread, which is how half the ternaries on these
    /// screens are written. Letting everything through put the run inside argument lists, where
    /// <c>Chrome(owed ? "MeetingStoppedOnAPerson" : "MeetingLine")</c> — a shape already on one of
    /// these screens — would have demanded a Spanish translation of a resource key, and reported it
    /// under the name of the property the call was assigned to. Crossing a bracket already closed
    /// and never entering an open one keeps both: a completed call is something the value went
    /// past, an open one is somewhere the value went inside, and a word inside a call is a word
    /// handed to a method — the gap this class has always named.
    /// <para>
    /// An <c>=</c> belonging to <c>==</c>, <c>!=</c>, <c>&lt;=</c>, <c>&gt;=</c> or <c>=&gt;</c> is
    /// not a second assignment and the two lookarounds let it through. At any character at most one
    /// branch can begin, so there is nothing here for the engine to backtrack through; measured over
    /// the four screens the whole match is under a millisecond.
    /// </para>
    /// </remarks>
    private const string WithinOneAssignment =
        @"(?:[^;""{}=()\[\]]|\(" + InsideOneCall + @"\)|\[[^\[\]""]*\]|(?<=[=!<>])=|=(?=[=>]))*";

    /// <summary>
    /// What can stand inside a call before it either closes or reaches its first quoted argument:
    /// anything but a bracket or a quote, or one whole call nested in it.
    /// </summary>
    /// <remarks>
    /// The quote is the load-bearing exclusion. Letting one through made the pattern give the
    /// argument's own closing quote back to the engine and start counting from there, so what was
    /// quoted back at the reader ran from the call to the next quote anywhere in the file — a red
    /// test naming a screen and then burying which line of it.
    /// </remarks>
    private const string InsideOneCall = @"(?:[^()""]|\([^()""]*\))*";

    /// <summary>An attached property, which has no assignment to be found by — only its setter.</summary>
    private static string AttachedSetter() =>
        @"(?<!\w)(?:" + Named(Attached, name => name.Replace(".", ".Set", StringComparison.Ordinal))
        + @")\(" + InsideOneCall + @"""[^""]*""";

    /// <summary>An entry of <see cref="Reads"/> that is a property of the element itself.</summary>
    private static bool Plain(string name) => !name.Contains('.');

    /// <summary>
    /// An entry of <see cref="Reads"/> that is attached, so that markup's <c>Owner.Prop</c> is
    /// <c>Owner.SetProp(element, value)</c> in code and there is no assignment to look for.
    /// </summary>
    private static bool Attached(string name) => name.Contains('.');

    /// <summary>
    /// The entries of <see cref="Reads"/> matching <paramref name="of"/>, as an alternation. Sorted
    /// so the pattern reads the same every run; which alternative wins is settled by the lookbehind
    /// and not by the order, so <c>Text</c> standing before <c>PlaceholderText</c> costs nothing.
    /// </summary>
    /// <remarks>
    /// Empty throws rather than returning one. <c>string.Join</c> over nothing is <c>""</c>, and
    /// <c>(?:)</c> is a valid group matching the empty string, so a half of this pattern that lost
    /// its last name would go on running — matching everything or nothing depending on which half,
    /// and reporting neither.
    /// </remarks>
    private static string Named(Func<string, bool> of, Func<string, string> spelt)
    {
        var names = Reads.Where(of).Select(spelt).Select(Regex.Escape).Order(StringComparer.Ordinal).ToArray();

        return names.Length == 0
            ? throw new InvalidOperationException(
                "Reads has no property of this kind left, and half of the pattern would match "
                + "the empty string instead of saying so.")
            : string.Join('|', names);
    }
}
