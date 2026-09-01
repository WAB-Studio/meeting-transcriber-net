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
/// One picker and not every one of them. The rule bites only where a list is longer than its drop
/// down, and the microphones, the languages and what will be spoken are each a handful — a rule
/// applied to them would be a rule nothing could break.
/// </para>
/// </remarks>
public class PickerPanelTests
{
    private const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void The_source_picker_lays_its_list_out_on_something_other_than_a_carousel()
    {
        var picker = MainWindow()
            .Descendants(XName.Get("ComboBox", Xaml))
            .SingleOrDefault(element => (string?)element.Attribute(XName.Get("Name", X)) == "SourcePicker");

        picker.ShouldNotBeNull("MainWindow.xaml has no ComboBox named SourcePicker.");

        var panel = picker
            .Elements(XName.Get("ComboBox.ItemsPanel", Xaml))
            .Elements(XName.Get("ItemsPanelTemplate", Xaml))
            .Elements()
            .SingleOrDefault();

        panel.ShouldNotBeNull(
            "SourcePicker declares no ItemsPanel, so its list is back on a ComboBox's own "
            + "CarouselPanel and opens wherever that puts it.");
        panel.Name.LocalName.ShouldNotBe("CarouselPanel");
    }

    private static XDocument MainWindow()
    {
        var file = AppSources.With(".xaml")
            .Single(found => found.Name.Equals("MainWindow.xaml", StringComparison.Ordinal));

        return XDocument.Load(file.FullName);
    }
}
