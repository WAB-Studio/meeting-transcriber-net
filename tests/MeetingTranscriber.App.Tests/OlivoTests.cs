using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-173 and ISC-173.1, over the screens: every colour, text size and corner a screen uses is one
/// of the system's named few, and never a value chosen on the screen itself.
/// </summary>
/// <remarks>
/// It reads source rather than a running window, for the reason <see cref="ScreenTextsTests"/>
/// gives: a WinUI tree needs a UI thread and a packaged host that a build agent has not got. What
/// that costs is that a resource resolving at runtime is not what is proved here — a key that
/// exists in the dictionary and a key the platform finds are the same to this — and what it buys is
/// the one failure that is otherwise silent, which is a screen quietly typing a value of its own
/// and looking right in the designer.
/// <para>
/// The dictionary is held to <c>docs/design.md</c> and not to itself. That page is the authority
/// and this file is that page reaching the screen, so the check that matters is the two agreeing:
/// the colour table there is parsed and every row of it has to be a brush here with that value. A
/// test that only said the keys exist would pass over a palette somebody re-tuned by hand.
/// </para>
/// </remarks>
public partial class OlivoTests
{
    /// <summary>
    /// The platform styles Olivo's own control styles are <c>BasedOn</c>. Allowed inside the
    /// dictionary and nowhere else: that is the whole shape of taking the platform's geometry and
    /// not its skin — focus, the keyboard and the automation peer stay the platform's, and every
    /// screen goes through an Olivo key to reach them.
    /// </summary>
    private static readonly HashSet<string> PlatformBases =
    [
        "DefaultButtonStyle",
        "DefaultComboBoxItemStyle",
        "DefaultComboBoxStyle",
        "DefaultTextBoxStyle",
    ];

    /// <summary>
    /// The properties that carry a colour, a size or a corner — the three ISC-173.1 names. A value
    /// typed into one of these is the failure this class exists to find, and a markup extension
    /// naming a key is what it has to be instead.
    /// </summary>
    private static readonly HashSet<string> CarriesAValue =
    [
        "Background",
        "BorderBrush",
        "CornerRadius",
        "Fill",
        "FontFamily",
        "FontSize",
        "Foreground",
        "Stroke",
    ];

    public static TheoryData<string> Screens() =>
        [.. AppSources.With(".xaml").Where(file => !IsTheDictionary(file)).Select(file => file.FullName)];

