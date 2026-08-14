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
/// The format is not on the packet: a source declares it once when the timeline is built, because
/// a device that changes format mid recording is a new stretch of the recording rather than an odd
/// packet, and nothing yet asks the timeline for that.
/// </para>
/// </remarks>
/// <param name="Channel">Which of the two sources this came from.</param>
/// <param name="DevicePosition">Frames the device had produced before this block's first frame.</param>
/// <param name="CapturedAt">When the device read that position, on the shared monotonic clock.</param>
/// <param name="Samples">The block's bytes, in the source's own format.</param>
public sealed record CapturePacket(
    AudioChannel Channel,
    long DevicePosition,
    MonotonicInstant CapturedAt,
    ReadOnlyMemory<byte> Samples);
