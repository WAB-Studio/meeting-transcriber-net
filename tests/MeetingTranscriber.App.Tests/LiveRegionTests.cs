using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// A line marked as a live region has to be one a narrator is really told about, and what decides
/// that is where its words come from: the code that shows it, never the XAML.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists against already happened once and read as correct in review. Lines on
/// the recording window were given <c>AutomationProperties.LiveSetting</c> and their text bound in
/// the XAML, and nothing was ever announced: a live region is announced on its text changing, not
/// on its visibility, and a <c>Collapsed</c> element is not in the automation tree at all — so a
/// sentence bound once while the panel was hidden never changes again. The device dies, the line
/// appears, and somebody recording through a screen reader is told nothing until they go walking
/// the window for it.
/// </para>
/// <para>
/// Two halves, because there are two ways to have a live region that says nothing: words bound
/// where nothing will change them, and words never set at all. Both are structural, which is what
/// makes them checks a build agent can run — a WinUI tree needs a UI thread and a packaged host.
/// Which properties count as carrying words, and which ways of carrying them this reads past, are
/// named on <see cref="Carries"/>.
/// </para>
/// <para>
/// What no probe here reaches is a narrator really reading it out; that is run by hand and written
/// down, like every other check that needs a packaged host.
/// </para>
/// </remarks>
public partial class LiveRegionTests
{
    private const string LiveSetting = "AutomationProperties.LiveSetting";

    private static readonly XName Named = XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");

    public static TheoryData<string> Screens() =>
        [.. AppSources.With(".xaml").Select(file => file.FullName)];

    [Fact]
    public void There_are_live_regions_to_check()
    {
        // Without this the checks below pass by finding nothing, which reads exactly like a
        // codebase with nothing wrong in it.
        var regions = AppSources.With(".xaml").SelectMany(file => LiveRegions(file.FullName));

        regions.ShouldNotBeEmpty();
    }

    /// <summary>
    /// The first half: no live region carries its words in the XAML, on itself or anywhere under
    /// it.
    /// </summary>
    /// <remarks>
    /// Under it as well as on it, because a live region is as often a panel as a line — what is
    /// announced is the region, and the words sit on whatever is inside. And an attached property
    /// has two spellings, the attribute and the child element that sets the same thing: reading
    /// only the first is how one lesson gets learned twice, which
    /// <c>ScreenTextsTests.No_screen_writes_words_between_its_tags</c> is already this repo's
    /// worked example of.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Screens))]
    public void A_live_region_does_not_carry_its_words_where_nothing_will_change_them(string path)
    {
        var bound = LiveRegions(path)
            .SelectMany(region => region.DescendantsAndSelf())
            .Where(Carries)
            .Select(NameOf)
            .ToArray();

        bound.ShouldBeEmpty(
            $"{Path.GetFileName(path)} puts the words of a live region in the XAML, where they are "
            + "set once while it is collapsed and never change again, so nothing is announced when "
            + "it appears. They belong in the code that shows it: " + string.Join("; ", bound));
    }

    /// <summary>
    /// The second half: every live region is really told something by the code behind its screen.
    /// </summary>
    /// <remarks>
    /// The other way to announce nothing, and the one the first half would create on its own —
    /// delete the binding, forget the call, and a blank line renders, says nothing and passes every
    /// check. Held by name and not by reading what the call hands over, for the reason this whole
    /// project reads source: there is no <c>ProjectReference</c> to the application. What a name
    /// reaching the words is worth is that a region nobody wired at all cannot be one of them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_live_region_is_told_its_words_by_the_code_that_shows_it(string path)
    {
        var regions = LiveRegions(path);
        if (regions.Count == 0)
        {
            return;
        }

        var unnamed = regions.Where(region => region.Attribute(Named) is null).Select(NameOf).ToArray();

        unnamed.ShouldBeEmpty(
            $"{Path.GetFileName(path)} has live regions with no x:Name, so nothing can be shown to "
            + "set their words: " + string.Join("; ", unnamed));

        var behind = new FileInfo(path + ".cs");
        behind.Exists.ShouldBeTrue(
            $"{behind.Name} is where those words would be set, and there is no such file.");

        var code = File.ReadAllText(behind.FullName);
        var silent = regions
            .Select(region => region.Attribute(Named)!.Value)
            .Where(name => !Regex.IsMatch(code, $@"\bTell\(\s*{Regex.Escape(name)}\s*,"))
            .ToArray();

        silent.ShouldBeEmpty(
            $"{behind.Name} never tells these live regions anything, so they render blank and "
            + "announce nothing: " + string.Join("; ", silent));
    }