    [Fact]
    public void There_are_screens_to_check()
    {
        // Without this every theory below passes by finding nothing, which is how a path that
        // stopped resolving reads exactly like a codebase with nothing wrong in it.
        Screens().Count.ShouldBeGreaterThan(0);
        Olivo().Root.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_chooses_a_colour_a_size_or_a_corner_of_its_own(string path)
    {
        var chosen = XDocument
            .Load(path)
            .Descendants()
            .SelectMany(Values)
            .Where(carried => !carried.Value.TrimStart().StartsWith('{'))
            .Select(carried => $"{carried.Property}=\"{carried.Value}\"")
            .ToArray();

        chosen.ShouldBeEmpty(
            $"{Path.GetFileName(path)} chooses these on the screen instead of naming one of Olivo's: "
            + string.Join("; ", chosen));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_quietens_a_line_of_text_with_opacity(string path)
    {
        // The three inks are what makes a second line quieter, and `docs/design.md` §Type says so
        // outright. Opacity is a fourth grey nobody chose — and a different fourth grey over every
        // surface it lands on, so two lines meant to read alike do not.
        //
        // Text, and not everything. The same page gives opacity one job and names it: a source that
        // died drops its **whole card** to 62 per cent, which is the one thing on any screen that
        // says a thing is not there rather than that it is quiet. A ban on every element would be
        // this check forbidding the design, and the card that draws that source would arrive to
        // find the guard in its way.
        var faded = XDocument
            .Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock"
                || (string?)element.Attribute("TargetType") == "TextBlock")
            .Where(element => element.Attribute("Opacity") is not null
                || element.Elements("Setter").Any(setter => (string?)setter.Attribute("Property") == "Opacity"))
            .Select(element => element.Name.LocalName)
            .ToArray();

        faded.ShouldBeEmpty(
            $"{Path.GetFileName(path)} quietens these with Opacity instead of naming one of the "
            + "three inks: " + string.Join("; ", faded));
    }

    public static TheoryData<string> CodeBehind() =>
        [.. AppSources.With(".cs").Select(file => file.FullName)];

    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void No_code_behind_chooses_a_colour_or_a_size_of_its_own(string path)
    {
        // The markup is not the only place a screen is drawn. Every card on the meetings list and
        // every step of the save is built in code, so a check that read only `.xaml` would be a
        // check with half the application outside it — and it was: a glyph on the saving card
        // carried its own font size for as long as that card has existed.
        var source = File.ReadAllText(path);

        var chosen = ValueInCode()
            .Matches(source)
            .Where(match => !SourceLines.StandsInACommentedLine(source, match.Index))
            .Select(match => $"line {SourceLines.LineOf(source, match.Index)}: {match.Value.Trim()}")
            .ToArray();

        chosen.ShouldBeEmpty(
            $"{Path.GetFileName(path)} chooses these in code instead of naming one of Olivo's: "
            + string.Join("; ", chosen));
    }

    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void Every_resource_the_code_behind_asks_for_by_name_exists(string path)
    {
        // A screen built in code names its brushes and its ranks as strings, and a string that
        // names nothing is not a build failure — it is an exception on the UI thread, thrown while
        // a meeting is being recorded, off a build that was green. Two of the meter's four layers
        // are reachable only this way.
        var source = File.ReadAllText(path);

        // Olivo's, and the screen's own. A screen keeps the styles that are only ever its —
        // what a meeting's row looks like is nobody else's — and its code-behind reaches those
        // through the same call, so both are names that resolve.
        var reachable = Keys(Olivo());
        var markup = new FileInfo(Path.ChangeExtension(path, null));

        if (markup.Exists && markup.Extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            reachable.UnionWith(Keys(XDocument.Load(markup.FullName)));
        }

        var missing = KeyInCode()
            .Matches(source)
            .Select(match => match.Groups["key"].Value)
            .Where(key => !reachable.Contains(key))
            .Distinct()
            .ToArray();

        missing.ShouldBeEmpty(
            $"{Path.GetFileName(path)} asks for resources that are neither Olivo's nor its own "
            + "screen's, so nothing defines them and the lookup throws on the UI thread: "
            + string.Join("; ", missing));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_resource_a_screen_names_is_one_that_exists(string path)
    {
        var screen = XDocument.Load(path);
        var itsOwn = Keys(screen);

        var missing = screen
            .Descendants()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                .Concat(element.Elements("Setter").Select(setter => (string?)setter.Attribute("Value") ?? string.Empty)))
            .SelectMany(value => Named().Matches(value).Select(match => match.Groups["key"].Value))
            .Where(key => !itsOwn.Contains(key) && !Keys(Olivo()).Contains(key))
            .Distinct()
            .ToArray();

        missing.ShouldBeEmpty(
            $"{Path.GetFileName(path)} names resources that are neither its own nor Olivo's, so "
            + "they are the platform's and the screen is not drawn from one visual system: "
            + string.Join("; ", missing));
    }

    [Fact]
    public void The_dictionary_carries_every_colour_the_design_names_at_the_value_it_names()
    {
        var page = Palette();
        page.Count.ShouldBe(12, "docs/design.md §Colour is the twelve-row table this reads.");

        var brushes = Olivo()
            .Descendants()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                brush => (string?)brush.Attribute(XName.Get("Key", X)) ?? string.Empty,
                brush => (string?)brush.Attribute("Color") ?? string.Empty,
                StringComparer.Ordinal);

        foreach (var (key, value) in page)
        {
            brushes.ShouldContainKey(
                key, $"docs/design.md names {key} and Olivo.xaml does not define it.");
            brushes[key].ShouldBe(
                value,
                $"{key} is {value} on docs/design.md and {brushes[key]} in Olivo.xaml. That page is "
                + "the authority, so the dictionary is what is wrong.");
        }
    }

    [Fact]
    public void Every_key_in_the_dictionary_is_one_a_screen_reaches()
    {
        // `docs/design.md` §Colour says the key is that page's suggestion and **the first screen to
        // need one settles it**. A key for a screen nobody has written is therefore not settled by
        // anything — it is a guess at what that screen will want, made by somebody who is not
        // building it, and `CLAUDE.md` is plain that an abstraction built for a caller that does
        // not exist costs more than the duplication it saved. Twenty-one of these were cut on the
        // way to this test.
        //
        // The colour table is the exception, and the only one: that page writes those twelve keys
        // down itself, so they are settled by the page rather than by a screen.
        var settledByThePage = Palette().Keys;

        var reached = Reached();

        var orphans = Keys(Olivo())
            .Except(PlatformBases)
            .Except(settledByThePage)
            .Where(key => !reached.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        orphans.ShouldBeEmpty(
            "Olivo defines these and no screen names them, so the screen that needs one has not "
            + "been built and it is not settled: " + string.Join("; ", orphans));
    }

    /// <summary>
    /// Every resource key any screen names, in markup or in code — the dictionary's own definitions
    /// left out, so a key used only by the entry that defines it does not count as reached.
    /// </summary>
    private static HashSet<string> Reached()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in AppSources.With(".xaml").Concat(AppSources.With(".cs")))
        {
            var source = File.ReadAllText(file.FullName);

            if (IsTheDictionary(file))
            {
                source = Defines().Replace(source, string.Empty);
            }

            foreach (Match found in Named().Matches(source))
            {
                reached.Add(found.Groups["key"].Value);
            }

            foreach (Match found in KeyInCode().Matches(source))
            {
                reached.Add(found.Groups["key"].Value);
            }
        }

