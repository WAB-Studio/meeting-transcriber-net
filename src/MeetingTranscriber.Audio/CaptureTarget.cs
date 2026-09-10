using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What a channel is listening to: an endpoint Windows names, one program's audio wherever it comes
/// out, or everything this machine plays wherever that comes out.
/// </summary>
/// <remarks>
/// This is the only thing that knows there is more than one way to open a channel. Everything
/// downstream — the source, the session, the file, what gets printed — asks a target for its name
/// and for its stream, so which of the three a recording is on is not a branch repeated at each of
/// them. Channel 1 is always an endpoint, and channel 0 is never one: both shapes of channel 0 are
/// the same process-loopback activation under two modes, which is why moving between them mid
/// meeting is a stream swapped rather than a second kind of capture.
/// </remarks>
public abstract record CaptureTarget
{
    private protected CaptureTarget()
    {
    }

    /// <summary>What a person recording this would call it.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Which channel this feeds, which is fixed by what it is rather than chosen: an endpoint is
    /// the microphone, and both ways of getting what was played are channel 0.
    /// </summary>
    /// <remarks>
    /// Taken from here rather than passed in, because passed in it was a parameter two of the three
    /// ignored — so a target opened onto the wrong channel would have come back as a stream quietly
    /// labelled with the channel it was not.
    /// </remarks>
    public abstract AudioChannel Channel { get; }

    /// <summary>
    /// How channel 0 was obtained when it is this, or nothing when this is not a way of obtaining
    /// channel 0 at all — which is the microphone's endpoint and only that.
    /// </summary>
    /// <remarks>
    /// Here rather than worked out by whoever writes it down, because the ways in are what this
    /// type is for and this is the name one of them is stored under. A fourth way to open a channel
    /// has to answer it to compile, where a type test somewhere else would classify it silently as
    /// whichever way was written last.
    /// </remarks>
    public abstract CaptureMode? Mode { get; }

    /// <summary>
    /// Opens it for capture onto <see cref="Channel"/>, either as a channel's first device or as
    /// one taking over a channel that is already being recorded.
    /// </summary>
    /// <param name="carryingOn">
    /// The sequence this stream is to go on laying its packets out by, when it is taking over a
    /// channel whose device numbers no frames of its own, or nothing when it is starting one.
    /// </param>
    /// <remarks>
    /// <para>
    /// <see cref="ReopenedSource"/> is what says whether a channel already being recorded may go on
    /// being laid out by the sequence it was being laid out by, and it carries the words each
    /// refusal is made in. What that leaves is the whole machine's audio, which is the one thing a
    /// channel already being recorded is ever carried on to.
    /// </para>
    /// <para>
    /// <b>It is asked here and not in the ways in, so that none of them can forget.</b> This method
    /// is not the one a shape overrides — <see cref="Opening"/> is — so a fourth way in reaches
    /// <see cref="ReopenedSource"/> whether or not whoever wrote it knew the rule existed, and has
    /// to be given an answer there to take a sequence at all. Three overrides that each remembered
    /// to call it would be three copies of a call where there used to be three copies of a rule.
    /// </para>
    /// </remarks>
    internal WasapiStream Open(FramePositions? carryingOn)
    {
        ReopenedSource.EnsureMayCarryOn(this, carryingOn);

        return Opening(carryingOn);
    }

    /// <summary>
    /// Opens the stream itself, once <see cref="Open"/> has settled whether this source may take
    /// the sequence it is being handed.
    /// </summary>
    /// <remarks>
    /// The only thing a way in writes. It never sees a sequence it may not have, so it has no
    /// refusal of its own and cannot grow one that disagrees with the rule.
    /// </remarks>
    private protected abstract WasapiStream Opening(FramePositions? carryingOn);

    /// <summary>A device Windows names, which is the microphone and only ever the microphone.</summary>
    /// <remarks>
    /// What an endpoint names is a place sound comes out of, and channel 0 is not about a place: a
    /// machine playing through speakers and a headset at once has both of them in the recording.
    /// </remarks>
    public sealed record Endpoint(AudioDevice Device) : CaptureTarget
    {
        /// <inheritdoc/>
        public override string Name => Device.Name;

        /// <summary>
        /// Nothing: a microphone is not a way of obtaining channel 0, and there is no third mode
        /// meaning "this is the other channel". What reads this only ever reads channel 0's.
        /// </summary>
        public override CaptureMode? Mode => null;

        /// <inheritdoc/>
        public override AudioChannel Channel => AudioChannel.Microphone;

        /// <remarks>
        /// Always a stretch of its own, at its own zero, because a microphone numbers its own
        /// frames. A channel is moved onto one — that is what re-opening the microphone is — and it
        /// arrives here with no sequence to carry, which is why nothing about that move is refused.
        /// </remarks>
        private protected override WasapiStream Opening(FramePositions? carryingOn) =>
            WasapiStream.On(Device, Channel);
    }

    /// <summary>
    /// One program and everything it started. Only channel 0 can be one: a microphone hears a room
    /// and not a process.
    /// </summary>
    public sealed record Program(AudioProcess Process) : CaptureTarget
    {
        /// <inheritdoc/>
        public override string Name => Process.ToString();

        /// <inheritdoc/>
        public override CaptureMode? Mode => CaptureMode.ProcessLoopback;

        /// <inheritdoc/>
        public override AudioChannel Channel => AudioChannel.Loopback;

        /// <remarks>
        /// A program's audio numbers no frames either, so carrying a channel onto one would work —
        /// what says no is that nothing moves a recording onto a program. Both the answer and the
        /// sentence are <see cref="ReopenedSource"/>'s, so the day somebody decides a running
        /// channel may be pointed at another program, that is the one file they open.
        /// </remarks>
        private protected override WasapiStream Opening(FramePositions? carryingOn) =>
            WasapiStream.Following(Process);
    }

    /// <summary>
    /// Everything this machine is playing, wherever it comes out. Only channel 0 can be this one,
    /// for the reason a program can: a microphone hears a room.
    /// </summary>
    /// <remarks>
    /// It names nothing, and that is what it is — not a device, not a program, no id anything
    /// reopens it by. So every one of these equals every other one, which is right: there is one
    /// machine.
    /// </remarks>
    public sealed record TheWholeMachine : CaptureTarget
    {
        /// <summary>
        /// What a person recording this would call it. English, like every other name this
        /// application writes into a folder — and unlike a device's or a program's, which are
        /// names this machine gave, it is a phrase. So it goes in the folder and nowhere near a
        /// screen: what a screen is handed for this one is no words at all, and the catalogue has
        /// the sentence. See <c>ChannelReading.Capturing</c>.
        /// </summary>
        public override string Name => "everything this machine plays";

        /// <inheritdoc/>
        public override CaptureMode? Mode => CaptureMode.FullLoopback;

        /// <inheritdoc/>
        public override AudioChannel Channel => AudioChannel.Loopback;

        /// <remarks>
        /// The one way in that takes a sequence to carry on from, which is
        /// <see cref="ReopenedSource.CarriesTheSequenceOn"/>'s answer and not this method's: it
        /// comes off a virtual device that numbers nothing, so the channel goes on being laid out
        /// by the instants it was already being laid out by and there is no seam to reconcile.
        /// </remarks>
        private protected override WasapiStream Opening(FramePositions? carryingOn) =>
            WasapiStream.TheWholeMachine(carryingOn);
    }
}
