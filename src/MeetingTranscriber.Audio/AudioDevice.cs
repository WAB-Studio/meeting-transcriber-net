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
    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}
