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
    /// What the endpoint says it is, or <see cref="EndpointKind.Unsaid"/> when nothing asked it.
    /// </summary>
    /// <remarks>
    /// Part of the value like every other field here, the same way <see cref="IsDefault"/> is:
    /// this record is what the machine answered about an endpoint, and two answers that disagree
    /// are two answers. What names the endpoint is <see cref="Id"/>, and anything asking whether
    /// two of these are the same device asks that — which is what the window's picker does. The
    /// alternative would be an <c>Equals</c> written by hand to leave one field out, and the next
    /// field added would have to remember it.
    /// <para>
    /// Not positional, because no caller outside enumeration can answer it: the form factor is
    /// read off an open endpoint, so everywhere else is a device nothing asked, which is what the
    /// default says rather than something each call site has to spell.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// How a report or a log names it: the maker's name, and whether Windows reaches for this one
    /// when nothing was asked for.
    /// </summary>
    /// <remarks>
    /// English, and never what a window shows. This is the audio domain, which does not know what
    /// language somebody is reading the application in and is given no way to find out, so a
    /// screen putting this in front of a person says <c>(default)</c> to somebody who chose
    /// Spanish. What a picker shows is <c>DeviceLines.Of</c> in
    /// <c>MeetingTranscriber.Presentation</c>, where the whole line is an entry of the catalogue
    /// with the maker's name inside it. What is left here is for the command line and for whoever
    /// reads a report afterwards.
    /// </remarks>
    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}
