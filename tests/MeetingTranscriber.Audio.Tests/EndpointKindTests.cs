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

    private static AudioDevice Playing(EndpointKind kind) =>
        new("{an-endpoint}", "An endpoint", IsDefault: true) { Kind = kind };
}
