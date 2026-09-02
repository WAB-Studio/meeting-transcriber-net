using System.Text.RegularExpressions;
using System.Xml;
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
/// <para>
/// The markup half is held the same way, by <see cref="NamesAnEntry"/>,
/// <see cref="CarriesNoWords"/> and <see cref="SaysSomethingElse"/>, and for the same reason: it ran
/// green over a binding pointing anywhere it liked, because the four screens only ever pointed one
/// at the catalogue and a check with nothing to find reads like a check that works.
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

    /// <summary>
    /// The screens. A <c>ResourceDictionary</c> is not one of them and is left out: it holds
    /// values — a colour, a corner, a type rank — and the two checks below are about words a person
    /// reads, so over Olivo.xaml they would go red over <c>&lt;CornerRadius&gt;4&lt;/CornerRadius&gt;</c>.
    /// </summary>
    /// <remarks>
    /// One route out of a dictionary is closed by the first check below, which takes one entry of
    /// the catalogue and nothing else — so <c>{StaticResource Greeting}</c> is not a literal one
    /// indirection away — and <c>OlivoTests</c> refuses to let a sentence be written in there as a
    /// <c>String</c> or a <c>TextBlock</c> at all.
    /// <para>
    /// The route neither of them closes is a <c>Setter</c>: <c>&lt;Setter Property="Text"
    /// Value="Sin nombre" /&gt;</c> inside a style in Olivo.xaml reaches every screen wearing that
    /// style, and <c>OlivoTests</c> reads the <c>Setter</c> shape only for the colours, sizes and
    /// corners of ISC-173.1. Closing it belongs to that class, which already has the shape to do it
    /// with, and it is #182's <c>left_out</c>. What is closed here is the same shape in a screen's
    /// own resources, which the check below does read.
    /// </para>
    /// </remarks>
    public static TheoryData<string> Screens() =>
    [
        .. AppSources.With(".xaml")
            .Where(file => XDocument.Load(file.FullName).Root?.Name.LocalName != "ResourceDictionary")
            .Select(file => file.FullName),
    ];

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
        var carried = NotNamingTheCatalogueIn(XDocument.Load(path, LoadOptions.SetLineInfo));

        carried.ShouldBeEmpty(
            $"{Path.GetFileName(path)} gets these from somewhere other than UiTexts, so whatever "
            + "they say is in one language: " + string.Join("; ", carried));
    }

    /// <summary>
    /// The words a person reads that <paramref name="screen"/> gets from anywhere but the
    /// catalogue, each with the line it is on — the shape the code-behind halves answer in, for the
    /// reason <see cref="LiteralsIn"/> gives: a screen is hundreds of lines long, and the finding
    /// this now reports is a near-miss among twenty bindings spelt almost the same, where the old
    /// one was a bare literal somebody could pick out by eye.
    /// </summary>
    /// <remarks>
    /// Two shapes, because a screen sets a property two ways. The attribute is the obvious one; a
    /// <c>Setter</c> inside the screen's own <c>Resources</c> is the same property set through a
    /// style, and there are fifteen of them across these screens today. <c>OlivoTests.Values</c>
    /// reads both for the same reason over the colours and corners it holds.
    /// <para>
    /// A <c>Setter</c> spelling its value as <c>&lt;Setter.Value&gt;Sin nombre&lt;/Setter.Value&gt;</c>
    /// is not a third: that is words between tags, and
    /// <see cref="No_screen_writes_words_between_its_tags"/> is what reads them — for every element
    /// and not only a <c>Setter</c>. Rows for it are that check's, in
    /// <see cref="WordsBetweenTags"/>.
    /// </para>
    /// </remarks>
    private static string[] NotNamingTheCatalogueIn(XDocument screen) =>
    [
        .. screen
            .Descendants()
            .SelectMany(SetsAWordAPersonReads)
            .Where(set => !NamesAnEntryOfTheCatalogue(set.On, set.Value))
            .Select(set => $"line {At(set.On)}: {set.Property}=\"{set.Value}\""),
    ];

    /// <summary>
    /// Where <paramref name="element"/> puts words a person reads: one of <see cref="Reads"/> as an
    /// attribute, and the <c>Setter</c> that says the same thing through a style. The element comes
    /// back with each, because what the prefix in a binding resolves to is decided where the
    /// binding stands.
    /// </summary>
    private static IEnumerable<(XElement On, string Property, string Value)> SetsAWordAPersonReads(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(a => Reads.Contains(a.Name.LocalName)))
        {
            yield return (element, attribute.Name.LocalName, attribute.Value);
        }

        if (element.Name.LocalName == "Setter"
            && (string?)element.Attribute("Property") is { } property
            && Reads.Contains(property)
            && (string?)element.Attribute("Value") is { } value)
        {
            yield return (element, property, value);
        }
    }

    /// <summary>The line <paramref name="element"/> stands on, which is why the screen is loaded
    /// with <see cref="LoadOptions.SetLineInfo"/>.</summary>
    private static int At(XElement element) => ((IXmlLineInfo)element).LineNumber;

    /// <summary>
    /// Whether <paramref name="value"/>, standing on <paramref name="on"/>, is the one form a
    /// screen may put words on a person's screen in: <c>{x:Bind In(loc:UiTexts.Something)}</c>,
    /// whole.
    /// </summary>
    /// <remarks>
    /// The whole form and not the <c>{x:Bind</c> it opens with. A binding is an indirection, and
    /// what it points at is a member of whatever the binding resolves against; the sibling check
    /// over the code-behind only reads the properties named in <see cref="Reads"/>, so a member
    /// with any other name returning a literal was seen by neither half and a screen reader said
    /// it in one language.
    /// <para>
    /// A binding may name nothing else — not a resource, not a property path, not a second clause.
    /// A screen wanting one of those is a screen wanting to say something the catalogue does not
    /// carry, which is the defect this exists for; the day one is legitimate, this is one edit and
    /// the edit is somebody's decision. Two of those days are worth expecting. A <c>DataTemplate</c>
    /// with an <c>x:DataType</c> resolves <c>x:Bind</c> against the data and not the page, so the
    /// one form here is not expressible inside one and no screen has written one yet; and
    /// <see cref="Reads"/> holds three names — <c>Content</c>, <c>Header</c>, <c>Description</c> —
    /// that can carry a <c>UIElement</c> rather than words, which is why what this reports says the
    /// words come from outside the catalogue rather than that the screen typed them.
    /// </para>
    /// <para>
    /// Refusing a second clause is what keeps every catalogue binding <c>OneTime</c>, which is what
    /// makes the <c>Bindings.Update()</c> each screen calls the one thing that repaints it when the
    /// language changes.
    /// </para>
    /// <para>
    /// The catalogue's prefix is resolved against the screen rather than spelt <c>loc</c> here,
    /// because it is the namespace that says this is the catalogue: a screen is free to call the
    /// prefix what it likes, and one that pointed <c>loc</c> at some other namespace would otherwise
    /// read as naming a catalogue entry while naming a stranger's. <c>x</c> is spelt and not
    /// resolved, which is the asymmetry worth stating: the prefix on the XAML language namespace is
    /// arbitrary in principle, and in fact it is <c>x</c> in every XAML file these tools write. A
    /// screen that renamed it would go red here rather than through, which is the direction a guard
    /// should fail in, and resolving it would put a declaration in every row below that has nothing
    /// to do with what the row is for.
    /// </para>
    /// </remarks>
    private static bool NamesAnEntryOfTheCatalogue(XElement on, string value)
    {
        var named = TheCatalogueBindingForm().Match(value);

        return named.Success
            && on.GetNamespaceOfPrefix(named.Groups["catalogue"].Value)?.NamespaceName == Catalogue;
    }

    /// <summary>
    /// Where the catalogue lives, spelt from the type itself so that moving it is the compiler's
    /// failure and not four screens reading as monolingual. <c>using:</c> is how XAML spells a CLR
    /// namespace and is the only spelling these screens use; a screen reaching for another would go
    /// red here, which is the direction a guard should fail in.
    /// </summary>
    private static readonly string Catalogue = "using:" + typeof(UiTexts).Namespace;

    /// <summary>
    /// A binding naming one entry of the catalogue, and the whole of the value. Anchored with
    /// <c>\A</c> and <c>\z</c> rather than <c>^</c> and <c>$</c>, which in .NET would also match
    /// before a trailing newline, so that a second clause — another binding, <c>, Mode=OneWay</c>,
    /// a word after the brace — is a different form and is refused as one.
    /// </summary>
    /// <remarks>
    /// <c>UiTexts</c> comes from the type and not a spelling, so that renaming the catalogue is a
    /// build failure here rather than four screens going red as if they had gone monolingual.
    /// </remarks>
    [GeneratedRegex(@"\A\s*\{\s*x:Bind\s+In\(\s*(?<catalogue>[A-Za-z_][\w.-]*):"
        + nameof(UiTexts) + @"\.[A-Za-z_]\w*\s*\)\s*\}\s*\z")]
    private static partial Regex TheCatalogueBindingForm();

    /// <summary>
    /// Screens that name an entry of the catalogue, which this must leave alone. Rows and not only
    /// the four real screens, for the reason <see cref="WordsAPersonWouldRead"/> gives about the
    /// code-behind half: a check held only to what the application happens to say today is a check
    /// nobody notices has stopped being the one described here.
    /// </summary>
    /// <remarks>
    /// The second row declares the prefix on an ancestor, which is where all four screens declare
    /// it and so the only resolution the application actually exercises.
    /// </remarks>
    public static TheoryData<string> NamesAnEntry() =>
    [
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" Text=""{x:Bind In(loc:UiTexts.Record)}"" />",
        @"<Grid xmlns:loc=""using:MeetingTranscriber.Presentation""><TextBlock Text=""{x:Bind In(loc:UiTexts.Record)}"" /></Grid>",
        @"<Button xmlns:loc=""using:MeetingTranscriber.Presentation"" AutomationProperties.Name=""{x:Bind In(loc:UiTexts.Record)}"" />",
        @"<TextBlock xmlns:texts=""using:MeetingTranscriber.Presentation"" Text=""{x:Bind In(texts:UiTexts.Record)}"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" Text=""{x:Bind In(loc:UiTexts.Record) }"" />",
        @"<Setter xmlns:loc=""using:MeetingTranscriber.Presentation"" Property=""Text"" Value=""{x:Bind In(loc:UiTexts.Record)}"" />",
    ];

    /// <summary>
    /// Screens carrying nothing anybody reads, which this must not report. Its own set and not a
    /// row among the ones above, because it passes by the <see cref="Reads"/> filter never reaching
    /// it rather than by naming an entry — so it cannot go red for any edit to the form, and a row
    /// that cannot go red for the thing around it is coverage in appearance only.
    /// </summary>
    public static TheoryData<string> CarriesNoWords() =>
    [
        @"<Button Style=""{StaticResource NormalButtonOnPaper}"" Grid.Row=""0"" />",
        @"<Setter Property=""CornerRadius"" Value=""4"" />",
    ];

    /// <summary>
    /// Screens that put words in front of somebody without naming an entry of the catalogue. The
    /// fourth is what #182 planted on the recorder and watched stay green; the rest are the forms a
    /// binding can wear while pointing somewhere the catalogue is not.
    /// </summary>
    /// <remarks>
    /// Three earn their place over the others. <c>{}</c> is XAML's escape for a value that opens
    /// with a brace, so the second row is a literal that reads as the good form for its first two
    /// characters. The seventh points a well-spelt prefix at another namespace and the eighth
    /// shadows a good outer declaration with a bad inner one, which are the two shapes resolving
    /// the prefix exists to tell apart. The last is a <c>Setter</c>, which reaches a screen through
    /// a style rather than through an attribute.
    /// </remarks>
    public static TheoryData<string> SaysSomethingElse() =>
    [
        @"<TextBlock Text=""Grabar una reunión"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" Text=""{}{x:Bind In(loc:UiTexts.Record)}"" />",
        @"<TextBlock Text=""{StaticResource Greeting}"" />",
        @"<TextBlock Text=""{Binding Name}"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" AutomationProperties.Name=""{x:Bind TheOtherSide}"" />",
        @"<TextBlock Text=""{x:Bind In(TheOtherSide)}"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" Text=""{x:Bind In(loc:Elsewhere.Record)}"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.App"" Text=""{x:Bind In(loc:UiTexts.Record)}"" />",
        @"<Grid xmlns:loc=""using:MeetingTranscriber.Presentation""><TextBlock xmlns:loc=""using:MeetingTranscriber.App"" Text=""{x:Bind In(loc:UiTexts.Record)}"" /></Grid>",
        @"<TextBlock Text=""{x:Bind In(loc:UiTexts.Record)}"" />",
        @"<TextBlock xmlns:loc=""using:MeetingTranscriber.Presentation"" Text=""{x:Bind In(loc:UiTexts.Record), Mode=OneWay}"" />",
        @"<Setter xmlns:loc=""using:MeetingTranscriber.Presentation"" Property=""Text"" Value=""Sin nombre"" />",
    ];

    [Theory]
    [MemberData(nameof(NamesAnEntry))]
    public void A_screen_naming_an_entry_of_the_catalogue_is_left_alone(string screen) =>
        NotNamingTheCatalogueIn(Parse(screen)).ShouldBeEmpty(
            "this names an entry of the catalogue and was reported anyway: " + screen);

    [Theory]
    [MemberData(nameof(CarriesNoWords))]
    public void A_screen_carrying_nothing_anybody_reads_is_left_alone(string screen) =>
        NotNamingTheCatalogueIn(Parse(screen)).ShouldBeEmpty(
            "nobody reads anything on this and it was reported anyway: " + screen);

    [Theory]
    [MemberData(nameof(SaysSomethingElse))]
    public void A_screen_naming_anything_else_is_found(string screen) =>
        NotNamingTheCatalogueIn(Parse(screen)).ShouldNotBeEmpty(
            "this puts words on a screen from outside the catalogue and nothing found it: " + screen);

    /// <summary>
    /// A row as the check reads a screen — line info and all, so that a row exercises the same
    /// load a real screen gets and not a second one that could drift from it.
    /// </summary>
    private static XDocument Parse(string screen) => XDocument.Parse(screen, LoadOptions.SetLineInfo);

    [Theory]
    [MemberData(nameof(Screens))]
    public void No_screen_writes_words_between_its_tags(string path)
    {
        var written = WordsBetweenTagsIn(XDocument.Load(path));

        written.ShouldBeEmpty(
            $"{Path.GetFileName(path)} writes words between its tags instead of binding an entry "
            + "of UiTexts: " + string.Join("; ", written));
    }

    /// <summary>
    /// The words <paramref name="screen"/> writes between its own tags. The same literal as the
    /// attribute check's wearing the other syntax: <c>&lt;TextBlock&gt;Listo&lt;/TextBlock&gt;</c>
    /// sets the very property the attribute would have, and no attribute check can see it.
    /// </summary>
    private static string[] WordsBetweenTagsIn(XDocument screen) =>
    [
        .. screen
            .Descendants()
            .SelectMany(element => element.Nodes().OfType<XText>().Select(text => (element, text)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.text.Value))
            .Select(pair => $"<{pair.element.Name.LocalName}>{pair.text.Value.Trim()}"),
    ];

    /// <summary>
    /// Screens writing words between their tags, which this must find. Rows and not only the real
    /// screens, for the reason <see cref="WordsAPersonWouldRead"/> gives: no screen has ever written
    /// one, so until now this check had no subject at all and read exactly like a check that works.
    /// </summary>
    /// <remarks>
    /// The last two are a <c>Setter</c> spelling its value as a property element, which is legal
    /// XAML and is a word a person reads. That shape is here and not in
    /// <see cref="SaysSomethingElse"/> because this is the check that stops it: reading a
    /// <c>Setter</c>'s <c>Value</c> attribute a second time as an element would be a second owner
    /// for the same rule, and this one already holds it for every element rather than for a
    /// <c>Setter</c> alone. <c>&lt;x:String&gt;</c> is the idiomatic way to put a string in a
    /// property element and is caught by the same text node.
    /// </remarks>
    public static TheoryData<string> WordsBetweenTags() =>
    [
        @"<TextBlock>Listo</TextBlock>",
        @"<Grid><TextBlock>Listo</TextBlock></Grid>",
        @"<Setter Property=""Text""><Setter.Value>Sin nombre</Setter.Value></Setter>",
        @"<Setter Property=""Text""><Setter.Value><x:String xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">Sin nombre</x:String></Setter.Value></Setter>",
    ];

    /// <summary>
    /// Screens with nothing between their tags, which this must leave alone — every screen in the
    /// application is this shape. The words in the first are in an attribute, which is the sibling
    /// check's to refuse and not this one's; the second is a property element wrapping a control,
    /// which is what property-element syntax exists for and says nothing of its own.
    /// </summary>
    public static TheoryData<string> NothingBetweenTags() =>
    [
        @"<TextBlock Text=""Grabar una reunión"" />",
        @"<Setter Property=""Content""><Setter.Value><Border Grid.Row=""0"" /></Setter.Value></Setter>",
    ];

    [Theory]
    [MemberData(nameof(WordsBetweenTags))]
    public void Words_between_a_screen_s_tags_are_found(string screen) =>
        WordsBetweenTagsIn(Parse(screen)).ShouldNotBeEmpty(
            "this puts words on a screen between its tags and nothing found it: " + screen);

    [Theory]
    [MemberData(nameof(NothingBetweenTags))]
    public void A_screen_with_nothing_between_its_tags_is_left_alone(string screen) =>
        WordsBetweenTagsIn(Parse(screen)).ShouldBeEmpty(
            "there are no words between these tags and it was reported anyway: " + screen);

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
            .Where(match => !SourceLines.StandsInACommentedLine(source, match.Index))
            .Select(match => $"line {SourceLines.LineOf(source, match.Index)}: "
                + string.Join(' ', match.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))),
    ];

    // Whether a finding stands on a line that is all comment, and which line it is on, are
    // SourceLines' — the same two answers every guard in this project that greps the application's
    // own source needs, and the reason they are one place is written there.

    /// <summary>
    /// The shapes a word a person reads is written in. Held here and not only against the screens,
    /// for the reason <see cref="There_are_screens_to_check"/> already gives about the file list:
    /// this check ran green over eleven hundred lines of code-behind for as long as they existed,
    /// because the one shape it could not see was the one they were all written in and there was
    /// nothing else for it to find. A guard whose subject is absent reads exactly like a guard that
    /// works. These are the subject, kept where an edit to the pattern runs into them.
    /// </summary>
    /// <remarks>
    /// One row per property of <see cref="Reads"/> at least, which is what
    /// <see cref="Every_property_a_person_reads_is_caught_by_a_literal_row"/> holds this to. Most of
    /// them had none, and the pattern is built out of that same set, so taking <c>Title</c> or
    /// <c>Content</c> out of it stopped both halves seeing them and reddened nothing. The rest of
    /// the rows are the shapes an assignment wears, which is a second axis over the same list and
    /// why there are more rows than properties.
    /// </remarks>
    public static TheoryData<string> WordsAPersonWouldRead() => [.. WordsOnScreen];

    /// <summary>These as a list, so that the <c>Fact</c> below can ask what they catch.</summary>
    private static readonly string[] WordsOnScreen =
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
        @"Title = ""Reuniones"";",
        @"Save.Content = ""Guardar"";",
        @"Microphone.Description = ""El micrófono que graba tu voz"";",
        @"Search.PlaceholderText = ""Buscar una reunión"";",
        @"Confirm.PrimaryButtonText = ""Sí, borrar"";",
        @"Confirm.SecondaryButtonText = ""Cancelar"";",
        @"Confirm.CloseButtonText = ""Cerrar"";",
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
    /// about types that wants an analyser. A switch expression is the fifth, and a comment opened
    /// after code on the same line is the one false red left standing rather than a miss.
    /// <para>
    /// The fourth is the same gap as the one under it and is written down separately because of
    /// what it is: <c>In</c> is now the one member every word on every screen passes through, so
    /// where the shape above loses one string this one loses a whole screen. It is out of reach for
    /// the reason an expression-bodied member always was — there is no assignment to find it by —
    /// and it is what the markup half trusts once it has held a binding to naming a catalogue
    /// entry. Nothing in the repo asserts the four screens spell it the same or return what it
    /// says; that is #182's <c>left_out</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<string> OutOfReachOnPurpose() =>
    [
        @"Show(""Listo"");",
        @"StatusText.Text = Fmt(""Listo"");",
        @"StatusText.Text = ready ? Fmt($""{count}"") : ""nada"";",
        @"public string In(UiText text) => ""Grabar una reunión"";",
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
            "this row says the check does not reach this, and it did, so the row and the check "
            + "no longer agree: " + written);

    /// <summary>
    /// Every spelling C# has for a literal, with the words it puts in front of somebody. Being found
    /// is half of what a row here says and being quoted back is the other: a finding reading
    /// <c>Text = ""</c> has seen a literal and cannot say which one, which is what a raw string used
    /// to report — the pattern matched the empty pair at its head and stopped.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="WordsAPersonWouldRead"/> rather than folded into it. That set is
    /// one row per property and per shape of assignment; how the literal itself is spelt is a third
    /// axis, and crossing the three would be a hundred rows saying what these eleven do. It is also
    /// the stricter of the two assertions — a row here has to be quoted back with its words, where a
    /// row there only has to be found — which is deliberate: what a spelling can go wrong in is
    /// being read partly.
    /// <para>
    /// Every alternative and every quantifier in <see cref="Literal"/> has a row that narrowing it
    /// reddens, which is what stops this being a set that grew a spelling and pinned none of it.
    /// The verbatim row with an escaped quote in it is what tells the verbatim alternative from the
    /// plain one, which stopped at that quote and quoted back <c>@"dijo "</c>. The raw row with a
    /// four-quote delimiter over content holding <c>"""</c> is the only thing that tells
    /// <c>{3,}</c> from <c>{3}</c> — a longer delimiter over content that does not need one reads
    /// the same either way. The raw row over several lines is the only thing that tells
    /// <c>[\s\S]</c> from <c>.</c>, and several lines is what raw strings are for. And
    /// <c>$$""" """</c> is the only thing between <c>\$*</c> and <c>\$?</c>.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> EverySpellingOfALiteral() => new()
    {
        { @"StatusText.Text = ""hola"";", "hola" },
        { @"StatusText.Text = @""hola"";", "hola" },
        { @"StatusText.Text = $""hola {count}"";", "hola {count}" },
        { @"StatusText.Text = $@""hola {count}"";", "hola {count}" },
        { @"StatusText.Text = @""dijo """"hola"""""";", @"dijo """"hola""""" },
        { """"StatusText.Text = """hola""";"""", "hola" },
        { """""StatusText.Text = """"cierra con """ dentro"""";""""", """"cierra con """ dentro"""" },
        { """"StatusText.Text = $$"""hola {{count}}""";"""", "hola" },
        { "StatusText.Text = \"\"\"\n            hola\n            \"\"\";", "hola" },
        { """"StatusText.Text = $"""hola {count}""";"""", "hola {count}" },
        { """"AutomationProperties.SetName(box, """hola""");"""", "hola" },
    };

    [Theory]
    [MemberData(nameof(EverySpellingOfALiteral))]
    public void A_literal_is_found_with_its_words_however_it_is_spelt(string written, string words) =>
        LiteralsIn(written)
            .ShouldHaveSingleItem("this is one word on a screen, spelt one way: " + written)
            .ShouldContain(
                words,
                Case.Sensitive,
                "the check found this and could not say what it found: " + written);

    /// <summary>
    /// Every property in <see cref="Reads"/> is caught by a row above. Without this the coverage is
    /// a list somebody keeps in step by hand, and it was not in step: a property added to the set is
    /// read by <see cref="LiteralOnScreen"/> and pinned by nothing, so narrowing the set back out
    /// again leaves the suite green.
    /// </summary>
    /// <remarks>
    /// A match and not the row it came from, and where the match <em>begins</em> and not what is
    /// anywhere inside it. Both halves of <see cref="LiteralOnScreen"/> open on the property name
    /// behind a zero-width lookbehind, so a match always starts with the property it is about, and
    /// asking that is asking the real question: <c>Foo.Header = bar.Text == null ? "a" : "b";</c>
    /// spans a whole ternary and has <c>Text</c> inside it while pinning only <c>Header</c>. It also
    /// tells <c>Text</c> from <c>PlaceholderText</c> for free.
    /// <para>
    /// <see cref="LiteralOnScreen"/> and not <see cref="WhatATypeSaysAboutItself"/>, which is built
    /// out of the same set and is unpinned the same way — five of nineteen properties have a row in
    /// <see cref="WhatATypeSaysForItself"/>. That is the same defect over the other pattern and is
    /// this card's <c>left_out</c>; what stops it being hidden is this name saying which pattern it
    /// holds.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_property_a_person_reads_is_caught_by_a_literal_row()
    {
        var caught = WordsOnScreen
            .SelectMany(row => LiteralOnScreen.Matches(row).Select(match => match.Value))
            .ToArray();

        var unpinned = Reads
            .Where(name => !caught.Any(match => match.StartsWith(SpeltInCode(name), StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        unpinned.ShouldBeEmpty(
            "these are properties the check reads and no row would catch, so taking one back out "
            + "of the pattern leaves this suite green: " + string.Join("; ", unpinned));
    }

    /// <summary>
    /// A word a screen never typed and shows anyway: what a type says about itself. The three
    /// checks above are about words a screen wrote down, and a `ToString` is the one shape that
    /// puts words on a screen without any being written there — the words are in another
    /// assembly, in whatever language whoever wrote that type happened to be in.
    /// </summary>
    /// <remarks>
    /// This is what let `(default)` on to the microphone picker: `AudioDevice.ToString` returned
    /// `"{Name} (default)"` and the recorder assigned it to `ItemsSource`, so the screen carried
    /// no literal, bound no catalogue entry, and said an English word to somebody reading in
    /// Spanish. Every check above ran green over it for as long as the picker existed.
    /// <para>
    /// <c>ItemsSource</c> is held here and is deliberately not in <see cref="Reads"/>. It never
    /// holds a string, so the literal check would have nothing to find in it, and holding markup
    /// to binding one would refuse the collections every picker on these screens is filled from
    /// in code. What it is is a list somebody picks from, which is words a person reads by the
    /// dozen — a screen reader says every row of it out loud.
    /// </para>
    /// <para>
    /// Only a <c>ToString()</c> taking nothing. One taking a format or a culture is how a screen
    /// writes a number in the language it is being read in, which is <c>ScreenNumbers</c>' whole
    /// job and the opposite of the mistake — and a type's own no-argument rendering is never data
    /// the way a resource key is, so this crosses brackets freely where
    /// <see cref="WithinOneAssignment"/> may not.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CodeBehind))]
    public void No_screen_shows_a_person_what_a_type_says_about_itself(string path)
    {
        var shown = SaidByATypeIn(File.ReadAllText(path));

        shown.ShouldBeEmpty(
            $"{Path.GetFileName(path)} puts what a type says about itself in front of a person, "
            + "so those words are in whatever language that type was written in: "
            + string.Join("; ", shown));
    }

    /// <summary>
    /// The places <paramref name="source"/> hands a person a type's own rendering, each with the
    /// line it is on — the same shape the sibling above answers in, for the same reason.
    /// </summary>
    private static string[] SaidByATypeIn(string source) =>
    [
        .. WhatATypeSaysAboutItself
            .Matches(source)
            .Where(match => !SourceLines.StandsInACommentedLine(source, match.Index))
            .Select(match => $"line {SourceLines.LineOf(source, match.Index)}: "
                + string.Join(' ', match.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))),
    ];

    /// <summary>
    /// What a screen shows a person out of a type rather than out of the catalogue. Rows and not
    /// only a pattern, for the reason <see cref="WordsAPersonWouldRead"/> gives: this check has
    /// exactly one subject in the application today and it is the one that was just taken out, so
    /// without these it would be a guard with nothing to find, which reads the same as a guard
    /// that works.
    /// </summary>
    public static TheoryData<string> WhatATypeSaysForItself() =>
    [
        @"MicrophonePicker.ItemsSource = _microphones.Select(device => device.ToString()).ToArray();",
        "MicrophonePicker.ItemsSource = _microphones\n            .Select(device => device.ToString())\n            .ToArray();",
        @"StatusText.Text = chosen.ToString();",
        @"Foo.Header = program?.ToString() ?? string.Empty;",
        @"AutomationProperties.SetName(box, device.ToString());",
        @"ToolTipService.SetToolTip(row, meeting.ToString());",
    ];

    /// <summary>
    /// A rendering the reader's own language decides, which is data written the way that language
    /// writes it and is exactly what a screen is supposed to do with a number.
    /// </summary>
    public static TheoryData<string> WhatTheReadersLanguageDecides() =>
    [
        @"Text = things.Count.ToString(UiLanguages.Culture(_language));",
        @"Text = length.ToTimeSpan().ToString(@""h\:mm\:ss"", CultureInfo.InvariantCulture);",
        @"AutomationProperties.SetAutomationId(open, entry.Meeting.Id.ToString());",
        @"Dump(wouldNotSay.ToString());",
        @"var spelt = device.ToString();",
        @"        // MicrophonePicker.ItemsSource = _microphones.Select(device => device.ToString()).ToArray();",
    ];

    /// <summary>
    /// What this is known not to reach, named rather than papered over — and one of these is live
    /// in the application as it stands, which is why it is a row and not a sentence.
    /// </summary>
    /// <remarks>
    /// A <c>ToString</c> behind a method is out of reach for the reason
    /// <see cref="OutOfReachOnPurpose"/> already gives about an expression-bodied member: there is
    /// no assignment to find it by, and following the call is a parse. <c>MainWindow.NameOf</c> is
    /// that shape today — it renders an <c>AudioProcess</c> as <c>"{Name} (pid {Id})"</c> into the
    /// source picker, one row under the microphone one this check was written for. Whether
    /// <c>(pid …)</c> is a word this application chose or a token Windows spells the same in both
    /// languages is a question nobody has answered, and it is issue #84's `left_out` rather than
    /// something to decide inside a guard.
    /// </remarks>
    public static TheoryData<string> SaidThroughAMethodAndOutOfReach() =>
    [
        @"SourcePicker.ItemsSource = _sources.Select(NameOf).ToArray();",
        @"private string NameOf(RecorderSource source) => source.Follow!.ToString();",
    ];

    [Theory]
    [MemberData(nameof(WhatATypeSaysForItself))]
    public void What_a_type_says_about_itself_is_found_however_it_reaches_a_person(string written) =>
        SaidByATypeIn(written).ShouldNotBeEmpty(
            "this puts a type's own words on a screen and nothing found it: " + written);

    [Theory]
    [MemberData(nameof(WhatTheReadersLanguageDecides))]
    public void A_rendering_the_reader_chose_is_left_alone(string written) =>
        SaidByATypeIn(written).ShouldBeEmpty(
            "the language being read in decides this one and it was reported anyway: " + written);

    [Theory]
    [MemberData(nameof(SaidThroughAMethodAndOutOfReach))]
    public void What_is_said_through_a_method_is_still_out_of_reach(string written) =>
        SaidByATypeIn(written).ShouldBeEmpty(
            "this row says the check does not reach this, and it did, so the row and the check "
            + "no longer agree: " + written);

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
    /// A <see cref="Literal"/> reaching one of <see cref="Reads"/> from code-behind, however the
    /// assignment is written.
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
    /// A <c>ToString()</c> taking nothing, reaching one of <see cref="Reads"/> or a list somebody
    /// picks from — built out of the same one list, for the same reason
    /// <see cref="LiteralOnScreen"/> is.
    /// </summary>
    /// <remarks>
    /// <c>[^;]*?</c> and not <see cref="WithinOneAssignment"/>, which is the one place these two
    /// patterns part. That bound exists to keep the literal check out of argument lists, where a
    /// resource key would be reported as a word wanting a translation; there is no such thing
    /// here, because a no-argument <c>ToString()</c> is never a key, a format or a path. So this
    /// one goes wherever the statement goes — through a <c>Select</c>, across a lambda's
    /// <c>=&gt;</c>, over as many lines as the statement takes — and stops at the <c>;</c> that
    /// ends it, which is what keeps one statement's words off the next one's name.
    /// </remarks>
    private static readonly Regex WhatATypeSaysAboutItself = new(
        SaysItselfInto(Named(Plain) + "|" + PickedFrom)
        + "|" + @"(?<!\w)(?:" + Named(Attached)
        + @")\([^;]*?\.ToString\(\)",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// The property a list somebody picks from is set through. Not an entry of
    /// <see cref="Reads"/>, for the reason
    /// <see cref="No_screen_shows_a_person_what_a_type_says_about_itself"/> gives.
    /// </summary>
    private const string PickedFrom = "ItemsSource";

    /// <summary>One of <paramref name="properties"/> taking a type's own rendering.</summary>
    private static string SaysItselfInto(string properties) =>
        @"(?<!\w)(?:" + properties + @")\s*(?:\+|\?\?)?=(?![=>])[^;]*?\.ToString\(\)";

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
        @"(?<!\w)(?:" + Named(Plain) + @")"
        + @"\s*(?:\+|\?\?)?=(?![=>])\s*(?:" + WithinOneAssignment + @"[?:+]\s*)?" + Literal;

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
        @"(?<!\w)(?:" + Named(Attached) + @")\(" + InsideOneCall + Literal;

    /// <summary>
    /// A literal and the words in it — one alternative per spelling C# has for one, in the order
    /// C# tells them apart by: raw, then verbatim, then plain. Interpolation is a <c>$</c> on the
    /// front of any of the three.
    /// </summary>
    /// <remarks>
    /// One alternative each and not one pattern loose enough for all three, because the spellings
    /// disagree about what a run of quotes means and the loose reading of one is a defect in
    /// another. A raw string opens on three quotes or more, of which the first two are a complete
    /// empty pair, so without the raw alternative first the plain one matches its head and reports a
    /// word a person reads with an empty value — the check seeing something and unable to say what.
    /// The other way round, <c>@""""</c> is a verbatim string holding one escaped quote and not a
    /// raw string at all, so the raw alternative takes no <c>@</c>: reading it as a three-quote
    /// opener sends it hunting for a closing run and one finding swallows three statements, which is
    /// the failure <see cref="InsideOneCall"/> already says was paid for once.
    /// <para>
    /// The verbatim alternative is the one that pays for itself twice. It reports
    /// <c>@"dijo ""hola"""</c> whole, where the plain alternative stopped at the first escaped quote
    /// and quoted back <c>@"dijo "</c>.
    /// </para>
    /// <para>
    /// The two uses of this in one pattern share the group name <c>opened</c>. Two groups of one
    /// name in .NET are one group, holding whatever last captured, and a capture is undone when the
    /// alternative that made it fails — so inside an alternation the backreference is the one the
    /// alternative being tried just made.
    /// </para>
    /// </remarks>
    private const string Literal =
        @"(?:\$*(?<opened>""{3,})(?:(?!\k<opened>)[\s\S])*?\k<opened>"
        + @"|[$@]*@[$@]*""(?:[^""]|"""")*"""
        + @"|\$*""[^""]*"")";

    /// <summary>An entry of <see cref="Reads"/> that is a property of the element itself.</summary>
    private static bool Plain(string name) => !name.Contains('.');

    /// <summary>An entry of <see cref="Reads"/> that is attached rather than the element's own.</summary>
    private static bool Attached(string name) => name.Contains('.');

    /// <summary>
    /// How an entry of <see cref="Reads"/> is spelt where a screen assigns it. A plain property is
    /// its own name; an attached one has no assignment to be found by, so markup's <c>Owner.Prop</c>
    /// is <c>Owner.SetProp(element, value)</c> in code.
    /// </summary>
    /// <remarks>
    /// One function and not a lambda at each of the four call sites, because
    /// <see cref="Every_property_a_person_reads_is_caught_by_a_literal_row"/> asks the same question
    /// of a match: a second spelling of this would be a check that agreed with itself about a name
    /// the pattern spells some other way.
    /// </remarks>
    private static string SpeltInCode(string name) =>
        Plain(name) ? name : name.Replace(".", ".Set", StringComparison.Ordinal);

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
    private static string Named(Func<string, bool> of)
    {
        var names = Reads.Where(of).Select(SpeltInCode).Select(Regex.Escape).Order(StringComparer.Ordinal).ToArray();

        return names.Length == 0
            ? throw new InvalidOperationException(
                "Reads has no property of this kind left, and half of the pattern would match "
                + "the empty string instead of saying so.")
            : string.Join('|', names);
    }
}
