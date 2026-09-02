namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// ISC-152, over a line half of which came from the catalogue and half from a machine. The walk
/// beside this proves every entry exists in both languages; it says nothing about what a screen
/// puts around a name it was handed — which is how <c>(default)</c> reached a Spanish picker for
/// as long as the recorder has had one.
/// </summary>
/// <remarks>
/// The device's own name is the falsifier here rather than a detail of the fixture: a check that
/// only asked whether the two languages differ would pass on a line that translated the name as
/// well, and the maker's name is not this application's to translate. So each assertion says both
/// halves — the name survives untouched, and nothing else on the line does.
/// <para>
/// This holds the words. What holds the screen to reaching for them is
/// <c>ScreenTextsTests.No_screen_shows_a_person_what_a_type_says_about_itself</c>, which reads the
/// recorder's own source: neither is enough alone, because the words being right proves nothing
/// about a picker that goes on rendering an <c>AudioDevice</c> instead.
/// </para>
/// </remarks>
public class DeviceLinesTests
{
    /// <summary>A name with no word of either language in it, so that anything found was added.</summary>
    private const string Maker = "Krisp Microphone (Elgato Wave XLR)";

    [Fact]
    public void The_default_device_is_marked_in_the_language_being_read()
    {
        // The defect, said as what somebody reading the application sees: one English word on a
        // Spanish screen, put there by the application and not by the maker of the device.
        var line = DeviceLines.Of(Maker, isDefault: true);

        line.In(UiLanguage.Spanish).ShouldBe($"{Maker} (predeterminado)");
        line.In(UiLanguage.English).ShouldBe($"{Maker} (default)");
    }

    [Fact]
    public void A_device_that_is_not_the_default_is_its_own_name_and_nothing_else()
    {
        // No marking in either language rather than a translated absence: there is nothing to say
        // about an endpoint Windows does not reach for, and a picker that said so anyway would be
        // this application adding a word to every line it has.
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            DeviceLines.Of(Maker, isDefault: false).In(language).ShouldBe(Maker);
        }
    }

    [Fact]
    public void What_is_put_around_a_device_name_is_the_catalogue_and_not_this_type()
    {
        // Held against the entry and not against the two literals above, so that translating
        // `TheDeviceWindowsUsesByDefault` moves this line with it instead of going red for having
        // been improved. What it refuses is the line built anywhere but out of that entry.
        foreach (var language in Enum.GetValues<UiLanguage>())
        {
            DeviceLines.Of(Maker, isDefault: true).In(language)
                .ShouldBe(UiTexts.TheDeviceWindowsUsesByDefault.In(language, Maker), language.ToString());
        }
    }

    [Fact]
    public void An_endpoint_windows_named_nothing_draws_an_empty_row_rather_than_closing_the_window()
    {
        // Not an edge case somebody imagined: it is what the alternative would have cost. A guard
        // here throws inside the recorder's constructor and inside the callback a device change
        // fires, so a driver answering with a blank name would mean the window did not open — over
        // a row a person can see and pick around.
        DeviceLines.Of(string.Empty, isDefault: false).In(UiLanguage.Spanish).ShouldBeEmpty();
    }
}
