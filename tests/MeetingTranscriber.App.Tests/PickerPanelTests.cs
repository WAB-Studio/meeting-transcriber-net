using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// Every picker in the application is the application's own drop-down, whose open list sits inside
/// the window, under its pill or over it, and is bounded by its own ceiling.
/// </summary>
/// <remarks>
/// <para>
/// A platform <c>ComboBox</c> opens its list in a window the platform places, for a carousel, and
/// three batches of patching where that window went were each seen on screen to have failed: the
/// list opened over the entry that was chosen, as wide as the monitor, part way down the alphabet.
/// The class keeps the name it had while it held the carousel's check, because the claim it
/// evidences (ISC-158.1, a list opening where its first entry is seen first) is still this class's.
/// </para>
/// <para>
/// This reads the markup rather than a running window, for the reason <see cref="ScreenTextsTests"/>
/// gives: a WinUI tree needs a UI thread and a packaged host that a build agent has not got. What
/// it can hold is what is silent when it goes — a popup not constrained to the window, a picker
/// that is the platform's again — and the evidence that the list opens where it should is a UI
/// probe on a packaged build. Where the list goes is arithmetic and is <c>ListPlacementTests</c>'s.
/// </para>
/// </remarks>
public class PickerPanelTests
{
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The one place the word is allowed in code: what kind of control a screen reader is told it is.</summary>
    private const string TheControlTypeItIsReadAs = "AutomationControlType.ComboBox";

    [Fact]
    public void No_screen_holds_a_platform_ComboBox()
    {
        var markup = AppSources.With(".xaml")
            .Where(file => File.ReadAllText(file.FullName).Contains("<ComboBox", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToArray();

        var code = AppSources.With(".cs")
            .Where(file => SourceLines
                .Occurrences(File.ReadAllText(file.FullName).Replace(TheControlTypeItIsReadAs, string.Empty, StringComparison.Ordinal), "ComboBox")
                .Any())
            .Select(file => file.Name)
            .ToArray();

        markup.ShouldBeEmpty(
            "These screens draw a platform ComboBox, whose list is a window the platform places: "
            + string.Join("; ", markup));

        code.ShouldBeEmpty(
            "These files name the ComboBox type, so a picker is the platform's again: "
            + string.Join("; ", code));
    }

    [Fact]
    public void The_open_list_stays_inside_the_window_under_its_pill()
    {
        // The popup is constrained to the root of the window it is in, and dismissed by a press
        // outside it: without the first the platform may put it in a window of its own as wide as
        // the monitor, and without the second a list stays open over whatever was pressed.
        var popup = Picker()
            .Descendants(XName.Get("Popup", Xaml))
            .Single();

        ((string?)popup.Attribute("ShouldConstrainToRootBounds")).ShouldBe(
            "True", "The drop-down's popup is not constrained to the window's root.");
        ((string?)popup.Attribute("IsLightDismissEnabled")).ShouldBe(
            "True", "A press outside the open list does not close it.");
    }

    [Fact]
    public void The_open_list_is_bounded_by_the_pickers_own_ceiling()
    {
        // With nothing bounding it a list is as tall as the window and opens over its own pill. The
        // ceiling is the control's `MaxDropDownHeight`, and it is handed to the one rule that
        // decides where a list goes.
        var source = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "DropDown.cs")).FullName);

        var call = source[source.IndexOf("ListPlacement.For(", StringComparison.Ordinal)..];
        call = call[..call.IndexOf(");", StringComparison.Ordinal)];

        call.ShouldContain(
            "MaxDropDownHeight",
            customMessage: "DropDown does not hand MaxDropDownHeight to ListPlacement.For, so an "
            + "open list is as tall as the window.");
    }

    [Fact]
    public void An_unanswered_pill_keeps_its_rule()
    {
        // MainWindow's DropDownWantingAnAnswer sets BorderBrush to pico. A template that draws the
        // pill's border from a fixed brush ignores it, and the question still unanswered is no
        // longer drawn where it is missing.
        var pill = Picker()
            .Descendants()
            .Single(element => (string?)element.Attribute(XName.Get("Name", X)) == "Pill");

        ((string?)pill.Attribute("BorderBrush")).ShouldBe(
            "{TemplateBinding BorderBrush}",
            "The pill's rule is not the control's own BorderBrush, so a style that sets it is ignored.");
    }

    [Fact]
    public void Every_picker_on_every_screen_is_drawn_from_that_one_style()
    {
        // Without this the template above is a style nothing has to use. A picker naming another
        // style, or none, has no template at all, and would look right in the designer either way.
        var strays = AppSources.With(".xaml")
            .Where(file => !file.Name.Equals("Olivo.xaml", StringComparison.Ordinal))
            .SelectMany(file => Pickers(file)
                .Where(picker => (string?)picker.Attribute("Style") != "{StaticResource DropDown}")
                .Select(picker => $"{file.Name}: {(string?)picker.Attribute(XName.Get("Name", X))}"))
            .ToArray();

        strays.ShouldBeEmpty(
            "These pickers are not drawn from Olivo's DropDown, so they have no template: "
            + string.Join("; ", strays));
    }

    /// <summary>
    /// The properties a <c>ComboBox</c> took and the application's own pill does not draw.
    /// </summary>
    /// <remarks>
    /// A pill has no header, no description and no editable field — that is what lets the box
    /// itself stand at the control rank's 34 — so the template has no part for any of them. For
    /// the control itself they are not even properties now, and the XAML compiler says so; this is
    /// what stays true if somebody adds one to the control without a part to draw it.
    /// <c>PlaceholderText</c> is the exception and is not here: the template draws it.
    /// </remarks>
    public static TheoryData<string> WhatThePillDoesNotDraw() => ["Header", "Description", "IsEditable"];

    [Theory]
    [MemberData(nameof(WhatThePillDoesNotDraw))]
    public void No_picker_names_something_the_pill_does_not_draw(string property)
    {
        var named = AppSources.With(".xaml")
            .Where(file => !file.Name.Equals("Olivo.xaml", StringComparison.Ordinal))
            .SelectMany(file => Pickers(file)
                .Where(picker => picker.Attribute(property) is not null)
                .Select(picker => $"{file.Name}: {(string?)picker.Attribute(XName.Get("Name", X))}"))
            .ToArray();

        named.ShouldBeEmpty(
            $"These pickers set {property}, which Olivo's pill has no part for, so it would draw "
            + "nothing: " + string.Join("; ", named));
    }

    /// <summary>Every <c>local:DropDown</c> in a file, whatever the prefix the file gives its namespace.</summary>
    private static IEnumerable<XElement> Pickers(FileInfo file)
    {
        return XDocument
            .Load(file.FullName)
            .Descendants()
            .Where(element => element.Name.LocalName == "DropDown");
    }

    private static XElement Picker()
    {
        var file = AppSources.With(".xaml")
            .Single(found => found.Name.Equals("Olivo.xaml", StringComparison.Ordinal));

        var picker = XDocument
            .Load(file.FullName)
            .Descendants(XName.Get("Style", Xaml))
            .SingleOrDefault(style => (string?)style.Attribute(XName.Get("Key", X)) == "DropDown");

        picker.ShouldNotBeNull("Olivo.xaml has no style keyed DropDown.");
        return picker;
    }
}
