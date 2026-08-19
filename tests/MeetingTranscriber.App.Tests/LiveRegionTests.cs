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
/// What no probe here reaches is a narrator really reading it out; that is run by hand and written
/// down, like every other check that needs a packaged host.
/// </para>
/// </remarks>
public class LiveRegionTests
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

    /// <summary>Whether an element says in the XAML what it reads as.</summary>
    private static bool Carries(XElement element) =>
        element.Attribute("Text") is not null
        || element.Elements().Any(child => child.Name.LocalName.EndsWith(".Text", StringComparison.Ordinal));

    /// <summary>Every element declared a live region, by either spelling of the attached property.</summary>
    private static IReadOnlyList<XElement> LiveRegions(string path) =>
    [
        .. XDocument
            .Load(path)
            .Descendants()
            .Where(element =>
                element.Attributes().Any(attribute => attribute.Name.LocalName == LiveSetting)
                || element.Elements().Any(child => child.Name.LocalName == LiveSetting)),
    ];

    private static string NameOf(XElement element) =>
        element.Attribute(Named)?.Value ?? element.Name.LocalName;
}
