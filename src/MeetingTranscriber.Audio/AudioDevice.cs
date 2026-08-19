namespace MeetingTranscriber.Audio;

/// <summary>
/// An audio endpoint this machine offers, named the way Windows names it. The id is what a
/// capture reopens it by; the name is what a person recognises and what they type.
/// </summary>
/// <remarks>
/// It says nothing about which channel it feeds. A device is a device, and which of the two
/// channels it lands on is <see cref="Domain.Audio.AudioChannel"/>'s to say — the microphone of
/// one recording is an endpoint like any other.
/// </remarks>
public sealed record AudioDevice(string Id, string Name, bool IsDefault)
{
    /// <summary>
    /// What the endpoint says it is. Not a positional value, because it is the machine's answer
    /// about a device and not part of what names one: a device built to be recorded from — in a
    /// test, or from a name somebody typed — is the same device whether or not anything asked.
    /// </summary>
    public EndpointKind Kind { get; init; } = EndpointKind.Unsaid;

    /// <summary>
    /// Whether what is played through this endpoint comes out into the room, where the microphone
    /// recording the meeting hears it a second time.
    /// </summary>
    /// <remarks>
    /// Speakers and nothing else, including the endpoint that did not say. What this feeds is a
    /// warning that costs nothing to be sure of, and its whole worth is that it is never wrong:
    /// told once that the room can hear them while they are wearing headphones, nobody reads the
    /// line again. Measuring how much of channel 0 really comes back in on channel 1 is the audio
    /// engine's, and it is not this.
    /// </remarks>
    public bool PlaysIntoTheRoom => Kind is EndpointKind.Speakers;

    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}
