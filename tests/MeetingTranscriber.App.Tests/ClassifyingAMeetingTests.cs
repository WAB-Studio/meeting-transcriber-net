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
    /// The three tables above are over three differently-named expressions, and that is load
    /// bearing rather than a matter of taste.
    /// </summary>
    /// <remarks>
    /// <see cref="EnumTable.Read"/> matches one switch per file per expression, so two tables over
    /// one name would leave the second read finding the first table, no arms for the enum it asked
    /// about, and a failure that says the screen holds no table at all. Held here rather than left
    /// to whoever reads the three tests above and wonders why the variables differ.
    /// </remarks>
    [Fact]
    public void No_two_tables_on_this_screen_switch_on_the_same_word()
    {
        var switching = Regex
            .Matches(File.ReadAllText(AppSources.At(Screen).FullName), @"(?<over>\w+) switch\r?\n")
            .Select(match => match.Groups["over"].Value)
            .ToArray();

        switching.ShouldNotBeEmpty("ClassifyingAMeeting.xaml.cs holds no table, so this reads nothing.");
        switching.Distinct(StringComparer.Ordinal).ShouldBe(switching);
    }

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
        var named = Regex.Matches(
                File.ReadAllText(AppSources.At(Screen).FullName),
                @"Chrome\(""(?<key>\w+)""\)")
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

        raising.Value.ShouldContain("Pause()");
        raising.Value.ShouldContain("Classify?.Invoke");
    }
}