        return reached;
    }

    [Fact]
    public void The_dictionary_names_no_word_anybody_reads()
    {
        // What pays for `ScreenTextsTests` leaving a ResourceDictionary alone. A word can only
        // reach somebody through a property a screen binds, and that check refuses anything but
        // `{x:Bind}` on every one of them — so a sentence in here is unreachable. This is the other
        // half: it cannot be put here in the first place.
        //
        // The subject is the words and not the element. It was the element, which was right while
        // the dictionary held only styles — and a control template has to be able to carry a
        // `TextBlock`, because `PlaceholderText` reaches the screen through a template part named
        // `PlaceholderTextBlock` and nothing else draws it. What is refused is the same thing it
        // always was: a `Text` this file settles rather than passes through, as an attribute or
        // between the tags.
        var words = Olivo()
            .Descendants()
            .Where(element => element.Name.LocalName is "String" or "TextBlock")
            .Where(Says)
            .Select(element => $"<{element.Name.LocalName}>")
            .ToArray();

        words.ShouldBeEmpty(
            "Olivo.xaml holds words rather than values: " + string.Join("; ", words));
    }

    /// <summary>
    /// The three shapes the check above has to tell apart, kept as rows rather than as a sentence
    /// about them: narrowing it from "no TextBlock at all" to "no TextBlock that says something" is
    /// exactly the edit that could let a word through, and a green run over a dictionary that
    /// happens to hold none would not notice.
    /// </summary>
    public static TheoryData<string, bool> WordsOrValues() => new()
    {
        { """<TextBlock Text="Elegí un idioma" />""", true },
        { """<TextBlock>Elegí un idioma</TextBlock>""", true },
        { """<TextBlock Text="{TemplateBinding PlaceholderText}" />""", false },
    };

    [Theory]
    [MemberData(nameof(WordsOrValues))]
    public void A_word_is_told_from_a_value_however_it_was_written(string written, bool isAWord) =>
        Says(XElement.Parse(written)).ShouldBe(isAWord, written);

    [Fact]
    public void Both_fonts_are_in_the_package_with_the_licence_that_lets_them_be()
    {
        // `docs/design.md` says both fonts ship inside the package, so a machine with the
        // application has them and no screen falls back. The SIL Open Font License allows that
        // exactly on the condition that each copy carries the notice and the licence, so the two
        // OFL files are as load-bearing as the fonts and are checked with them.
        foreach (var shipped in new[]
        {
            "SpaceGrotesk.ttf",
            "JetBrainsMono.ttf",
            "SpaceGrotesk-OFL.txt",
            "JetBrainsMono-OFL.txt",
        })
        {
            var file = AppSources.At(Path.Combine("MeetingTranscriber.App", "Assets", "Fonts", shipped));

            file.Exists.ShouldBeTrue($"{shipped} is not there, so the package does not carry it.");
            file.Length.ShouldBeGreaterThan(0, $"{shipped} is empty.");

            Project().ShouldContain(
                $"Assets\\Fonts\\{shipped}",
                Case.Sensitive,
                $"{shipped} is on disk but is not Content, so it is not inside the package.");
        }

        var faces = Olivo()
            .Descendants()
            .Where(element => element.Name.LocalName == "FontFamily")
            .Select(element => element.Value)
            .ToArray();

        faces.Length.ShouldBe(2, "Olivo has two faces: one for text and one for numbers.");
        faces.ShouldAllBe(face => face.StartsWith("ms-appx:///Assets/Fonts/", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_eases_both_ways_and_nothing_bounces()
    {
        // `docs/design.md` §What moves: entering decelerates and leaving accelerates, which is the
        // platform's own grammar and reads as weight rather than as an effect. An ease that is
        // symmetric reads as an effect, and the three that overshoot read as a toy.
        var wrong = AppSources.With(".cs")
            .Concat(AppSources.With(".xaml"))
            .SelectMany(file => Findings(file, File.ReadAllText(file.FullName)))
            .ToArray();

        wrong.ShouldBeEmpty(
            "These ease both ways, bounce or overshoot, and docs/design.md §What moves allows "
            + "none of the three: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// Where <paramref name="source"/> eases both ways, bounces or overshoots — the prose about not
    /// doing those things left out, because the rule is written down beside the code it governs and
    /// the example that makes it clear is the thing being banned.
    /// </summary>
    private static IEnumerable<string> Findings(FileInfo file, string source) => EasesBothWays()
        .Matches(source)
        .Where(match => !SourceLines.StandsInACommentedLine(source, match.Index))
        .Select(match => $"{file.Name} line {SourceLines.LineOf(source, match.Index)}: {match.Value}");

    /// <summary>
    /// The colour table of <c>docs/design.md</c> §Colour, as the key each row settles and the value
    /// it settles it at. Read from the page rather than written down here, because a copy of it
    /// here would be a third thing to keep in step with the two this check exists to compare.
    /// </summary>
    private static Dictionary<string, string> Palette()
    {
        var page = File.ReadAllText(AppSources.At(Path.Combine("..", "docs", "design.md")).FullName);

        return PaletteRow()
            .Matches(page)
            .ToDictionary(
                row => row.Groups["key"].Value,
                row => row.Groups["value"].Value,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether an element carries words of its own — a <c>Text</c> that is not a binding, or text
    /// between its tags.
    /// </summary>
    /// <remarks>
    /// A markup extension is a value coming from somewhere else, and a template part carrying
    /// <c>{TemplateBinding PlaceholderText}</c> settles nothing: the words are whatever the screen
    /// put on the control, which <see cref="ScreenTextsTests"/> holds to the catalogue at the other
    /// end. Anything not in braces is this file saying it.
    /// </remarks>
    private static bool Says(XElement element) =>
        element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
        || (string?)element.Attribute("Text") is { } said && !said.TrimStart().StartsWith('{');

    /// <summary>Every key a dictionary or a screen's own resources define.</summary>
    private static HashSet<string> Keys(XDocument document) =>
    [
        .. document
            .Descendants()
            .Select(element => (string?)element.Attribute(XName.Get("Key", X)))
            .OfType<string>(),
        .. PlatformBases,
    ];

    /// <summary>
    /// What one element carries that is a colour, a size or a corner — as an attribute, and as the
    /// <c>Setter</c> shape that says the same thing inside a style.
    /// </summary>
    private static IEnumerable<(string Property, string Value)> Values(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(a => CarriesAValue.Contains(a.Name.LocalName)))
        {
            yield return (attribute.Name.LocalName, attribute.Value);
        }

        if (element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") is { } property
            && CarriesAValue.Contains(property)
            && (string?)element.Attribute("Value") is { } value)
        {
            yield return (property, value);
        }
    }

    private static bool IsTheDictionary(FileInfo file) =>
        file.Name.Equals("Olivo.xaml", StringComparison.Ordinal);

    private static XDocument Olivo() => XDocument.Load(
        AppSources.With(".xaml").Single(IsTheDictionary).FullName);

    private static string Project() => File.ReadAllText(
        AppSources.At(Path.Combine("MeetingTranscriber.App", "MeetingTranscriber.App.csproj")).FullName);

    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [GeneratedRegex(@"\{(?:Static|Theme)Resource\s+(?<key>[A-Za-z0-9_]+)\s*\}")]
    private static partial Regex Named();

    /// <summary>
    /// A row of the colour table: the role, the value, where it goes, and the key. The key is what
    /// this reads and the value is what it checks, so both are captured and the two middle columns
    /// are not.
    /// </summary>
    [GeneratedRegex(@"^\|[^|]+\|\s*`(?<value>#[0-9A-Fa-f]{6})`\s*\|[^|]+\|\s*`(?<key>\w+Brush)`\s*\|",
        RegexOptions.Multiline)]
    private static partial Regex PaletteRow();

    [GeneratedRegex(@"EaseInOut|BounceEase|ElasticEase|BackEase")]
    private static partial Regex EasesBothWays();

    /// <summary>
    /// A colour or a size written into code rather than named. The brush shapes are what building
    /// a colour from nothing looks like; <c>FontSize</c> is what a rank looks like when it is a
    /// number. A key read out of the dictionary is a name and never matches these.
    /// </summary>
    [GeneratedRegex(@"FontSize\s*=\s*\d|new\s+SolidColorBrush\s*\(|Color(s)?\.From[A-Za-z]*\s*\(|Colors\.[A-Za-z]")]
    private static partial Regex ValueInCode();

    /// <summary>A resource asked for by name from code.</summary>
    [GeneratedRegex(@"(?:Resources\[|Painted\(|Chrome\(|Sized\()""(?<key>\w+)""")]
    private static partial Regex KeyInCode();

    /// <summary>Where the dictionary settles a key, as opposed to where anything uses one.</summary>
    [GeneratedRegex(@"x:Key=""\w+""")]
    private static partial Regex Defines();
}
