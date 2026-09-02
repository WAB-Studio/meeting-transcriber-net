using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// A picker holding more than fits opens at the top of itself, so the entry put first is the one
/// somebody sees first.
/// </summary>
/// <remarks>
/// A <c>ComboBox</c> lays its list out on a <c>CarouselPanel</c> unless it is told otherwise, and
/// a carousel decides where a list too long to fit opens. With nothing chosen, what channel 0 can
/// follow — every program on the machine — opened part way down the alphabet, so the whole
/// machine, which is deliberately first because it is the answer that is always right about what
/// it records, was not on screen when the list was.
/// <para>
/// This reads the markup rather than a running window, for the reason <see cref="ScreenTextsTests"/>
/// gives: a WinUI tree needs a UI thread and a packaged host that a build agent has not got. What
/// it can hold is the one thing whose absence is silent — a picker with no items panel of its own
/// takes the carousel back, and every screen still looks right in the designer. The evidence that
/// it opens at the top is a UI probe on a packaged build, recorded against ISC-158.1; this is what
/// keeps that evidence from going stale without anybody hearing.
/// </para>
/// <para>
/// It holds the system's own picker style now and no longer one picker on one screen. Naming one
/// was right while the rule bit on exactly one list — the microphones, the languages and what will
/// be spoken are each a handful — and it stopped being right when the pickers became pills drawn
/// from a single style with a template of its own. Settled in the dictionary the rule cannot be
/// forgotten by the next screen at all, and a carousel is not something this design wants
/// anywhere. The second check below is what stops that from being a rule nothing has to obey.
/// </para>
/// </remarks>
public class PickerPanelTests
{
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void The_picker_lays_its_list_out_on_something_other_than_a_carousel()
    {
        var panel = Picker()
            .Elements(XName.Get("Setter", Xaml))
            .Where(setter => (string?)setter.Attribute("Property") == "ItemsPanel")
            .Elements(XName.Get("Setter.Value", Xaml))
            .Elements(XName.Get("ItemsPanelTemplate", Xaml))
            .Elements()
            .SingleOrDefault();

        panel.ShouldNotBeNull(
            "The DropDown style declares no ItemsPanel, so every picker in the application is back "
            + "on a ComboBox's own CarouselPanel and opens wherever that puts it.");
        panel.Name.LocalName.ShouldNotBe("CarouselPanel");
    }

    [Fact]
    public void Every_picker_on_every_screen_is_drawn_from_that_one_style()
    {
        // Without this the check above holds a style nothing has to use. A picker naming another
        // style, or none, is one carousel back and one Windows skin back — and it would look right
        // in the designer either way.
        var strays = AppSources.With(".xaml")
            .Where(file => !file.Name.Equals("Olivo.xaml", StringComparison.Ordinal))
            .SelectMany(file => XDocument.Load(file.FullName)
                .Descendants(XName.Get("ComboBox", Xaml))
                .Where(picker => (string?)picker.Attribute("Style") != "{StaticResource DropDown}")
                .Select(picker => $"{file.Name}: {(string?)picker.Attribute(XName.Get("Name", X))}"))
            .ToArray();

        strays.ShouldBeEmpty(
            "These pickers are not drawn from Olivo's DropDown, so they are the platform's: "
            + string.Join("; ", strays));
    }

    /// <summary>
    /// The properties a `ComboBox` still takes and Olivo's picker no longer draws.
    /// </summary>
    /// <remarks>
    /// A pill has no header, no description and no editable field — that is what lets the box
    /// itself stand at the control rank's 34 — so the template has no part for any of them. None of
    /// that is a compile error and none of it is an exception: the property binds, the build is
    /// green, and the label is simply not on the screen. It already happened, to the picker on the
    /// packaging-checks window, and it survived every check in this repository including the one
    /// above — which holds that every picker takes this template, and so guarantees the next screen
    /// meets it too. <c>PlaceholderText</c> is the exception and is not here: the template draws it.
    /// </remarks>
    public static TheoryData<string> WhatThePillDoesNotDraw() => ["Header", "Description", "IsEditable"];

    [Theory]
    [MemberData(nameof(WhatThePillDoesNotDraw))]
    public void No_picker_names_something_the_pill_does_not_draw(string property)
    {
        var named = AppSources.With(".xaml")
            .Where(file => !file.Name.Equals("Olivo.xaml", StringComparison.Ordinal))
            .SelectMany(file => XDocument.Load(file.FullName)
                .Descendants(XName.Get("ComboBox", Xaml))
                .Where(picker => picker.Attribute(property) is not null)
                .Select(picker => $"{file.Name}: {(string?)picker.Attribute(XName.Get("Name", X))}"))
            .ToArray();

        named.ShouldBeEmpty(
            $"These pickers set {property}, which Olivo's pill has no part for, so it binds and "
            + "draws nothing: " + string.Join("; ", named));
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
