using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio;

/// <summary>
/// One block of samples as a device handed it over, carrying the two numbers that say where it
/// belongs: how many frames that device had produced before this block's first frame, and when
/// it read that count on the machine's monotonic clock.
/// </summary>
/// <remarks>
/// <para>
/// Both numbers are WASAPI's own report of the packet, and neither is inferred from when the
/// application got round to looking. Counting the bytes that arrived would give the same position
/// only while nothing is ever lost — which is exactly the case where the number is not needed. A
/// stretch the device dropped shows up as a jump in <see cref="DevicePosition"/> and as nothing at
/// all in a byte count, so a timeline built on bytes closes the hole up and reports a meeting
/// shorter than the one that happened.
/// </para>
/// <para>
/// WASAPI reports two flags with a packet and only one of them is here. It sets a timestamp error
/// when it cannot say when it read the position it just handed over — the position itself still
/// stands, and it is only the instant beside it that is not the device's word — and that has to be
/// on the packet: the timeline stops a recording whose clock does not add up, so a device saying
/// "do not measure against this one" is the difference between skipping an observation and throwing
/// away a meeting. It also sets a data discontinuity when a glitch cost the stream some audio, and
/// that one is deliberately not here — a lost stretch is a jump in <see cref="DevicePosition"/>,
/// which is the same news arriving with its length attached, and a flag nothing reads is one more
/// thing that can disagree with the number beside it.
/// </para>
/// <para>
/// The format is not on every packet, and it is on exactly the packets where a source starts
/// being a different device: <see cref="Opening"/>. A device that changes format mid recording is
/// a new stretch of the recording rather than an odd packet, so what declares a format is the
/// packet that begins a stretch, and every packet after it is that stretch's until the next one
/// says otherwise.
/// </para>
/// <para>
/// Nor is the unit of <see cref="DevicePosition"/>, and that one is not a decision but something
/// nothing here can know. A shared-mode client is handed the format it asked for, converted; the
/// counter is not converted with it, and a device that runs at another rate goes on counting in its
/// own frames. So the number is the device's own and says where the block goes only once something
/// has decided it can be laid out at all — which is <see cref="SourcePositions"/>, and it is the
/// only thing that may read this as a position on the recording.
/// </para>
/// </remarks>
/// <param name="Channel">Which of the two sources this came from.</param>
/// <param name="DevicePosition">
/// Frames the device had produced before this block's first frame, counted in whatever the device
/// counts in, which is not always the frames of <paramref name="Samples"/>.
/// </param>
/// <param name="CapturedAt">When the device read that position, on the shared monotonic clock.</param>
/// <param name="Samples">The block's bytes, in the source's own format.</param>
/// <param name="TimingIsSound">
/// Whether the device vouched for <see cref="CapturedAt"/>. False is WASAPI's timestamp error: the
/// samples are still the meeting and <see cref="DevicePosition"/> is still where they go, and only
/// the observation of how fast this device runs is thrown away.
/// </param>
/// <param name="Opening">
/// The format this packet's device hands over, when this packet is the first of a stretch that
/// device is recording, and nothing on every other packet.
/// </param>
/// <remarks>
/// A stretch is what a channel that changed device is made of. The device that takes over numbers
/// its frames from its own zero and is free to hand over another format entirely, so
/// <see cref="DevicePosition"/> on this packet is measured against nothing before it and the
/// samples behind it are read at this format rather than at the one before. Everything either side
/// of the seam is still one source and one file — what the gap between them is worth is the
/// timeline's, and it is worth exactly the audio that never arrived.
/// </remarks>
public sealed record CapturePacket(
    AudioChannel Channel,
    long DevicePosition,
    MonotonicInstant CapturedAt,
    ReadOnlyMemory<byte> Samples,
    bool TimingIsSound = true,
    StreamFormat? Opening = null);
