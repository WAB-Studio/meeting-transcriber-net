namespace MeetingTranscriber.Audio;

/// <summary>
/// What kind of thing an endpoint is, as the endpoint itself declares it. The one question this
/// application asks of it is whether what comes out of it lands in a room, where the microphone
/// recording the meeting hears it a second time.
/// </summary>
/// <remarks>
/// It is what the device says of itself and never a measurement. A driver fills this field in
/// once, at install, from what the jack is wired to — so it is free to be sure about and wrong
/// about a headset somebody plugged into a speaker socket. What rests on it is a line beside a
/// meter and never a recording that stops.
/// </remarks>
public enum EndpointKind
{
    /// <summary>
    /// The endpoint did not say. Most machines' do say, and the ones that do not are drivers
    /// that never filled the field in — which is a thing to be quiet about rather than to guess.
    /// </summary>
    Unsaid,

    /// <summary>Speakers: what is played through it comes out into the room.</summary>
    Speakers,

    /// <summary>Headphones, so what is played through it reaches one person's ears.</summary>
    Headphones,

    /// <summary>A headset, which is headphones with a microphone on the same device.</summary>
    Headset,

    /// <summary>
    /// Something else Windows names — a line-out, a digital passthrough, a monitor over HDMI, a
    /// microphone. None of them is a room this application is willing to be sure about.
    /// </summary>
    SomethingElse,
}

/// <summary>
/// What Windows' own form factor number means here. The mapping is the only thing between an
/// integer out of a property store and a warning somebody reads, so it is a rule with a test
/// rather than a cast at the point of use.
/// </summary>
public static class EndpointKinds
{
    /// <summary>
    /// The values of Windows' <c>EndpointFormFactor</c>, which is what
    /// <c>PKEY_AudioEndpoint_FormFactor</c> holds. Spelled out rather than cast into, because
    /// what arrives is whatever a driver wrote and a cast would name every unknown number after
    /// whichever enum member happened to sit at it.
    /// </summary>
    private const int RemoteNetworkDevice = 0;
    private const int Speakers = 1;
    private const int LineLevel = 2;
    private const int Headphones = 3;
    private const int Microphone = 4;
    private const int Headset = 5;
    private const int Handset = 6;
    private const int UnknownDigitalPassthrough = 7;
    private const int Spdif = 8;
    private const int DigitalAudioDisplayDevice = 9;

    /// <summary>
    /// What an endpoint declaring <paramref name="formFactor"/> is. Anything outside the range
    /// Windows names is <see cref="EndpointKind.Unsaid"/> rather than a refusal: a driver writing
    /// a number nobody documented is not a reason to fail to open a device.
    /// </summary>
    /// <remarks>
    /// A monitor over HDMI is <see cref="EndpointKind.SomethingElse"/> and not speakers, even
    /// though most of them have speakers in them. What the warning it feeds is worth is that it
    /// is never wrong — the same endpoint number is what an AV receiver and a capture card both
    /// report, and a line telling somebody wearing headphones that the room can hear them is a
    /// line they stop reading.
    /// </remarks>
    public static EndpointKind Of(int formFactor) => formFactor switch
    {
        Speakers => EndpointKind.Speakers,
        Headphones => EndpointKind.Headphones,
        Headset => EndpointKind.Headset,
        RemoteNetworkDevice
            or LineLevel
            or Microphone
            or Handset
            or UnknownDigitalPassthrough
            or Spdif
            or DigitalAudioDisplayDevice => EndpointKind.SomethingElse,

        // Windows' own `UnknownFormFactor`, which is 10, and a number it never named at all. One
        // arm, because they are one answer: a driver that has not said.
        _ => EndpointKind.Unsaid,
    };
}
