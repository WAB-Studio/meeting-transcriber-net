using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The screen one meeting is filed from, held to what no rule of its own can reach: that it has a
/// word for every member of the three closed vocabularies it draws, that every style it names at
/// the moment it draws is one its own markup declares, and that it can be opened and left at all.
/// </summary>
/// <remarks>
/// What the screen decides is <c>MeetingShapesTests</c>, <c>MeetingFilingTests</c> and
/// <c>MeetingClassifyingTests</c>, which run. What none of them can see is whether the window has a
/// word for what they answer — and a shape with no name is a chip drawn blank, while a role with no
/// column is a link the corpus can hold and the screen cannot say.
/// <para>
/// How the tables are read out of source, and why they have to be, is <see cref="EnumTable"/>'s.
/// </para>
/// </remarks>
public class ClassifyingAMeetingTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "ClassifyingAMeeting.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "ClassifyingAMeeting.xaml");

    private static readonly string Classification =
        Path.Combine("MeetingTranscriber.Domain", "Meetings", "Classification.cs");

    [Fact]
    public void The_screen_names_every_shape_a_meeting_can_be_filed_under() =>
        EnumTable.Read(
                Screen,
                "shape",
                "MeetingShape",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "MeetingShapes.cs"))
            .ShouldNameItsWholeEnum("MeetingShape");

    /// <summary>
    /// The screen has a word for every name a place can be called and a line for every chip.
    /// </summary>
    /// <remarks>
    /// <c>PlaceName</c> is what a kind of meeting calls a level or a person, and a member this screen
    /// cannot say is a pill drawn with no word over it. The chip's line is the other table: it is
    /// read as its own because a chip with no description is a tooltip that shows nothing.
    /// </remarks>
    [Fact]
    public void Every_shape_and_every_place_name_has_words_on_this_screen()
    {
        EnumTable.Read(
                Screen,
                "place",
                "PlaceName",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "MeetingShapes.cs"))
            .ShouldNameItsWholeEnum("PlaceName");

        EnumTable.Read(
                Screen,
                "meeting",
                "MeetingShape",
                Path.Combine("MeetingTranscriber.Domain", "Meetings", "MeetingShapes.cs"))
            .ShouldNameItsWholeEnum("MeetingShape (as described)");
    }

    /// <summary>
    /// What a pill offers is what its level's name holds, so nothing offers an organization where a
    /// project is asked.
    /// </summary>
    /// <remarks>
    /// Read inside the one place that lists a level's nodes, because the corpus holds all three
    /// classes and the pill under <em>Materia</em> showing the organizations beside the projects was
    /// the confusion this screen was changed for.
    /// </remarks>
    [Fact]
    public void No_pill_offers_an_organization_where_a_project_is_asked()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);
        var offering = Body(source, "private static IReadOnlyList<Node> WhatMayStandAt(");

        offering.ShouldContain("MeetingShapes.Holds(name)");
        offering.ShouldContain("node.Kind == holds");
        offering.ShouldContain("BelongsToNobody(name)");

        var top = Body(source, "private UIElement APill(");

        top.ShouldContain("UiTexts.ANewOrganization");

        // Work of nobody in particular is offered only under the two generic names.
        top.ShouldContain("if (BelongsToNobody(name))");
        top.ShouldContain("UiTexts.WorkThatIsNobodysInParticular");
    }

    /// <summary>
    /// With no chip lit, each of the two columns the screen does not open is one press away, so
    /// the crossings §5.3 stores are still fileable by hand.
    /// </summary>
    [Fact]
    public void With_no_chip_lit_a_path_can_be_added_under_each_of_the_three_roles()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);
        var columns = Body(source, "private void TheColumns(");

        columns.ShouldContain("MeetingShape.FilledByHand");
        columns.ShouldContain("$\"{role}-open\"");
        columns.ShouldContain("AddAPath(role)");

        var presses = Body(source, "private static UiText OpenColumn(");

        presses.ShouldContain("MeetingNodeRole.Counterpart => UiTexts.OpenOtherOrganization");
        presses.ShouldContain("MeetingNodeRole.About => UiTexts.OpenItIsAbout");

        UiTexts.OpenOtherOrganization.Spanish.ShouldBe("Otra organización…");
        UiTexts.OpenItIsAbout.Spanish.ShouldBe("Trata sobre…");
    }

    /// <summary>
    /// Every way a meeting relates to what it was about has a column on this screen.
    /// </summary>
    /// <remarks>
    /// What fires the day a fourth one joins the closed vocabulary and this screen is not told: the
    /// corpus would hold links nothing on any screen could show or take off.
    /// </remarks>
    [Fact]
    public void The_screen_names_every_way_a_meeting_relates_to_a_node() =>
        EnumTable.Read(Screen, "role", "MeetingNodeRole", Classification)
            .ShouldNameItsWholeEnum("MeetingNodeRole");

    /// <summary>
    /// Every way somebody can be named on a meeting has a toggle on this screen.
    /// </summary>
    /// <remarks>
    /// Both of them, and both pressable: §5.3 row 10 is a person a meeting is about who was never
    /// in the room, and with one badge somebody filing that meeting by hand cannot say so.
    /// </remarks>
    [Fact]
    public void The_screen_names_every_way_somebody_is_named_on_a_meeting() =>
        EnumTable.Read(Screen, "named", "MeetingPersonRole", Classification)
            .ShouldNameItsWholeEnum("MeetingPersonRole");

    /// <summary>
    /// Every style this screen looks up by name is one it declares.
    /// </summary>
    /// <remarks>
    /// The half no compiler reaches, and on this screen it is the half that bites: <c>Chrome</c> is
    /// a <c>ResourceDictionary</c> indexer over the screen's own resources and does not walk up to
    /// the application's, so naming an Olivo key straight from the code-behind is green in CI and
    /// throws on the UI thread the moment a picker is drawn. Every Olivo style this screen builds
    /// in code is aliased in its own markup for that reason, and this is what catches the omission.
    /// </remarks>
    [Fact]
    public void Every_style_this_screen_names_is_one_it_declares()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        // Every lookup takes its key as a literal, which is what makes the check below able to see
        // them all. A key worked out on the way in — a ternary in the call, a method handing one
        // back by name — is a key this reads past, and a key that names nothing is an exception on
        // the UI thread off a build with nothing wrong in it. Two of these had that shape and were
        // caught by nothing; the answer was to hand back the style instead of the name.
        Regex.Matches(source, @"Chrome\((?<taking>[^)]*)\)")
            .Select(match => match.Groups["taking"].Value.Trim())
            .Where(taking => !Regex.IsMatch(taking, @"^""\w+""$") && taking != "string named")
            .ToArray()
            .ShouldBeEmpty("these lookups do not name their style as a literal, so nothing checks it.");

        var named = Regex.Matches(source, @"Chrome\(""(?<key>\w+)""\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = Regex.Matches(
                File.ReadAllText(AppSources.At(Markup).FullName),
                @"x:Key=""(?<key>\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        named.ShouldNotBeEmpty("ClassifyingAMeeting.xaml.cs names no style, so this check reads nothing.");
        declared.ShouldNotBeEmpty("ClassifyingAMeeting.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "ClassifyingAMeeting.xaml declares no style by these names, so the screen throws where "
            + "it is drawn.");
    }

    /// <summary>
    /// The meeting screen says it should be filed, and the window opens the screen that files it.
    /// </summary>
    /// <remarks>
    /// The one thing that makes this screen reachable at all, and the one thing no other check here
    /// covers: every table above would pass over a screen nothing can open. Read out of source
    /// because opening a window is what it would otherwise take, and no build agent has one.
    /// </remarks>
    [Fact]
    public void The_meeting_screen_says_it_should_be_filed_and_the_window_opens_the_screen_that_files_it()
    {
        var meeting = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs")).FullName);

        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        meeting.ShouldContain("Classify?.Invoke");
        window.ShouldContain("Reading.Classify += OnClassifyTheMeeting");
        window.ShouldContain("Classifying.Show(meeting)");

        // Both ways back, and they are not the same answer: one meeting was filed and the other was
        // not, so only the first has anything for the screen underneath to read again.
        window.ShouldContain("Classifying.Filed += OnFiled");
        window.ShouldContain("Classifying.Left += OnLeftTheClassification");
        window.ShouldContain("Reading.ReadAgain()");

        // And the window lets go of it. A window that shut over a screen still holding a meeting
        // leaves the meeting screen holding a recording with it.
        window.ShouldContain("Classifying.Close()");
    }

    /// <summary>
    /// The recording is stopped before the screen that files the meeting takes the window.
    /// </summary>
    /// <remarks>
    /// A player left running behind a collapsed screen is sound coming out of an application that
    /// appears to be doing nothing else — which is the thing <c>ReadingAMeeting.Close</c>'s own
    /// remark says must not happen, and which nothing else here would notice. Source-read because
    /// no build agent has a window to hear it in.
    /// </remarks>
    [Fact]
    public void The_recording_is_stopped_before_the_screen_that_files_it_takes_the_window()
    {
        var meeting = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs")).FullName);

        // The closing brace is anchored to the method's own indentation and not to any brace at
        // all: lazy to `[ ]*\}` stops at the first `}` inside the body, which is a check that reads
        // the guard at the top and none of what follows it.
        var raising = Regex.Match(
            meeting,
            @"private void OnClassify\(object sender, RoutedEventArgs e\)\r?\n[ ]{4}\{.*?\r?\n[ ]{4}\}",
            RegexOptions.Singleline);

        raising.Success.ShouldBeTrue(
            "ReadingAMeeting.xaml.cs no longer has an OnClassify, so this check reads nothing.");

        var stopped = raising.Value.IndexOf("Pause()", StringComparison.Ordinal);
        var raised = raising.Value.IndexOf("Classify?.Invoke", StringComparison.Ordinal);

        stopped.ShouldBeGreaterThan(-1, "OnClassify does not stop the recording at all.");
        raised.ShouldBeGreaterThan(-1, "OnClassify raises nothing, so no screen is opened.");

        // The order and not only that both are there, which is the whole of what this is named
        // after: raised first, the recording plays on behind a screen that has already been
        // collapsed out of the automation tree, for as long as the handler and the redraw under it
        // take.
        stopped.ShouldBeLessThan(
            raised,
            "OnClassify raises Classify before it pauses, so the recording keeps playing behind a "
            + "screen nobody can see.");
    }

    /// <summary>
    /// Every pill and place that holds something has a press beside it that corrects its name,
    /// and no list offers one — the pills over the tree and the rows of people alike, because a name
    /// typed wrong is typed wrong in both, and in the same words for the reason
    /// <c>docs/design.md</c> gives.
    /// </summary>
    /// <remarks>
    /// Read inside each builder rather than anywhere in the file, so a press deleted from one of
    /// the two is what fails rather than the member surviving in a comment somewhere else.
    /// </remarks>
    [Fact]
    public void Every_filled_pill_and_place_has_a_correction_press_beside_it_and_none_inside_its_list()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        foreach (var builder in new[] { "private UIElement APill(", "private UIElement APlaceForSomebody(" })
        {
            Regex.IsMatch(Body(source, builder), @"extras\.Add\(\(\s*UiTexts\.CorrectThisName")
                .ShouldBeFalse(
                    $"`{builder}` offers correcting a name as an entry in the list of what the pill "
                    + "may become, where it is read past and cannot empty what stands there.");
        }

        Body(source, "private UIElement APill(").ShouldContain(
            "BesideIt(",
            customMessage: "no pill over the tree has a correction press beside it.");

        Body(source, "private UIElement APlaceForSomebody(").ShouldContain(
            "BesideIt(",
            customMessage: "no row of people has a correction press beside the name standing in it.");

        Body(source, "private StackPanel BesideIt(").ShouldContain("UiTexts.CorrectThisName");
    }

    /// <summary>
    /// Leaving the naming field writes nothing and erases nothing: a press elsewhere, <em>Agregar</em>
    /// included, is not an answer to the field.
    /// </summary>
    /// <remarks>
    /// The field was put away by its own <c>LostFocus</c>, so the redraw a press causes took what had
    /// been typed with it and the press looked as though it did nothing. The way out is now
    /// <em>Cancelar</em> and Escape, and what is typed rides on the field's state across a redraw.
    /// </remarks>
    [Fact]
    public void Losing_the_naming_field_s_focus_writes_and_erases_nothing()
    {
        var body = Body(File.ReadAllText(AppSources.At(Screen).FullName), "private UIElement AName(");

        body.ShouldNotContain(
            "LostFocus",
            customMessage: "the naming field answers a lost focus, so a press anywhere else puts the "
            + "pill back and erases what was typed.");

        body.ShouldContain("TextChanged", customMessage: "what is typed is not kept across a redraw.");
        body.ShouldContain("UiTexts.Save", customMessage: "the naming field has no press that commits it.");
        body.ShouldContain("UiTexts.Cancel", customMessage: "the naming field has no press that puts it away.");

        body.ShouldContain(
            "{ typing, cancel, save }",
            customMessage: "the act is not on the right: `docs/design.md` §Two places puts the way out on its left.");

        body.ShouldContain(
            "Opened",
            customMessage: "the field takes focus on every redraw, from whatever was pressed to cause it.");
    }

    /// <summary>
    /// The line that puts a classification by is there for one filled by hand, and for one already
    /// put by and lit — not beside a chip somebody chose from the fourteen.
    /// </summary>
    [Fact]
    public void The_keep_line_is_offered_only_for_a_classification_filled_by_hand()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        var line = Body(source, "private void TheLine(");

        line.ShouldContain("KeepingIt.Visibility");
        line.ShouldContain("MeetingShape.FilledByHand");
        line.ShouldContain("lit is not null");

        Body(source, "private void Render(").ShouldContain(
            "KeepingIt.Visibility = Visibility.Collapsed",
            customMessage: "the line is left standing over a screen with no meeting on it.");
    }

    /// <summary>
    /// Both things that can stand at the top of a tree are on offer there, and the class is no
    /// longer worked out from the level alone.
    /// </summary>
    /// <remarks>
    /// A body of work belonging to nobody in particular had no way into the tree from this screen,
    /// because the top level was hardcoded to an organization. The two entries are what replaced
    /// that without ever asking somebody a technical name.
    /// </remarks>
    [Fact]
    public void The_top_of_the_tree_offers_both_things_that_can_stand_there()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldContain("UiTexts.ANewOrganization");
        source.ShouldContain("UiTexts.WorkThatIsNobodysInParticular");

        Body(source, "private void NameANode(").ShouldNotContain(
            "NodeKind.Organization",
            customMessage: "NameANode still decides the class from the level, so the second entry "
            + "at the top of the tree writes an organization whatever it says.");
    }

    /// <summary>
    /// Correcting a name leaves the pills to the right of it alone.
    /// </summary>
    /// <remarks>
    /// The node standing in the pill is the same node it was, so the pills to its right are still
    /// its children. Putting it into the path again is what naming a <em>new</em> one has to do and
    /// what this must not: it would empty every choice somebody had already made below it.
    /// </remarks>
    [Fact]
    public void Correcting_a_name_leaves_the_pills_to_the_right_of_it_alone() =>
        Body(File.ReadAllText(AppSources.At(Screen).FullName), "private void CorrectTheName(")
            .ShouldNotContain(
                "PutAt(",
                customMessage: "correcting a node's name puts it into the path again, which empties "
                + "every pill to the right of it — the children somebody had already chosen.");

    /// <summary>
    /// Nothing this screen offers says <em>initiative</em> or <em>node</em> to anybody, in either
    /// language.
    /// </summary>
    /// <remarks>
    /// #105's rule, and it is what the two-entry answer at the top of the tree exists to keep.
    /// <em>Organization</em> is not on the list and the name says so: it is a plain word somebody
    /// uses out loud, and one of the entries below is <em>Una organización nueva…</em>.
    /// </remarks>
    [Fact]
    public void Nothing_on_this_screen_says_initiative_or_node_to_a_person()
    {
        string[] technical = ["initiative", "iniciativa", "node", "nodo"];

        UiText[] offered =
        [
            UiTexts.CorrectThisName,
            UiTexts.ANewOrganization,
            UiTexts.WorkThatIsNobodysInParticular,
            UiTexts.AboutThisPerson,
        ];

        foreach (var words in offered)
        {
            foreach (var name in technical)
            {
                words.Spanish.ShouldNotContain(name, Case.Insensitive, customMessage: name);
                words.English.ShouldNotContain(name, Case.Insensitive, customMessage: name);
            }
        }
    }

    /// <summary>
    /// Every pill on this screen is a control a screen reader and the probe can address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pills are built in code and have no <c>x:Name</c>, and <c>UiProbe.ElementWords</c>
    /// matches an element only by <c>AutomationId</c> or <c>Name</c> — so an unnamed one is a bare
    /// <c>ComboBox</c> in the tree that no <c>choose</c> can reach. That is why #296's own Proof
    /// could not be driven and had to be reasoned about instead, and a screen reader is in exactly
    /// the same position.
    /// </para>
    /// <para>
    /// Both properties, because they answer two different questions and only one of them is a key.
    /// The name is what somebody hears and is deliberately what stands in the pill — so it is not
    /// unique, it is Spanish, and it changes the moment anybody chooses anything. The id is where
    /// the pill stands, which is unique, the same in both languages, and still the same word after
    /// a <c>choose</c>: it is what <c>UiProbe.Search</c> matches first, and without it a walk over
    /// this screen cannot be written at all, since the column's own heading is drawn beside the
    /// pills carrying the identical string.
    /// </para>
    /// <para>
    /// That <c>OneOfThese.Build</c> takes a name and an id is the compiler's to enforce and not a
    /// test's; what no compiler can say is that the parameters are used on the control, and that no
    /// second picker is built somewhere else without them. <c>TheirOrganization</c> in the markup is
    /// not a second one — XAML gives it an <c>x:Name</c>, which is where an id comes from there.
    /// </para>
    /// <para>
    /// Read as text, so it is a cheap guard and not a structural impossibility: <c>ComboBox picker
    /// = new();</c> is the same construction and matches nothing here. What it stops is the
    /// ordinary way this would come back — a second builder written the way the first one is.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_pill_this_screen_builds_can_be_addressed_by_name()
    {
        var oneOfThese = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "OneOfThese.cs")).FullName);

        oneOfThese.ShouldContain(
            "AutomationProperties.SetName(picker",
            customMessage: "OneOfThese.Build returns a control with no name on it, so every pill "
            + "built through it is a bare ComboBox to a screen reader.");

        oneOfThese.ShouldContain(
            "AutomationProperties.SetAutomationId(picker",
            customMessage: "OneOfThese.Build returns a control with no id on it, so no `choose` "
            + "can reach a pill: the words are not unique on a screen and they move when "
            + "somebody answers.");

        // The field that stands where a pill was is the only thing on this screen that commits on
        // Enter, so a walk that cannot address it cannot finish an act.
        var source = File.ReadAllText(AppSources.At(Screen).FullName);
        Body(source, "private UIElement AName(").ShouldContain(
            "AutomationProperties.SetAutomationId(typing",
            customMessage: "the field a new name is typed into has no id, so nothing can send it "
            + "the Enter that writes the name.");

        oneOfThese.ShouldContain(
            "new ComboBox",
            customMessage: "the application's one ComboBox construction has moved out of "
            + "OneOfThese.cs, which is where this check expects to find it.");

        var occurrences = AppSources.With(".cs")
            .Sum(file => SourceLines.Occurrences(File.ReadAllText(file.FullName), "new ComboBox").Count());

        occurrences.ShouldBe(
            1,
            "the application builds a ComboBox somewhere other than OneOfThese.Build, and that "
            + "one is addressed by nothing. Build it through OneOfThese, which is also where the "
            + "index arithmetic lives.");
    }

    /// <summary>
    /// The corpus is opened once for each thing this screen does to it, and naming or correcting a
    /// node is one of them.
    /// </summary>
    /// <remarks>
    /// Five presses write through it — naming a node, correcting one, putting a classification by,
    /// correcting its name and discarding it — each spelling out the same ladder
    /// before <c>InTheCorpus</c> existed: the folder, the context, the layer, the corpus saying no,
    /// the corpus failing. That is what made the <c>First</c>-versus-<c>FirstOrDefault</c>
    /// divergence #296 fixed possible — the same lookup written more than once, only one of which
    /// was right. Adding a person moved to <c>AddingSomebody</c>, pinned in
    /// <c>SayingWhoIsWhoTests</c>, and a fourth caller opening the corpus for a node is what this
    /// still goes red on.
    /// </remarks>
    [Fact]
    public void This_screen_opens_the_corpus_once_for_each_thing_it_does_to_it()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        SourceLines.Occurrences(source, "new HumanLayer(").Count().ShouldBe(
            1,
            "naming a node or correcting one is written more than once, which is the shape the "
            + "divergence #296 fixed grew in. InTheCorpus is the one place on this screen that "
            + "opens the corpus to write the tree's vocabulary; adding or correcting a person is "
            + "AddingSomebody's.");

        SourceLines.Occurrences(source, "CorpusDatabase.Open(").Count().ShouldBe(
            3,
            "this screen opens the corpus three times and each is a different thing it does: Draw "
            + "reads the meeting, InTheCorpus writes the tree's vocabulary and the classifications put by under a name, and OnSave files it. "
            + "Adding or correcting a person opens its own, in AddingSomebody, and is not one of "
            + "these three. A fourth opening here is a fourth thing this screen does to the "
            + "corpus, and the answer is to say here what it is — not to raise the number. Read as "
            + "text, so a construction spelled another way walks past it.");
    }

    /// <summary>
    /// The classifications put by are drawn after the fourteen, so they come back on every meeting.
    /// </summary>
    [Fact]
    public void The_classifications_put_by_are_drawn_after_the_fourteen()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);
        var render = Body(source, "private void Render(");

        render.ShouldContain("TheShapesOnOffer(");
        render.ShouldContain("TheKeptOnOffer(");
        render.IndexOf("TheKeptOnOffer(", StringComparison.Ordinal).ShouldBeGreaterThan(
            render.IndexOf("TheShapesOnOffer(", StringComparison.Ordinal),
            "the classifications put by are drawn before the fourteen, or not drawn at all.");
    }

    /// <summary>
    /// Throwing a classification away loses something, so it sits past the gap at the margin.
    /// </summary>
    [Fact]
    public void Discarding_a_classification_put_by_is_the_press_that_loses_something()
    {
        var markup = File.ReadAllText(AppSources.At(Markup).FullName);

        Regex.Match(markup, @"<Button\s+x:Name=""DiscardKeptButton""[^>]*>", RegexOptions.Singleline)
            .Value.ShouldContain(
                "Style=\"{StaticResource ItLosesSomething}\"",
                customMessage: "the press that throws a classification away is not drawn as one that loses something.");
    }

    /// <summary>
    /// Correcting a name asks and writes nothing: the press opens the field, and only Enter in it
    /// commits, as it does on a pill.
    /// </summary>
    [Fact]
    public void Correcting_the_name_of_a_classification_put_by_commits_only_on_Enter()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        Body(source, "private void OnCorrectKeptName(").ShouldNotContain(
            "InTheCorpus(",
            customMessage: "the press renames straight away, so a mistaken press changes a name for good.");

        Body(source, "private void OnKeptNameKey(").ShouldContain("CorrectTheKeptName(");

        Body(source, "private void CorrectTheKeptName(").ShouldContain("InTheCorpus(");
    }

    /// <summary>
    /// One method's body, anchored on the closing brace at its own indentation. Lazy to
    /// <c>[ ]*\}</c> would stop at the first brace inside it, which is a check that reads the guard
    /// at the top and none of what follows.
    /// </summary>
    private static string Body(string source, string signature)
    {
        var found = Regex.Match(
            source,
            Regex.Escape(signature) + @".*?\r?\n[ ]{4}\}",
            RegexOptions.Singleline);

        found.Success.ShouldBeTrue($"ClassifyingAMeeting.xaml.cs no longer has a `{signature}`.");
        return found.Value;
    }
}