    /// <summary>
    /// The third way to have a live region that says nothing, and the one #204 was written about:
    /// it is inside something that travels off the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves above hold a line that is on screen and never announced. This holds the
    /// other shape — a line that is announced correctly and is not on screen. A screen that
    /// rearranges itself takes a whole subtree away with <c>ScreenMotion.ArriveOrLeave</c>, and
    /// what that leaves behind is <c>Visibility.Collapsed</c>: not in the automation tree, so
    /// every live region under it is told its words into nothing. That is what the recording
    /// card's five fault lines were doing for the whole of every raised list, and moving them out
    /// is the fix — this is what stops the next one going back in.
    /// </para>
    /// <para>
    /// Read off the source like the rest of this class, and by name: what travels is whatever the
    /// code-behind hands to <c>ArriveOrLeave</c>, so the element has to be named at that call and
    /// not inside a local function that takes it — a helper leaves the source carrying a parameter
    /// name, which is what this check would then look for in the markup and never find. The first
    /// version of this check did exactly that and passed over a live region put back inside the
    /// travelling half, which is why <see cref="Something_on_these_screens_travels"/> stands beside
    /// it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Screens))]
    public void No_live_region_lives_inside_something_the_screen_travels_off_it(string path)
    {
        var behind = new FileInfo(path + ".cs");
        if (!behind.Exists)
        {
            return;
        }

        var travelling = Travels()
            .Matches(File.ReadAllText(behind.FullName))
            .Select(match => match.Groups["element"].Value)
            .ToHashSet(StringComparer.Ordinal);

        if (travelling.Count == 0)
        {
            return;
        }

        var hidden = XDocument
            .Load(path)
            .Descendants()
            .Where(element => travelling.Contains(element.Attribute(Named)?.Value ?? string.Empty))
            .SelectMany(element => element.DescendantsAndSelf())
            .Where(IsALiveRegion)
            .Select(NameOf)
            .ToArray();

        hidden.ShouldBeEmpty(
            $"{Path.GetFileName(path)} puts live regions inside something {behind.Name} travels "
            + "off the screen, so they announce nothing for as long as it is away — which is the "
            + "whole of what a collapsed element costs a narrator: "
            + string.Join("; ", hidden));
    }

    /// <summary>
    /// Something on these screens really does travel, so the theory above cannot pass by finding
    /// nothing — which is how it passed the first time it was written, over a defect that was
    /// there.
    /// </summary>
    [Fact]
    public void Something_on_these_screens_travels()
    {
        var named = AppSources
            .With(".xaml")
            .Select(file => new FileInfo(file.FullName + ".cs"))
            .Where(behind => behind.Exists)
            .SelectMany(behind => Travels().Matches(File.ReadAllText(behind.FullName)))
            .Select(match => match.Groups["element"].Value)
            .ToArray();

        named.ShouldNotBeEmpty(
            "no screen hands an element to ScreenMotion.ArriveOrLeave by name, so the check that "
            + "no live region is inside one has nothing to look at.");
    }

    /// <summary>An element this screen's code-behind moves on or off the screen, by name.</summary>
    [GeneratedRegex(@"ArriveOrLeave\(\s*(?<element>\w+)\s*,")]
    private static partial Regex Travels();

    /// <summary>Whether this element is declared a live region, by either spelling.</summary>
    private static bool IsALiveRegion(XElement element) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == LiveSetting)
        || element.Elements().Any(child => child.Name.LocalName == LiveSetting);

    /// <summary>
    /// Every property a live region's words can arrive on. Read as a set rather than as one name,
    /// because which one a line uses is a matter of what control it is — a <c>TextBlock</c> says
    /// <c>Text</c>, anything with content says <c>Content</c>, and a region announced by its
    /// automation name says that — and a check that knew only the control the screen happens to
    /// use today would wave the same defect through the day somebody changes it.
    /// </summary>
    private static readonly string[] Words = ["Text", "Content", "Header", "AutomationProperties.Name"];

    /// <summary>Whether an element says in the XAML what it reads as.</summary>
    /// <remarks>
    /// Both spellings again: the attribute, and the child element that sets the same property —
    /// <c>TextBlock.Text</c> for one of the control's own, and the attached one under its whole
    /// name. What this does not reach is words that never appear on the element at all: a style or
    /// a template setting one of these, or a control of our own with a words property under some
    /// other name. Neither exists on any screen here, and the second half below is what holds a
    /// region reached by neither, since it is told by name or it is not told at all.
    /// </remarks>
    private static bool Carries(XElement element) =>
        element.Attributes().Any(attribute => Words.Contains(attribute.Name.LocalName))
        || element.Elements().Any(child => Words.Any(word =>
            child.Name.LocalName == word
            || child.Name.LocalName.EndsWith("." + word, StringComparison.Ordinal)));

    /// <summary>Every element declared a live region, by either spelling of the attached property.</summary>
    private static IReadOnlyList<XElement> LiveRegions(string path) =>
        [.. XDocument.Load(path).Descendants().Where(IsALiveRegion)];

    private static string NameOf(XElement element) =>
        element.Attribute(Named)?.Value ?? element.Name.LocalName;
}
