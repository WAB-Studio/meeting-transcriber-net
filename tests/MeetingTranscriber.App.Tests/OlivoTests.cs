using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-173 and ISC-173.1, over the screens: every colour, text size and corner a screen uses is one
/// of the system's named few, and never a value chosen on the screen itself. And the dictionary's
/// half of ISC-152: no word a person reads is written in Olivo.xaml, as an element that says
/// something or as a property a style settles onto every screen wearing it.
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
/// <para>
/// That holds in both directions, so part of this class gates a prose document and fails the build
/// over a diff carrying no C# and no XAML. The page used to sanction a colour written inside a
/// component while the screen check refused one, and a rule the authority grants and the build
/// refuses is settled by whichever the reader happened to open. So every colour the page writes is
/// held to <c>Olivo.xaml</c> too, and the one place a value with no brush behind it may stand is
/// §Colour's <c>Decided, and not yet a key</c> table.
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
    /// <remarks>
    /// <c>Color</c> is here because the rest only reach a brush a screen <em>uses</em>, and a
    /// screen that declares one of its own goes round every one of them:
    /// <c>&lt;SolidColorBrush x:Key="MyOwnGrey" Color="#B9B5AC" /&gt;</c> in a screen's own
    /// <c>Resources</c> is a value chosen on the screen, and
    /// <see cref="Every_resource_a_screen_names_is_one_that_exists"/> then allows the screen to name
    /// it because a screen's own keys are its own. The dictionary is where every brush is built, and
    /// <see cref="Screens"/> leaves it out, so no legitimate spelling loses anything here.
    /// <para>
    /// Written as an alternation and split, rather than as a set spelt out. <see cref="ValueInCode"/>
    /// watches the same nine names where a screen builds a <c>Setter</c> in code, and
    /// <c>[GeneratedRegex]</c> takes a constant expression only — so the constant is the list and
    /// this is derived from it, and there is nothing for the two sides to disagree about. The set
    /// matches an attribute name and the pattern matches a <c>DependencyProperty</c>, which are two
    /// spellings of one property rather than two lists.
    /// </para>
    /// </remarks>
    private const string CarriesAValueSpelt =
        "Background|BorderBrush|Color|CornerRadius|Fill|FontFamily|FontSize|Foreground|Stroke";

    /// <inheritdoc cref="CarriesAValueSpelt"/>
    private static readonly HashSet<string> CarriesAValue =
        [.. CarriesAValueSpelt.Split('|')];

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
        var chosen = ChosenOn(XDocument.Load(path));

        chosen.ShouldBeEmpty(
            $"{Path.GetFileName(path)} chooses these on the screen instead of naming one of Olivo's, "
            + "or hands one to a binding, which this check cannot follow: " + string.Join("; ", chosen));
    }

    /// <summary>
    /// Every colour, size and corner <paramref name="screen"/> writes down itself instead of naming
    /// one of Olivo's, as <c>Property="Value"</c>.
    /// </summary>
    /// <remarks>
    /// A value passes only when the whole of it names a key, which is <see cref="NamesAKey"/>, so
    /// every markup extension but the two <see cref="Named"/> reads is refused on a screen. That is
    /// deliberate. A binding hands the value to a code-behind property, where this check cannot
    /// follow it, and the sanctioned way to reach one from code is <c>Painted(…)</c> or
    /// <c>Sized(…)</c>, which <see cref="ChosenIn"/> polices — so a card that needs a state-driven
    /// brush has a door, and it is not this attribute. <c>{TemplateBinding …}</c> stands only inside
    /// a <c>ControlTemplate</c>, which lives in the dictionary <see cref="Screens"/> leaves out, so
    /// the day a screen writes one is the day a template moved out of Olivo.
    /// <para>
    /// It reads what <see cref="Values"/> gives it — attributes and the <c>Setter</c> shape, whose
    /// value may be written as an attribute or as a <c>&lt;Setter.Value&gt;</c>. So a colour nested
    /// as <c>&lt;Border.Background&gt;&lt;SolidColorBrush Color="…" /&gt;</c> is caught through the
    /// brush's own <c>Color</c> attribute, and the property elements that are <em>not</em>
    /// <c>&lt;Setter.Value&gt;</c> are not caught at all. None is written anywhere today, and a
    /// reader general enough for them would report
    /// <c>&lt;Border.Background&gt;&lt;StaticResource ResourceKey="OliveBrush" /&gt;</c>, the one
    /// legitimate spelling of the shape — which is why <see cref="ValueOf"/> reads a
    /// <c>&lt;Setter.Value&gt;</c> as its text and answers nothing when it holds an element instead.
    /// A <c>Setter</c> earns the exception because it reaches every element wearing the style, where
    /// a property element reaches one.
    /// </para>
    /// <para>
    /// The other spelling of a <c>Setter</c> — <c>Target="Chevron.Stroke"</c> inside a
    /// <c>&lt;VisualState.Setters&gt;</c>, which carries no <c>Property</c> attribute — is not
    /// reached either, and reaches a screen the day one writes an adaptive breakpoint. That sentence
    /// is <see cref="ScreenTextsTests.Screens"/>'s remarks', which own it for both halves.
    /// </para>
    /// </remarks>
    private static string[] ChosenOn(XDocument screen) =>
    [
        .. screen
            .Descendants()
            .SelectMany(Values)
            .Where(carried => !NamesAKey(carried.Value))
            .Select(carried => $"{carried.Property}=\"{carried.Value}\""),
    ];

    /// <summary>
    /// Whether <paramref name="value"/> is a key and nothing else.
    /// </summary>
    /// <remarks>
    /// The whole value and not a match somewhere inside it, because <c>{}</c> is XAML's escape for a
    /// literal opening on a brace: <c>{}{StaticResource PaperBrush}</c> reads as the sanctioned form
    /// from its third character on, and is a screen writing a value down. <see cref="Named"/> reused
    /// rather than a second anchored pattern, so one spelling of what a key looks like serves both
    /// the check that a key exists and the check that a value is one.
    /// </remarks>
    private static bool NamesAKey(string value)
    {
        var said = value.Trim();

        return Named().Match(said) is { Success: true } found
            && found.Index == 0
            && found.Length == said.Length;
    }

    /// <summary>
    /// One row of markup as a screen: <paramref name="markup"/> under a root that declares the two
    /// namespaces every screen declares.
    /// </summary>
    /// <remarks>
    /// The namespace is supplied here and not spelt onto each row, because getting it wrong is the
    /// one way this whole class of row lands useless and nothing would say so.
    /// <c>XElement.Elements("Setter")</c> builds a namespace-less name and so matched nothing on a
    /// real screen — the bug that had two of these checks green while reading nothing — but it
    /// matches perfectly well against a row parsed with no <c>xmlns</c> at all. Such a row passes
    /// before the fix and after it. A row cannot be written that way through this.
    /// <para>
    /// The root is inert: its only attributes are the two declarations, whose local names are
    /// <c>xmlns</c> and <c>x</c> and are in neither <see cref="CarriesAValue"/> nor
    /// <see cref="Named"/>'s vocabulary, and it is not a <c>TextBlock</c> and carries no
    /// <c>x:Key</c>. So every reader sees the row and the root alike, and reports only the row.
    /// </para>
    /// </remarks>
    private static XDocument Screen(string markup) =>
        XDocument.Parse($@"<Page xmlns=""{Presentation}"" xmlns:x=""{X}"">" + markup + "</Page>");

    [Fact]
    public void A_row_is_a_screen_and_not_a_fragment_standing_in_no_namespace()
    {
        // Every markup row below is a screen because `Screen` says so, and this is what says `Screen`
        // says so. A fragment parsed with no `xmlns` at all puts its elements in no namespace, which
        // is exactly where `Elements("Setter")` starts working — so a row written that way passes
        // with the bug this card fixed and without it, and proves nothing about either. That failure
        // used to be available to every row author; now it is available to this one line.
        Screen(@"<Style TargetType=""TextBlock""><Setter Property=""Opacity"" Value=""0.65"" /></Style>")
            .Descendants()
            .ShouldAllBe(element => element.Name.NamespaceName == Presentation);
    }

    /// <summary>
    /// Every shape <see cref="ChosenOn"/> has to tell apart, as fragments rather than as a sentence
    /// about them — for <see cref="WordsOrValues"/>'s reason, and because three of these were shapes
    /// the check walked past while reading eight real screens green.
    /// </summary>
    /// <remarks>
    /// Each row carries exactly one value-carrying property, and that is what makes <c>Any()</c>
    /// enough in the assertion: "something was reported" and "that value was reported" cannot come
    /// apart. A row added with two would quietly stop saying which of them was found.
    /// </remarks>
    public static TheoryData<string, bool> ChosenOrNamed() => new()
    {
        { @"<Grid Background=""#FCFCFB"" />", true },
        { @"<Style TargetType=""Button""><Setter Property=""Foreground"" Value=""#1C1B19"" /></Style>", true },
        { @"<Style TargetType=""Border""><Setter Property=""Border.Background"" Value=""#FCFCFB"" /></Style>", true },
        { @"<Style TargetType=""Button""><Setter Property=""Background""><Setter.Value>#FF112233</Setter.Value></Setter></Style>", true },
        { @"<Style TargetType=""Button""><Setter Property=""Background""><Setter.Value><StaticResource ResourceKey=""OliveBrush"" /></Setter.Value></Setter></Style>", false },
        { @"<Border><Border.Background><SolidColorBrush Color=""#FF445566"" /></Border.Background></Border>", true },
        { @"<Grid.Resources><SolidColorBrush x:Key=""MyOwnGrey"" Color=""#B9B5AC"" /></Grid.Resources>", true },
        { @"<Grid Background=""{x:Bind SomeBrushProperty}"" />", true },
        { @"<Grid Background=""{Binding SomeBrush}"" />", true },
        { @"<Grid Background=""{}{StaticResource PaperBrush}"" />", true },
        { @"<Grid Background=""{StaticResource PaperBrush}"" />", false },
        { @"<Style TargetType=""Button""><Setter Property=""Foreground"" Value=""{ThemeResource InkBrush}"" /></Style>", false },
        { @"<Grid Padding=""15,0,15,0"" />", false },
        { @"<Style TargetType=""Button""><Setter Property=""Padding"" Value=""15,0,15,0"" /></Style>", false },
    };

    [Theory]
    [MemberData(nameof(ChosenOrNamed))]
    public void A_value_a_screen_chose_is_told_from_a_key_it_named(string screen, bool chose) =>
        ChosenOn(Screen(screen)).Any().ShouldBe(chose, screen);

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_quietens_a_line_of_text_with_opacity(string path)
    {
        var faded = FadedIn(XDocument.Load(path));

        faded.ShouldBeEmpty(
            $"{Path.GetFileName(path)} quietens these with Opacity instead of naming one of the "
            + "three inks: " + string.Join("; ", faded));
    }

    /// <summary>
    /// Every line of text <paramref name="screen"/> quietens with opacity, by the element the
    /// opacity was written onto.
    /// </summary>
    /// <remarks>
    /// The three inks are what makes a second line quieter, and <c>docs/design.md</c> §Type says so
    /// outright. Opacity is a fourth grey nobody chose — and a different fourth grey over every
    /// surface it lands on, so two lines meant to read alike do not.
    /// <para>
    /// Text, and not everything. The same page gives opacity one job and names it: a source that
    /// died drops its <b>whole card</b> to 62 per cent, which is the one thing on any screen that
    /// says a thing is not there rather than that it is quiet. A ban on every element would be this
    /// check forbidding the design, and the card that draws that source would arrive to find the
    /// guard in its way.
    /// </para>
    /// <para>
    /// The <c>Setter</c> arm reads local names. It used to be <c>element.Elements("Setter")</c>,
    /// which builds a namespace-less <c>XName</c> while every element of every screen sits in the
    /// presentation namespace — so that arm had never matched anything, and a style quietening every
    /// <c>TextBlock</c> wearing it went straight through.
    /// </para>
    /// </remarks>
    private static string[] FadedIn(XDocument screen) =>
    [
        .. screen
            .Descendants()
            .Where(element => element.Name.LocalName == "TextBlock"
                || (string?)element.Attribute("TargetType") == "TextBlock")
            .Where(element => element.Attribute("Opacity") is not null
                || SettersOf(element).Any(setter => (string?)setter.Attribute("Property") == "Opacity"))
            .Select(element => element.Name.LocalName),
    ];

    /// <summary>
    /// The <c>Setter</c>s a style carries, written either way XAML allows them: as children of the
    /// <c>Style</c>, and inside the <c>&lt;Style.Setters&gt;</c> the content property can be spelt
    /// out as.
    /// </summary>
    /// <remarks>
    /// Both, and not <c>Descendants()</c>. A <c>Style</c> in a <c>TextBlock</c>'s own
    /// <c>Resources</c> is a style for something else, and descending would have this element answer
    /// for it.
    /// </remarks>
    private static IEnumerable<XElement> SettersOf(XElement style) => style
        .Elements()
        .SelectMany(child => child.Name.LocalName == "Style.Setters" ? child.Elements() : [child])
        .Where(child => child.Name.LocalName == "Setter");

    /// <summary>
    /// Every shape <see cref="FadedIn"/> has to tell apart. The second row is the one that fails on
    /// <c>main</c> as this card was written: the check it holds had never run.
    /// </summary>
    public static TheoryData<string, bool> QuietenedOrNot() => new()
    {
        { @"<TextBlock Opacity=""0.65"" />", true },
        { @"<Style TargetType=""TextBlock""><Setter Property=""Opacity"" Value=""0.65"" /></Style>", true },
        {
            @"<Style TargetType=""TextBlock""><Style.Setters>"
            + @"<Setter Property=""Opacity"" Value=""0.65"" /></Style.Setters></Style>",
            true
        },
        { @"<Border Opacity=""0.62"" />", false },
        { @"<Style TargetType=""Border""><Setter Property=""Opacity"" Value=""0.62"" /></Style>", false },
        { @"<TextBlock Text=""Hola"" />", false },
    };

    [Theory]
    [MemberData(nameof(QuietenedOrNot))]
    public void A_line_of_text_quietened_is_told_from_a_card_that_may_be(string screen, bool quietens) =>
        FadedIn(Screen(screen)).Any().ShouldBe(quietens, screen);

    public static TheoryData<string> CodeBehind() =>
        [.. AppSources.With(".cs").Select(file => file.FullName)];

    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void No_code_behind_chooses_a_colour_or_a_size_of_its_own(string path)
    {
        var chosen = ChosenIn(File.ReadAllText(path));

        chosen.ShouldBeEmpty(
            $"{Path.GetFileName(path)} chooses these in code instead of naming one of Olivo's: "
            + string.Join("; ", chosen));
    }

    /// <summary>
    /// Every colour and size <paramref name="source"/> writes into code rather than naming, as the
    /// line it stands on and what stands there.
    /// </summary>
    /// <remarks>
    /// The markup is not the only place a screen is drawn. Every card on the meetings list and every
    /// step of the save is built in code, so a check that read only <c>.xaml</c> would be a check
    /// with half the application outside it — and it was: a glyph on the saving card carried its own
    /// font size for as long as that card has existed.
    /// </remarks>
    private static string[] ChosenIn(string source) =>
    [
        .. ValueInCode()
            .Matches(source)
            .Where(match => !SourceLines.StandsInACommentedLine(source, match.Index))
            .Select(match => $"line {SourceLines.LineOf(source, match.Index)}: {match.Value.Trim()}"),
    ];

    /// <summary>
    /// Every shape <see cref="ChosenIn"/> has to tell apart, one line of C# to a row. A line and not
    /// a file: the reader is a pattern over text, and a file's worth of context proves nothing the
    /// line does not.
    /// </summary>
    /// <remarks>
    /// Each of the six reported rows reddens under exactly one edit to <see cref="ValueInCode"/>, so
    /// no arm of it is held by another arm's row. That is why the initialiser brush is
    /// <c>{ Color = chosen }</c> and <c>ColorHelper</c> stands on its own line below it: written as
    /// one row, <c>new SolidColorBrush { Color = ColorHelper.FromArgb(…) }</c>, either arm catches it
    /// and neither is proved.
    /// </remarks>
    public static TheoryData<string, bool> WrittenOrNamedInCode() => new()
    {
        { @"Glyph.FontSize = 14;", true },
        { @"var brush = new SolidColorBrush(Colors.Red);", true },
        { @"var brush = new SolidColorBrush { Color = chosen };", true },
        { @"var c = ColorHelper.FromArgb(255, 28, 27, 25);", true },
        { @"b.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14));", true },
        { @"b.Setters.Add(new Setter(Control.BackgroundProperty, ""#1C1B19""));", true },
        { @"b.Setters.Add(new Setter { Property = TextBlock.FontSizeProperty, Value = 14 });", true },
        { @"Glyph.FontSize = Sized(""DataSize"");", false },
        { @"var brush = Painted(""OliveBrush"");", false },
        { @"b.Setters.Add(new Setter(TextBlock.FontSizeProperty, Sized(""DataSize"")));", false },
        { @"b.Setters.Add(new Setter(Control.ForegroundProperty, Painted(""InkBrush"")));", false },
        {
            @"b.Setters.Add(new Setter(Control.ForegroundProperty, "
            + @"(Brush)Application.Current.Resources[""InkBrush""]));",
            false
        },
        { @"b.Setters.Add(new Setter { Property = Control.ForegroundProperty, Value = Painted(""x"") });", false },
        { @"x.Padding = new Thickness(15, 0, 15, 0);", false },
        { @"// Glyph.FontSize = 14;", false },
    };

    [Theory]
    [MemberData(nameof(WrittenOrNamedInCode))]
    public void A_value_written_into_code_is_told_from_a_key_it_named(string line, bool wrote) =>
        ChosenIn(line).Any().ShouldBe(wrote, line);

    [Fact]
    public void Every_property_that_carries_a_value_is_watched_where_a_Setter_is_built_in_code()
    {
        // `CarriesAValue` is `CarriesAValueSpelt` split, so the markup side and the code side cannot
        // name different properties. What that does not say is that a name in the alternation is
        // reachable through the arm around it — the arm could be restructured so a name in it never
        // matches, which is `Color` arriving with nothing exercising it all over again. This is what
        // says so, over every name rather than over the two the rows above happen to use.
        //
        // What neither says is that the list is long enough. It iterates the list, so a name taken
        // out of it leaves nothing behind to notice — that is one visible edit narrowing one guard,
        // and no test derived from the list can be the thing that catches it.
        foreach (var property in CarriesAValue)
        {
            ChosenIn($@"b.Setters.Add(new Setter(Control.{property}Property, ""x""));")
                .ShouldNotBeEmpty(
                    $"{property} is watched on a screen and nothing watches it where a screen builds "
                    + "a Setter in code, so the two sides have come apart.");
        }
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
        var missing = NamedButUndefinedIn(XDocument.Load(path), Keys(Olivo()));

        missing.ShouldBeEmpty(
            $"{Path.GetFileName(path)} names resources that are neither its own nor Olivo's, so "
            + "they are the platform's and the screen is not drawn from one visual system: "
            + string.Join("; ", missing));
    }

    /// <summary>
    /// Every key <paramref name="screen"/> names that neither it nor <paramref name="elsewhere"/>
    /// defines.
    /// </summary>
    /// <remarks>
    /// A screen's own keys resolve without being in <paramref name="elsewhere"/>: it keeps the styles
    /// that are only ever its, and what a meeting's row looks like is nobody else's.
    /// <para>
    /// Attributes, and no <c>Setter</c> arm. There used to be one, and it was dead for the reason
    /// <see cref="Screen"/> gives. Reviving it would have made it live and exactly redundant: a
    /// <c>Setter</c>'s <c>Value</c> is an attribute of the <c>Setter</c> element, and
    /// <c>Descendants()</c> stops at that element like any other. So it is gone, and the first row of
    /// <see cref="NamesOrDefines"/> is what holds the shape it claimed.
    /// </para>
    /// </remarks>
    private static string[] NamedButUndefinedIn(XDocument screen, IReadOnlySet<string> elsewhere)
    {
        var itsOwn = Keys(screen);

        return [.. screen
            .Descendants()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value))
            .SelectMany(value => Named().Matches(value).Select(match => match.Groups["key"].Value))
            .Where(key => !itsOwn.Contains(key) && !elsewhere.Contains(key))
            .Distinct()];
    }

    /// <summary>
    /// Every shape <see cref="NamedButUndefinedIn"/> has to tell apart, as the keys it must report
    /// and not merely as whether it reported something — <see cref="WaitsOrIsSettled"/>'s reason: the
    /// keys are what the check returns, so a row that said only "some key" would pass on the wrong
    /// one.
    /// </summary>
    /// <remarks>
    /// The theory hands these an empty set for <c>elsewhere</c> rather than Olivo's keys. A row
    /// leaning on the real dictionary would go red the day somebody added a key called <c>Nope</c>
    /// to it, and would stop being about what it says it is about.
    /// <para>
    /// The last row resolves because <see cref="Keys"/> unions <see cref="PlatformBases"/> into every
    /// document. That is this check answering "does the key exist", which it does; whether a screen
    /// is <em>allowed</em> to name a platform base — <see cref="PlatformBases"/> says nowhere but the
    /// dictionary — is a different question, and nothing in this class asks it.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string[]> NamesOrDefines() => new()
    {
        {
            @"<Style TargetType=""TextBlock"">"
            + @"<Setter Property=""Foreground"" Value=""{StaticResource Nope}"" /></Style>",
            ["Nope"]
        },
        { @"<Grid Background=""{StaticResource Nope}"" />", ["Nope"] },
        { @"<Grid Background=""{ThemeResource Nope}"" />", ["Nope"] },
        {
            @"<Grid><Grid.Resources><SolidColorBrush x:Key=""Mine"" Color=""#B9B5AC"" /></Grid.Resources>"
            + @"<Border Background=""{StaticResource Mine}"" /></Grid>",
            []
        },
        { @"<Button Style=""{StaticResource DefaultButtonStyle}"" />", [] },
    };

    [Theory]
    [MemberData(nameof(NamesOrDefines))]
    public void A_key_a_screen_names_without_defining_is_found_however_it_was_written(
        string screen, string[] missing) =>
        NamedButUndefinedIn(Screen(screen), new HashSet<string>(StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(missing, screen);

    [Fact]
    public void The_dictionary_carries_every_colour_the_design_names_at_the_value_it_names()
    {
        var page = Palette();
        page.Count.ShouldBe(12, "docs/design.md §Colour is the twelve-row table this reads.");

        var brushes = Brushes();

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
    public void The_page_sanctions_no_colour_a_screen_could_not_write()
    {
        // The other half of `No_screen_chooses_a_colour_a_size_or_a_corner_of_its_own`. That check
        // refuses a value typed onto a screen; this one refuses the page telling somebody to type
        // one. Until this existed the two contradicted each other in writing — §Colour said two
        // greys "appear inside components rather than as tokens", and the guard failed the build
        // over the first component that did it.
        var page = ColoursOnThePage();
        var dictionary = ColoursInTheDictionary();

        page.ShouldNotBeEmpty("Nothing on docs/design.md read as a colour, so this check is blind.");

        // Both sides are compared as text, so a second spelling of one colour reads as two. Six
        // digits is the only one this design has; anything else is said here rather than quietly
        // counted as a colour of its own, because the confusing failure is the one that names a
        // value nobody wrote.
        var odd = page.Concat(dictionary)
            .Where(colour => colour.Length != 7)
            .Order(StringComparer.Ordinal)
            .ToArray();

        odd.ShouldBeEmpty(
            "docs/design.md and Olivo.xaml write colours as six digits and these are not, so the "
            + "two sides cannot be compared as written: " + string.Join("; ", odd));

        var loose = page
            .Except(dictionary)
            .Except(Waiting())
            .Order(StringComparer.Ordinal)
            .ToArray();

        loose.ShouldBeEmpty(
            "docs/design.md writes these and no brush in Olivo.xaml carries them, so the page is "
            + "telling a screen to draw a colour the guard refuses. Either a brush settles it or "
            + "§Colour holds it as decided and not yet a key: " + string.Join("; ", loose));
    }

    [Fact]
    public void Nothing_waits_for_a_key_it_already_has()
    {
        // The table above empties as screens get built, and nothing else makes it. A value left
        // waiting after the screen that settled it landed is the page going on sanctioning a
        // colour somebody can now name — the same defect this pair exists to close, arriving the
        // other way round.
        var settled = Waiting()
            .Intersect(ColoursInTheDictionary())
            .Order(StringComparer.Ordinal)
            .ToArray();

        settled.ShouldBeEmpty(
            "§Colour holds these as decided and not yet a key and Olivo.xaml already carries them, "
            + "so the row belongs in the colour table and not in that one: "
            + string.Join("; ", settled));
    }

    /// <summary>
    /// Every shape <see cref="WaitingIn"/> has to tell apart, as pages rather than as a sentence
    /// about them — for <see cref="WordsOrValues"/>'s reason. Whole pages and not bare rows,
    /// because where a row stands decides as much as what it looks like: the last two rows here are
    /// correctly shaped and are not waiting, and a reader held only to the shape would sanction
    /// both. Loosening either axis is what would let a colour nobody decided count as decided, and
    /// a green run over a page that happens to hold none would not notice.
    /// </summary>
    public static TheoryData<string, string[]> WaitsOrIsSettled() => new()
    {
        { $"{WaitingSection}\n\n| `#A0567A` | the second speaker |\n", ["#A0567A"] },
        { $"{WaitingSection}\n\n| `#a0567a` | the second speaker |\n", ["#A0567A"] },
        { $"{WaitingSection}\n\n| Segundo | `#A0567A` | a dot | `SecondSpeakerBrush` |\n", [] },
        { $"{WaitingSection}\n\n| Second | `#A0567A` |\n", [] },
        { $"{WaitingSection}\n\nthe second speaker is `#A0567A` and nobody keyed it\n", [] },
        { $"{WaitingSection}\n\n## Type\n\n| `#A0567A` | the second speaker |\n", [] },
    };

    [Theory]
    [MemberData(nameof(WaitsOrIsSettled))]
    public void A_value_waiting_for_a_key_is_told_from_one_that_has_one(string page, string[] waits) =>
        WaitingIn(page).Order(StringComparer.Ordinal).ToArray().ShouldBe(waits, page);

    [Fact]
    public void A_page_with_nowhere_to_wait_is_loud_rather_than_permissive()
    {
        // The set this reader returns is subtracted from the colours the page writes, so an empty
        // one passes everything. A heading somebody renamed would otherwise turn the whole gate off
        // and read as a clean run. The type is named rather than `Exception`, because a reader that
        // threw for some other reason would satisfy a looser assertion and prove nothing.
        Should.Throw<ShouldAssertException>(
            () => WaitingIn("## Colour\n\n| `#A0567A` | the second speaker |\n"));
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
        // What pays for `ScreenTextsTests` leaving a ResourceDictionary alone. A word reaches
        // somebody through a property a screen sets, and that check refuses anything but a binding
        // naming an entry of the catalogue on every one of them. This is the other half: it cannot
        // be put here in the first place.
        //
        // The `Setter` route is `The_dictionary_settles_no_word_a_person_reads_through_a_style`'s,
        // and the two are split by subject rather than by spelling: this one refuses a `String` or a
        // `TextBlock` in here that says something, that one refuses a property this file settles
        // onto every screen wearing a style. A `Setter` is neither of those elements and a keyed
        // `<x:String>` has no property, so neither check is a second owner of the other's finding.
        //
        // Two shapes are outside both, and naming them is what stops the pair reading as exhaustive.
        // A `Setter` inside a `VisualState.Setters` names its property as
        // `Target="PlaceholderTextBlock.Text"` and carries no `Property` attribute at all. And a word
        // written straight onto a template part as an attribute — `<ContentPresenter Content="Guardar" />`
        // — is a property a person reads on an element that is neither a `String` nor a `TextBlock`,
        // so the filter above stops short of it. Both are green today and neither is reached.
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
    /// Every shape the check above has to tell apart, kept as rows rather than as a sentence about
    /// them: narrowing it from "no TextBlock at all" to "no TextBlock that says something" is
    /// exactly the edit that could let a word through, and a green run over a dictionary that
    /// happens to hold none would not notice. The inline pair is why there are five and not three
    /// — a `Run` carries most of what a `TextBlock` says and neither of its two shapes is an
    /// attribute or a text node on the element the filter stopped at.
    /// </summary>
    public static TheoryData<string, bool> WordsOrValues() => new()
    {
        { """<TextBlock Text="Elegí un idioma" />""", true },
        { """<TextBlock>Elegí un idioma</TextBlock>""", true },
        { """<TextBlock><Run Text="Elegí un idioma" /></TextBlock>""", true },
        { """<TextBlock Text="{}Elegí un idioma" />""", true },
        { """<TextBlock Text="{TemplateBinding PlaceholderText}" />""", false },
        { """<TextBlock><Run Text="{TemplateBinding PlaceholderText}" /></TextBlock>""", false },
    };

    [Theory]
    [MemberData(nameof(WordsOrValues))]
    public void A_word_is_told_from_a_value_however_it_was_written(string written, bool isAWord) =>
        Says(XElement.Parse(written)).ShouldBe(isAWord, written);

    [Fact]
    public void The_dictionary_settles_no_word_a_person_reads_through_a_style()
    {
        // The sibling of the check above, and the other half of what pays for `ScreenTextsTests`
        // leaving a ResourceDictionary alone. A word does not have to be an element in here to
        // reach somebody: a `Setter` over a property a person reads settles it onto every screen
        // wearing the style, and the screens themselves are held to the catalogue by a check that
        // never opens this file.
        var settled = SettledIn(Olivo());

        settled.ShouldBeEmpty(
            "Olivo.xaml settles these onto every screen wearing the style, so whatever they say is in "
            + "one language: " + string.Join("; ", settled));
    }

    /// <summary>
    /// Every word <paramref name="dictionary"/> settles onto a property a person reads through a
    /// <c>Setter</c>'s <c>Property</c> attribute, as <c>Property="Value"</c>.
    /// </summary>
    /// <remarks>
    /// <c>Descendants()</c> and not <see cref="SettersOf"/>. That helper exists so a style does not
    /// answer for a style nested in its own <c>Resources</c>; this starts from the document, so it
    /// reaches a <c>Setter</c> written as a child of the <c>Style</c> and one written inside
    /// <c>&lt;Style.Setters&gt;</c> alike.
    /// <para>
    /// A <c>Setter</c> inside a <c>&lt;VisualState.Setters&gt;</c> names its property with
    /// <c>Target</c> instead and is not reached — see <see cref="ScreenTextsTests.Screens"/>'s
    /// remarks, which own that sentence.
    /// </para>
    /// </remarks>
    private static string[] SettledIn(XDocument dictionary) =>
    [
        .. dictionary
            .Descendants()
            .SelectMany(element => SetBy(element, ScreenTextsTests.Reads)
                .Select(set => (On: element, set.Property, set.Value)))
            .Where(found => !PassesThrough(found.Value))
            .Select(found => $"{At(found.On)}{found.Property}=\"{found.Value}\""),
    ];

    /// <summary>
    /// Every shape <see cref="SettledIn"/> has to find, on two axes: one row per property
    /// <see cref="ScreenTextsTests.Reads"/> names, and then the spellings a <c>Setter</c> wears.
    /// </summary>
    /// <remarks>
    /// Kept as a list rather than only as a <c>TheoryData</c> so that
    /// <see cref="Every_property_a_person_reads_is_caught_by_a_setter_row"/> can ask the same rows
    /// the theory runs which properties they reach. A row per property is what stops the check from
    /// being narrowed to the one property the card that wrote it came in through.
    /// <para>
    /// The rows after the nineteen are the second axis — the spellings a <c>Setter</c> wears: a
    /// qualified property name, the <c>&lt;Style.Setters&gt;</c> spelling of the content property,
    /// the property element as a bare text node and as the idiomatic <c>&lt;x:String&gt;</c>, the
    /// <c>{}</c> escape, and the two bindings that are markup extensions a dictionary may not hand a
    /// word on through.
    /// </para>
    /// </remarks>
    private static readonly string[] WordsSettledThroughAStyle =
    [
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""Sin nombre"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""Content"" Value=""Guardar"" /></Style>",
        @"<Style TargetType=""ComboBox""><Setter Property=""Header"" Value=""Micrófono"" /></Style>",
        @"<Style TargetType=""ComboBox""><Setter Property=""Description"" Value=""El micrófono que graba tu voz"" /></Style>",
        @"<Style TargetType=""ComboBox""><Setter Property=""PlaceholderText"" Value=""Elegí un micrófono"" /></Style>",
        @"<Style TargetType=""ContentDialog""><Setter Property=""Title"" Value=""Reuniones"" /></Style>",
        @"<Style TargetType=""ContentDialog""><Setter Property=""PrimaryButtonText"" Value=""Sí, borrar"" /></Style>",
        @"<Style TargetType=""ContentDialog""><Setter Property=""SecondaryButtonText"" Value=""Cancelar"" /></Style>",
        @"<Style TargetType=""ContentDialog""><Setter Property=""CloseButtonText"" Value=""Cerrar"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""ToolTipService.ToolTip"" Value=""Grabar"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.Name"" Value=""Grabar"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.HelpText"" Value=""Empieza a grabar"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.FullDescription"" Value=""Empieza a grabar la reunión"" /></Style>",
        @"<Style TargetType=""ListView""><Setter Property=""AutomationProperties.ItemStatus"" Value=""Grabando"" /></Style>",
        @"<Style TargetType=""ListView""><Setter Property=""AutomationProperties.ItemType"" Value=""Reunión"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.LocalizedControlType"" Value=""botón"" /></Style>",
        @"<Style TargetType=""Grid""><Setter Property=""AutomationProperties.LocalizedLandmarkType"" Value=""barra"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.AcceleratorKey"" Value=""Control+G"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""AutomationProperties.AccessKey"" Value=""G"" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""TextBlock.Text"" Value=""Sin nombre"" /></Style>",
        @"<Style TargetType=""TextBlock""><Style.Setters><Setter Property=""Text"" Value=""Sin nombre"" /></Style.Setters></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text""><Setter.Value>Sin nombre</Setter.Value></Setter></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text""><Setter.Value><x:String>Sin nombre</x:String></Setter.Value></Setter></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{}{TemplateBinding PlaceholderText}"" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{Binding TheirName}"" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{x:Bind TheOtherSide}"" /></Style>",
    ];

    /// <inheritdoc cref="WordsSettledThroughAStyle"/>
    public static TheoryData<string> AWordSettledThroughAStyle() => [.. WordsSettledThroughAStyle];

    /// <summary>
    /// Every shape <see cref="SettledIn"/> has to leave alone, each of which earns its place
    /// separately.
    /// </summary>
    /// <remarks>
    /// <c>{TemplateBinding PlaceholderText}</c> is the shape the dictionary is <em>for</em> — it is
    /// how a placeholder reaches the screen through the template part named
    /// <c>PlaceholderTextBlock</c> — and a check reporting it would be a check forbidding the
    /// design. <c>{StaticResource Greeting}</c> and its <c>ThemeResource</c> twin pass because the
    /// words at the other end are <see cref="The_dictionary_names_no_word_anybody_reads"/>'s to
    /// refuse as an <c>x:String</c>, and a second refusal here would be a second owner.
    /// <c>CornerRadius</c> passes by never reaching the property filter, so it cannot go red for any
    /// edit to <see cref="PassesThrough"/>. The <c>&lt;Border /&gt;</c> row is
    /// <see cref="ValueOf"/>'s empty guard and <c>Value="   "</c> is its whitespace half — that one
    /// reddens when the guard narrows back to <c>IsNullOrEmpty</c>, which was measured. Its
    /// property-element twin does not redden for that edit, and is here saying why rather than
    /// looking like coverage it is not: the parser drops insignificant whitespace, so
    /// <see cref="ValueOf"/> is handed an empty string and never sees the spaces. The two spellings
    /// answer alike by two mechanisms and only one of them is this file's. The bare
    /// <c>TextBlock</c> is the boundary: it is a word, it is caught by the check above, and this one
    /// must not be a second owner of it.
    /// </remarks>
    public static TheoryData<string> AValueAStylePassesThrough() =>
    [
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{TemplateBinding PlaceholderText}"" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{StaticResource Greeting}"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""CornerRadius"" Value=""4"" /></Style>",
        @"<Style TargetType=""Button""><Setter Property=""Content""><Setter.Value><Border /></Setter.Value></Setter></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""{ThemeResource Greeting}"" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text"" Value=""   "" /></Style>",
        @"<Style TargetType=""TextBlock""><Setter Property=""Text""><Setter.Value>   </Setter.Value></Setter></Style>",
        @"<TextBlock Text=""Sin nombre"" />",
    ];

    [Theory]
    [MemberData(nameof(AWordSettledThroughAStyle))]
    public void A_word_settled_through_a_style_is_found_however_it_was_written(string row) =>
        SettledIn(Screen(row)).ShouldNotBeEmpty(
            "this puts a word on every screen wearing the style and nothing found it: " + row);

    [Theory]
    [MemberData(nameof(AValueAStylePassesThrough))]
    public void A_value_a_style_passes_through_is_left_alone(string row) =>
        SettledIn(Screen(row)).ShouldBeEmpty(
            "this settles no word of its own and it was reported anyway: " + row);

    [Fact]
    public void Every_property_a_person_reads_is_caught_by_a_setter_row()
    {
        // Not the direction the rows already hold. Narrowing `SetBy` reddens the theory above on
        // every row whose property it drops — measured, thirteen of them — so the rows are their own
        // guard against that. What nothing else catches is the other direction: a twentieth name
        // added to `Reads` is read by `SetBy` immediately and pinned by no row, so the set could
        // grow past its coverage and the suite would stay green. This is what says the two move
        // together, and it is named for the reader it holds because the sibling coverage Fact in
        // `ScreenTextsTests` holds a different one.
        var caught = WordsSettledThroughAStyle
            .SelectMany(row => Screen(row).Descendants())
            .SelectMany(element => SetBy(element, ScreenTextsTests.Reads))
            .Select(set => set.Property)
            .ToHashSet(StringComparer.Ordinal);

        var unpinned = ScreenTextsTests.Reads
            .Where(name => !caught.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToArray();

        unpinned.ShouldBeEmpty(
            "these are properties a person reads and no row above settles one, so this check could "
            + "stop reading it and the suite would stay green: " + string.Join("; ", unpinned));
    }

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
    /// <c>docs/design.md</c>, whole. One place and not one per check: three things in this class
    /// now read the page, and a second copy of where it is is a second thing to forget when the
    /// layout moves.
    /// </summary>
    private static string Page() => File.ReadAllText(
        AppSources.At(Path.Combine("..", "docs", "design.md")).FullName);

    /// <summary>
    /// The colour table of <c>docs/design.md</c> §Colour, as the key each row settles and the value
    /// it settles it at. Read from the page rather than written down here, because a copy of it
    /// here would be a third thing to keep in step with the two this check exists to compare.
    /// </summary>
    private static Dictionary<string, string> Palette() => PaletteRow()
        .Matches(Page())
        .ToDictionary(
            row => row.Groups["key"].Value,
            row => row.Groups["value"].Value,
            StringComparer.Ordinal);

    /// <summary>Every colour value the page writes down, wherever on it they stand.</summary>
    private static HashSet<string> ColoursOnThePage() => Colours(Page());

    /// <summary>
    /// Every colour <c>Olivo.xaml</c> carries. The brush's value and not the table's row: two of
    /// the dictionary's keys — <c>ControlRuleBrush</c> and <c>EmptyControlRingBrush</c> — were
    /// settled by a screen rather than by §Colour's table, so a check held to the table would call
    /// their values unsanctioned and be wrong about both.
    /// </summary>
    private static HashSet<string> ColoursInTheDictionary() =>
    [
        .. Brushes().Values.Select(value => value.ToUpperInvariant()),
    ];

    /// <summary>Every brush <c>Olivo.xaml</c> defines, as the key it is under and the value it is.</summary>
    private static Dictionary<string, string> Brushes() => Olivo()
        .Descendants()
        .Where(element => element.Name.LocalName == "SolidColorBrush")
        .ToDictionary(
            brush => (string?)brush.Attribute(XName.Get("Key", X)) ?? string.Empty,
            brush => (string?)brush.Attribute("Color") ?? string.Empty,
            StringComparer.Ordinal);

    /// <summary>
    /// The values §Colour holds as decided and not yet a key — the one place on the page a colour
    /// with no brush behind it is allowed to stand.
    /// </summary>
    private static HashSet<string> Waiting() => WaitingIn(Page());

    /// <summary>
    /// The waiting values of one page, read out of the body of §Colour's <c>Decided, and not yet a
    /// key</c> section and never out of the rest of the file.
    /// </summary>
    /// <remarks>
    /// Scoped rather than matched wherever the shape occurs, for the reason
    /// <c>IsaDocument.ReadSectionBody</c> is: a row shape floating over a seven-hundred-line
    /// document is a licence anybody grants by accident. <c>| Value | What it is |</c> is the most
    /// natural two-column header on a page thick with tables, and a hex in its first cell four
    /// sections away would otherwise sanction a colour nobody decided. Finding the heading is what
    /// makes the section's own sentence true, and is the blindness guard: a heading that has stopped
    /// resolving is loud rather than an empty set that passes everything.
    /// </remarks>
    private static HashSet<string> WaitingIn(string page)
    {
        var lines = page.ReplaceLineEndings("\n").Split('\n');
        var opens = Array.FindIndex(lines, line => line.StartsWith(WaitingSection, StringComparison.Ordinal));

        opens.ShouldBeGreaterThanOrEqualTo(
            0,
            $"docs/design.md has no '{WaitingSection}' heading, so there is nowhere a colour with no "
            + "brush behind it may stand and this check would pass over every one of them.");

        var body = lines
            .Skip(opens + 1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal));

        return [.. body
            .Select(line => WaitingRow().Match(line))
            .Where(row => row.Success)
            .Select(row => row.Groups["value"].Value.ToUpperInvariant())];
    }

    /// <summary>
    /// Every colour written in <paramref name="source"/>, upper-cased so one spelling is compared.
    /// </summary>
    private static HashSet<string> Colours(string source) =>
    [
        .. Hex().Matches(source).Select(found => found.Value.ToUpperInvariant()),
    ];

    /// <summary>The heading §Colour puts a decided-and-unkeyed colour under.</summary>
    private const string WaitingSection = "### Decided, and not yet a key";

    /// <summary>
    /// Whether an element carries words of its own — a <c>Text</c> that is not a binding, or text
    /// between its tags.
    /// </summary>
    /// <remarks>
    /// A markup extension is a value coming from somewhere else, and a template part carrying
    /// <c>{TemplateBinding PlaceholderText}</c> settles nothing: the words are whatever the screen
    /// put on the control, which <see cref="ScreenTextsTests"/> holds to the catalogue at the other
    /// end. Anything not in braces is this file saying it.
    /// <para>
    /// It reads what is under the element and not only the element, because a <c>TextBlock</c> says
    /// most of what it says through inlines: <c>&lt;TextBlock&gt;&lt;Run Text="Hola" /&gt;&lt;/TextBlock&gt;</c>
    /// has no text node of its own and no <c>Text</c> attribute of its own, and is a sentence on a
    /// screen. The element filter above already stops at a <c>TextBlock</c>, so this is what decides
    /// whether that <c>TextBlock</c> is holding words.
    /// </para>
    /// </remarks>
    private static bool Says(XElement element) =>
        element.DescendantNodesAndSelf().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
        || element.DescendantsAndSelf()
            .Select(inline => (string?)inline.Attribute("Text"))
            .OfType<string>()
            .Any(said => !PassesThrough(said));

    /// <summary>
    /// Whether <paramref name="value"/> is a value this file hands on rather than one it settles —
    /// one of the three markup extensions a dictionary legitimately reaches a screen through.
    /// </summary>
    /// <remarks>
    /// Not <see cref="NamesAKey"/>, and the two must not be unified though both know about
    /// <c>{}</c>. They answer different questions. On a screen every markup extension but a key is
    /// refused, because a binding hands the value to code this class cannot follow. Inside the
    /// dictionary <c>{TemplateBinding …}</c> is the whole point of a control template, so a key is
    /// not the only legitimate answer here. Collapsing them either lets a binding onto a screen or
    /// forbids a template binding in the file templates live in.
    /// <para>
    /// Three named extensions and not "anything in braces", which is what this was first written as
    /// and what let <c>&lt;Setter Property="Text" Value="{Binding TheirName}" /&gt;</c> settle a data
    /// object's words onto every screen wearing the style — the same value that
    /// <see cref="ScreenTextsTests.SaysSomethingElse"/> holds as a must-find row on a screen. A
    /// dictionary is allowed more than a screen, and it is not allowed that.
    /// </para>
    /// <para>
    /// The <c>{}</c> escape is a literal opening on a brace and passes through nothing: it is why
    /// this asks what the extension is called rather than whether the value starts with a brace.
    /// An empty value is not passed through either — <c>Text=""</c> goes on being reported exactly
    /// as it was before this existed. No emptiness rule is added here.
    /// </para>
    /// <para>
    /// What it cannot answer is whether a key it passes exists. <c>{StaticResource NoKeyLikeThis}</c>
    /// in here reaches <see cref="Every_resource_a_screen_names_is_one_that_exists"/> only through
    /// <see cref="Reached"/>, which reads every file, so an undefined key is caught as a name nobody
    /// defined and not as a word — and a key that is defined and holds a Spanish sentence is
    /// <see cref="The_dictionary_names_no_word_anybody_reads"/>'s to refuse where it is written.
    /// </para>
    /// </remarks>
    private static bool PassesThrough(string value)
    {
        var said = value.Trim();

        return said.StartsWith('{')
            && HandedOn().Match(said) is { Success: true } found
            && found.Index == 0
            && found.Length == said.Length;
    }

    /// <summary>
    /// The whole of a markup extension a dictionary may hand a value on through: a template binding,
    /// or a key resolved either way.
    /// </summary>
    /// <remarks>
    /// The whole value and not a match inside it, for <see cref="NamesAKey"/>'s reason:
    /// <c>{}{TemplateBinding PlaceholderText}</c> is a literal that reads as the sanctioned form from
    /// its third character on.
    /// </remarks>
    [GeneratedRegex(@"\{(?:TemplateBinding|StaticResource|ThemeResource)\s+[^}]+\}")]
    private static partial Regex HandedOn();

    /// <summary>
    /// The value a <c>Setter</c> carries, however it is spelt, or nothing when it writes none.
    /// </summary>
    /// <remarks>
    /// The property element is read as the text under it and not as whatever element it holds, which
    /// is what keeps <c>&lt;Setter.Value&gt;&lt;StaticResource ResourceKey="OliveBrush" /&gt;</c> —
    /// the one legitimate element spelling of a key — silent, and with it a <c>&lt;Border /&gt;</c>
    /// or any other control put there by property-element syntax. A brush declared under one is
    /// still caught, through its own <c>Color</c> attribute, exactly as a brush under a
    /// <c>&lt;Border.Background&gt;</c> is.
    /// <para>
    /// Whitespace answers nothing whichever way it is spelt. <c>Value="   "</c> and
    /// <c>&lt;Setter.Value&gt;   &lt;/Setter.Value&gt;</c> settle the same nothing, and one of them
    /// reporting while the other stayed silent would be the two-spellings-of-one-shape defect this
    /// reader exists to remove.
    /// </para>
    /// <para>
    /// A <c>Setter</c> carrying both spellings at once is not legal XAML and does not reach a
    /// screen, so nothing here has to choose between them on the merits; the attribute wins because
    /// it is asked first.
    /// </para>
    /// </remarks>
    private static string? ValueOf(XElement setter)
    {
        var written = (string?)setter.Attribute("Value")
            ?? setter.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "Setter.Value")
                ?.Value;

        return string.IsNullOrWhiteSpace(written) ? null : written.Trim();
    }

    /// <summary>
    /// Whether <paramref name="property"/>, as a <c>Setter</c> spells it, is one of
    /// <paramref name="properties"/>.
    /// </summary>
    /// <remarks>
    /// The whole string first and the part after the last dot second, and that order is
    /// load-bearing. <c>Property="Border.Background"</c> is legal, is the required spelling for an
    /// attached property, and sets exactly what <c>Property="Background"</c> sets — so a plain
    /// string comparison against the set is a hole with a qualifier in front of it. But
    /// <see cref="ScreenTextsTests.Reads"/> holds <c>AutomationProperties.Name</c> and
    /// <c>ToolTipService.ToolTip</c> as dotted names of their own, and eleven of its nineteen
    /// entries are dotted that way, so a last-dot match alone would reduce them to <c>Name</c> and
    /// <c>ToolTip</c> and lose all eleven.
    /// <para>
    /// <c>internal</c> because <see cref="ScreenTextsTests.SetsAWordAPersonReads"/> asks the same
    /// question of a <c>Setter</c> on a screen, and an exact match there while this one resolves the
    /// qualifier is two spellings of one rule in one assembly — the shape a qualified property would
    /// have slipped through. That makes the lending two-directional: this class borrows
    /// <see cref="ScreenTextsTests.Reads"/> and lends this back. Two shared pieces travelling both
    /// ways is the point at which they belong in a class of their own beside <c>AppSources</c> and
    /// <c>SourceLines</c>, which is a move this card did not make and the next reader of either
    /// class should.
    /// </para>
    /// </remarks>
    internal static bool IsOneOf(string property, IReadOnlySet<string> properties) =>
        properties.Contains(property)
        || properties.Contains(property[(property.LastIndexOf('.') + 1)..]);

    /// <summary>
    /// What a <c>Setter</c> puts on one of <paramref name="properties"/>, and nothing when it sets
    /// none.
    /// </summary>
    /// <remarks>
    /// The property set is an argument because two checks ask this same question of two different
    /// sets: <see cref="Values"/> asks it about the colours, sizes and corners of ISC-173.1, and
    /// <see cref="SettledIn"/> about the words a person reads. One <c>Setter</c> shape, read once.
    /// <para>
    /// The set is tested <em>before</em> <see cref="ValueOf"/> is asked, and reordering those two
    /// conditions would have this read a whole <c>ControlTemplate</c>'s text as a colour —
    /// Olivo.xaml holds three <c>&lt;Setter.Value&gt;</c> elements and every one of them wraps a
    /// template or a panel, under a property in neither set.
    /// </para>
    /// </remarks>
    private static IEnumerable<(string Property, string Value)> SetBy(
        XElement element, IReadOnlySet<string> properties)
    {
        if (element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") is { } property
            && IsOneOf(property, properties)
            && ValueOf(element) is { } value)
        {
            yield return (property, value);
        }
    }

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
    /// <remarks>
    /// The <c>Setter</c> shape is <see cref="SetBy"/>'s, so the property is matched
    /// <see cref="IsOneOf"/>'s way and the value read <see cref="ValueOf"/>'s way — which is what
    /// makes <c>&lt;Setter Property="Background"&gt;&lt;Setter.Value&gt;#FF112233&lt;/Setter.Value&gt;&lt;/Setter&gt;</c>
    /// a colour a screen chose rather than a colour nothing reads.
    /// </remarks>
    private static IEnumerable<(string Property, string Value)> Values(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(a => CarriesAValue.Contains(a.Name.LocalName)))
        {
            yield return (attribute.Name.LocalName, attribute.Value);
        }

        foreach (var set in SetBy(element, CarriesAValue))
        {
            yield return set;
        }
    }

    private static bool IsTheDictionary(FileInfo file) =>
        file.Name.Equals("Olivo.xaml", StringComparison.Ordinal);

    /// <summary>The dictionary, carrying the line each element stands on.</summary>
    /// <remarks>
    /// <see cref="LoadOptions.SetLineInfo"/> for <see cref="SettledIn"/>'s sake: Olivo.xaml is
    /// hundreds of lines of <c>Setter</c>s, and two of the spellings that check must find report a
    /// string that does not occur in the file at all — the words under a
    /// <c>&lt;Setter.Value&gt;</c> come back as <c>Text="Sin nombre"</c>, which is not greppable.
    /// It costs the option on one loader and changes no reader's verdict.
    /// </remarks>
    private static XDocument Olivo() => XDocument.Load(
        AppSources.With(".xaml").Single(IsTheDictionary).FullName, LoadOptions.SetLineInfo);

    /// <summary>The line <paramref name="element"/> stands on, or nothing when it was parsed from a
    /// row rather than loaded from the file.</summary>
    private static string At(XElement element) =>
        ((IXmlLineInfo)element).HasLineInfo() ? $"line {((IXmlLineInfo)element).LineNumber}: " : string.Empty;

    private static string Project() => File.ReadAllText(
        AppSources.At(Path.Combine("MeetingTranscriber.App", "MeetingTranscriber.App.csproj")).FullName);

    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The namespace every element of every screen is in.</summary>
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>A resource named in markup, and the key it names.</summary>
    /// <remarks>
    /// This is the detector and the authorizer both: <see cref="NamedButUndefinedIn"/> and
    /// <see cref="Reached"/> use it to find every key anybody named, and <see cref="NamesAKey"/>
    /// uses it to decide what a screen is allowed to write. So widening it — a dotted key, a
    /// trailing <c>, Mode=…</c> — is not only "the check finds more", it is also "a screen may
    /// write more", and nothing here goes red to say so.
    /// </remarks>
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

    /// <summary>
    /// A colour written out, however many digits somebody wrote. Whole: the boundaries are what
    /// stop an eight-digit <c>#AARRGGBB</c> being read as its first six, which would sanction a
    /// value no brush carries or report one under a name nobody typed. Six digits is the only
    /// spelling this design has, and
    /// <see cref="The_page_sanctions_no_colour_a_screen_could_not_write"/> is where that is held
    /// rather than here, so anything else is loud instead of silently truncated.
    /// </summary>
    [GeneratedRegex(@"(?<![0-9A-Fa-f])#[0-9A-Fa-f]{3,8}(?![0-9A-Fa-f])")]
    private static partial Regex Hex();

    /// <summary>
    /// A row of §Colour's *Decided, and not yet a key* table: the value, and the sentence saying
    /// what it is. Two columns and the value first, which is deliberately not
    /// <see cref="PaletteRow"/>'s four-column shape — a row that matched both would be a colour
    /// claiming a key and waiting for one at the same time, and it would also push
    /// <c>Palette()</c> past twelve. Where the row stands is <see cref="WaitingIn"/>'s to decide;
    /// this only says what one looks like.
    /// </summary>
    [GeneratedRegex(@"^\|\s*`(?<value>#[0-9A-Fa-f]{3,8})`\s*\|[^|]+\|\s*$")]
    private static partial Regex WaitingRow();

    [GeneratedRegex(@"EaseInOut|BounceEase|ElasticEase|BackEase")]
    private static partial Regex EasesBothWays();

    /// <summary>
    /// A colour or a size written into code rather than named. The brush shapes are what building
    /// a colour from nothing looks like; <c>FontSize</c> is what a rank looks like when it is a
    /// number. A key read out of the dictionary is a name and never matches these.
    /// </summary>
    /// <remarks>
    /// <c>new SolidColorBrush</c> ends on a boundary and not on a bracket, because the
    /// object-initialiser form <c>new SolidColorBrush { Color = … }</c> has a brace where a bracket
    /// was demanded, and building a brush in code is the failure whichever way it is spelt.
    /// <c>ColorHelper.FromArgb(…)</c> is the idiomatic WinUI spelling and was outside the old
    /// <c>Color(s)?</c> arm entirely.
    /// <para>
    /// The last two arms are a <c>Setter</c> built in code, in its two spellings:
    /// <c>new Setter(TextBlock.FontSizeProperty, 14)</c> sets a size with no <c>=</c> anywhere in it,
    /// so the first arm never sees one, and <c>new Setter { Property = …, Value = 14 }</c> is the
    /// same failure through the initialiser — the same pair the brush arm above answers for.
    /// <c>[\w.]*</c> takes the qualifier and backtracks so the property name matches.
    /// </para>
    /// <para>
    /// What tells a value from a name is <see cref="ThroughOlivo"/> after the value opens. It has to
    /// be read on the character the value starts at, and the whitespace before it is therefore
    /// atomic: a plain <c>\s*</c> hands the space back and lets the lookahead read <em>it</em>
    /// instead of the character behind it, which passes every sanctioned call as a failure. That was
    /// the shape this arm was first written in, and it reported
    /// <c>new Setter(TextBlock.FontSizeProperty, Sized("DataSize"))</c> — which is the route the
    /// class exists to sanction.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"FontSize\s*=\s*\d"
        + @"|new\s+SolidColorBrush\b"
        + @"|Color(?:s|Helper)?\.From[A-Za-z]*\s*\("
        + @"|Colors\.[A-Za-z]"
        + @"|new\s+Setter\s*\(\s*[\w.]*(?:" + CarriesAValueSpelt + @")Property\s*,(?>\s*)" + ThroughOlivo
        + @"|new\s+Setter\s*\{[^}]*(?:" + CarriesAValueSpelt + @")Property[^}]*Value\s*=(?>\s*)" + ThroughOlivo)]
    private static partial Regex ValueInCode();

    /// <summary>
    /// What a value handed to a <c>Setter</c> built in code looks like when it did <em>not</em> come
    /// through Olivo — read as: the value does not open on one of the sanctioned routes.
    /// </summary>
    /// <remarks>
    /// A letter or an underscore opens a call or a member, which is <c>Sized("DataSize")</c>,
    /// <c>Painted("InkBrush")</c> or a field holding one of those. <c>@</c> opens a verbatim
    /// identifier, which is the same thing spelt round a keyword. <c>(</c> opens a cast, and
    /// <c>(Brush)Application.Current.Resources[key]</c> is verbatim the body of this application's
    /// own <c>Painted</c> — refusing it would put the guard in front of the route it sanctions,
    /// which is how a guard stops being believed.
    /// <para>
    /// So <c>14</c> and <c>"#1C1B19"</c> are what is left, and they are the failure. What this buys
    /// the miss with is one level of indirection: <c>var size = 14;</c> and then <c>size</c> passes,
    /// because it opens on a letter like every sanctioned value does. Telling those apart is a
    /// scanner and not a pattern, and the smaller mistake is the one that under-reports —
    /// <c>SourceLines</c> makes the same call for the same reason.
    /// </para>
    /// </remarks>
    private const string ThroughOlivo = @"(?![A-Za-z_@(])";

    /// <summary>A resource asked for by name from code.</summary>
    [GeneratedRegex(@"(?:Resources\[|Painted\(|Chrome\(|Sized\()""(?<key>\w+)""")]
    private static partial Regex KeyInCode();

    /// <summary>Where the dictionary settles a key, as opposed to where anything uses one.</summary>
    [GeneratedRegex(@"x:Key=""\w+""")]
    private static partial Regex Defines();
}
