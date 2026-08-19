namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What an endpoint says it is, and the one question this application asks of that answer:
/// whether the meeting is coming out into the room, where the microphone hears it a second time.
/// </summary>
/// <remarks>
/// The numbers are Windows' own <c>EndpointFormFactor</c>, which is what
/// <c>PKEY_AudioEndpoint_FormFactor</c> holds. They are here as the integers a driver really
/// writes rather than as a cast of a named enum, because that is what the mapping has to survive:
/// a device is free to write anything into that field, and what comes back through a property
/// store is whatever it wrote.
/// </remarks>
public class EndpointKindTests
{
    /// <summary>Every form factor Windows names, and what each one means here.</summary>
    public static TheoryData<int, EndpointKind> FormFactors() => new()
    {
        { 0, EndpointKind.SomethingElse },  // RemoteNetworkDevice
        { 1, EndpointKind.Speakers },
        { 2, EndpointKind.SomethingElse },  // LineLevel
        { 3, EndpointKind.Headphones },
        { 4, EndpointKind.SomethingElse },  // Microphone
        { 5, EndpointKind.Headset },
        { 6, EndpointKind.SomethingElse },  // Handset
        { 7, EndpointKind.SomethingElse },  // UnknownDigitalPassthrough
        { 8, EndpointKind.SomethingElse },  // SPDIF
        { 9, EndpointKind.SomethingElse },  // DigitalAudioDisplayDevice
        { 10, EndpointKind.Unsaid },        // UnknownFormFactor
    };

    /// <summary>What a driver can write that Windows never named.</summary>
    public static TheoryData<int> NumbersWindowsNeverNamed() => [-1, 11, 255, int.MaxValue, int.MinValue];

    [Theory]
    [MemberData(nameof(FormFactors))]
    public void Every_form_factor_windows_names_is_one_of_the_kinds(int formFactor, EndpointKind kind) =>
        EndpointKinds.Of(formFactor).ShouldBe(kind);

    /// <summary>
    /// A number outside the range is a driver that has not said, and never one of the kinds. The
    /// alternative is a cast, which would name an undocumented number after whichever member
    /// happened to sit at it — and the member sitting at 1 is the one that puts a warning on
    /// somebody's screen.
    /// </summary>
    [Theory]
    [MemberData(nameof(NumbersWindowsNeverNamed))]
    public void A_number_windows_never_named_is_nothing_having_been_said(int formFactor) =>
        EndpointKinds.Of(formFactor).ShouldBe(EndpointKind.Unsaid);

    [Fact]
    public void Speakers_are_what_puts_the_meeting_into_the_room() =>
        Playing(EndpointKind.Speakers).PlaysIntoTheRoom.ShouldBeTrue();

    /// <summary>
    /// ISC-150's other half, and the half the warning is worth anything for. Told once that the
    /// room can hear them while they are wearing a headset, nobody reads the line again — so
    /// everything that is not speakers, the endpoint that did not say included, says nothing.
    /// </summary>
    [Theory]
    [InlineData(EndpointKind.Headphones)]
    [InlineData(EndpointKind.Headset)]
    [InlineData(EndpointKind.SomethingElse)]
    [InlineData(EndpointKind.Unsaid)]
    public void Nothing_else_is_taken_for_a_room(EndpointKind kind) =>
        Playing(kind).PlaysIntoTheRoom.ShouldBeFalse();

    /// <summary>
    /// A device nobody asked about is a device that has not said. It is the default because the
    /// alternative is a device built from a name somebody typed carrying an answer the machine
    /// never gave.
    /// </summary>
    [Fact]
    public void A_device_nobody_asked_about_has_not_said_what_it_is()
    {
        var device = new AudioDevice("{an-endpoint}", "An endpoint", IsDefault: true);

        device.Kind.ShouldBe(EndpointKind.Unsaid);
        device.PlaysIntoTheRoom.ShouldBeFalse();
    }

    /// <summary>
    /// The kind is part of the value. This record is what the machine answered about an endpoint,
    /// the same way <see cref="AudioDevice.IsDefault"/> is, and two answers that disagree are two
    /// answers — so a device nobody asked is not equal to the same endpoint enumerated.
    /// </summary>
    /// <remarks>
    /// Written down because it is the surprising half and the code has no other way to say it: the
    /// alternative was an <c>Equals</c> by hand leaving one field out, which the next field added
    /// would have to remember. What it costs is that value equality is not identity here, and what
    /// makes that safe is that nothing asking "the same device" uses it —
    /// <see cref="AudioDevices.Choose"/> and the window's picker both go by
    /// <see cref="AudioDevice.Id"/>.
    /// </remarks>
    [Fact]
    public void A_device_nobody_asked_about_is_a_different_answer_from_the_same_endpoint_enumerated()
    {
        var unasked = new AudioDevice("{an-endpoint}", "An endpoint", IsDefault: true);

        (unasked with { Kind = EndpointKind.Speakers }).ShouldNotBe(unasked);
    }

    /// <summary>
    /// And the half that makes the one above safe: what names an endpoint is its id, so a device
    /// carrying an answer nobody gave is still found among the ones that were enumerated.
    /// </summary>
    /// <remarks>
    /// Over <see cref="AudioDevices.Choose"/> rather than over an id comparison written here,
    /// because what has to be true is that the application's own way of finding a device does not
    /// go through equality. The day somebody writes <c>Contains</c> or <c>IndexOf</c> instead, this
    /// is what says the microphone somebody chose can no longer be found.
    /// </remarks>
    [Fact]
    public void An_endpoint_is_found_by_its_id_whatever_it_was_asked_about()
    {
        var unasked = new AudioDevice("{an-endpoint}", "An endpoint", IsDefault: true);
        var enumerated = unasked with { Kind = EndpointKind.Speakers };

        AudioDevices.Choose([enumerated], unasked.Id).ShouldBe(enumerated);
    }

    private static AudioDevice Playing(EndpointKind kind) =>
        new("{an-endpoint}", "An endpoint", IsDefault: true) { Kind = kind };
}
