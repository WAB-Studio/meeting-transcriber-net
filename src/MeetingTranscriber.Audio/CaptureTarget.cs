using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What a channel is listening to: an endpoint Windows names, or one program's audio wherever it
/// comes out.
/// </summary>
/// <remarks>
/// This is the only thing that knows there is more than one way to open a channel. Everything
/// downstream — the source, the session, the file, what gets printed — asks a target for its name
/// and for its stream, so following a program is not a branch that has to be repeated at each of
/// them. Channel 1 is always an endpoint; channel 0 is whichever of the two the meeting called for.
/// </remarks>
public abstract record CaptureTarget
{
    private protected CaptureTarget()
    {
    }

    /// <summary>What a person recording this would call it.</summary>
    public abstract string Name { get; }

    /// <summary>Opens it for capture onto <paramref name="channel"/>.</summary>
    internal abstract WasapiStream Open(AudioChannel channel);

    /// <summary>A device: the microphone, or everything the machine is playing.</summary>
    public sealed record Endpoint(AudioDevice Device) : CaptureTarget
    {
        /// <inheritdoc/>
        public override string Name => Device.Name;

        internal override WasapiStream Open(AudioChannel channel) => WasapiStream.On(Device, channel);
    }

    /// <summary>
    /// One program and everything it started. Only channel 0 can be one: a microphone hears a room
    /// and not a process.
    /// </summary>
    public sealed record Program(AudioProcess Process) : CaptureTarget
    {
        /// <inheritdoc/>
        public override string Name => Process.ToString();

        // The channel is not read, and there is no guard saying it must be channel 0: a session is
        // the only thing that builds one of these and it puts it nowhere else, so a check here
        // would be a refusal for something nothing produces.
        internal override WasapiStream Open(AudioChannel channel) => WasapiStream.Following(Process);
    }
}
