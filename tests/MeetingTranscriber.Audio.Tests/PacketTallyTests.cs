using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a source's packets add up to while it is being recorded, driven by packets nobody recorded.
/// Every test here is a pair of runs whose bytes are identical and whose device positions are not,
/// because that is the only difference a tally counting bytes could not see.
/// </summary>
public class PacketTallyTests
{
    private static readonly StreamFormat StereoFloat = new(48_000, 2, 32, SampleEncoding.IeeeFloat);

    /// <summary>The frames one fabricated packet carries, and therefore 10 ms of a 48 kHz device.</summary>
    private const int PacketFrames = 480;

    /// <summary>ISC-64. Nothing lost, and the stretch covered is exactly what the positions span.</summary>
    [Fact]
    public void A_source_that_lost_nothing_covers_what_its_positions_span()
    {
        var tally = Take(Packets());

        tally.Packets.ShouldBe(100);
        tally.Covers.Milliseconds.ShouldBe(1_000);
        tally.Lost.Milliseconds.ShouldBe(0);
        tally.Unvouched.ShouldBe(0);
    }

    /// <summary>
    /// ISC-64 and ISC-70. Ninety packets either way, so the two files are byte for byte the same
    /// length — and one of them is a second of a meeting with a tenth of it missing while the other
    /// is nine tenths of a second with nothing missing at all. A tally counting what arrived cannot
    /// tell them apart, which is what makes the shorter recording the silent failure it is.
    /// </summary>
    [Fact]
    public void The_same_bytes_are_a_hole_or_a_shorter_source_depending_on_the_positions()
    {
        var holed = Take(Packets(delivers: position => position is < 4_800 or >= 9_600));
        var shorter = Take(Packets().Take(90));

        holed.Packets.ShouldBe(90);
        holed.Covers.Milliseconds.ShouldBe(1_000);
        holed.Lost.Milliseconds.ShouldBe(100);

        shorter.Packets.ShouldBe(90);
        shorter.Covers.Milliseconds.ShouldBe(900);
        shorter.Lost.Milliseconds.ShouldBe(0);
    }

    /// <summary>
    /// ISC-65. The device reads a packet every ten milliseconds, and the one after a hole a
    /// hundred and ten milliseconds after the one before it — the hundred nobody was handed plus
    /// the ten the last delivered packet was itself worth. Instants stamped where the application
    /// collected the packets would put a whole poll's worth of them at no distance at all.
    /// </summary>
    [Fact]
    public void Packets_are_as_far_apart_as_the_device_read_them()
    {
        var steady = Take(Packets());
        var holed = Take(Packets(delivers: position => position is < 4_800 or >= 9_600));

        steady.Closest.Milliseconds.ShouldBe(10);
        steady.Furthest.Milliseconds.ShouldBe(10);

        holed.Closest.Milliseconds.ShouldBe(10);
        holed.Furthest.Milliseconds.ShouldBe(110);
    }

    /// <summary>
    /// ISC-65. A timestamp error is the device saying it cannot say <em>when</em> it read the
    /// count, never that the count is wrong. So the block is still placed by its position and still
    /// part of the length, and only the step across it goes unmeasured — dropping it instead would
    /// lose ten milliseconds of a meeting over a clock reading nothing here needs.
    /// </summary>
    [Fact]
    public void A_packet_whose_instant_the_device_would_not_vouch_for_still_counts_as_meeting()
    {
        var packets = Packets().Take(3).ToArray();
        var tally = Take([packets[0], packets[1] with { TimingIsSound = false }, packets[2]]);

        tally.Packets.ShouldBe(3);
        tally.Unvouched.ShouldBe(1);
        tally.Lost.Milliseconds.ShouldBe(0);
        tally.Covers.Milliseconds.ShouldBe(30);

        // The only step between two instants the device vouched for spans the packet it did not,
        // rather than reading as one step of no length and one of twenty.
        tally.Closest.Milliseconds.ShouldBe(20);
        tally.Furthest.Milliseconds.ShouldBe(20);
    }

    /// <summary>
    /// ISC-70. The device disowning its clock from the first packet onwards is not the recording
    /// being empty. Measuring the length from the first instant it vouched for would report nothing
    /// at all for a file with a meeting in it.
    /// </summary>
    [Fact]
    public void A_source_that_opened_with_an_unvouched_packet_still_covers_it()
    {
        var tally = Take(Packets().Take(3).Select(packet => packet with { TimingIsSound = false }));

        tally.Packets.ShouldBe(3);
        tally.Unvouched.ShouldBe(3);
        tally.Covers.Milliseconds.ShouldBe(30);
        tally.Lost.Milliseconds.ShouldBe(0);
        tally.Closest.Milliseconds.ShouldBe(0);
        tally.Furthest.Milliseconds.ShouldBe(0);
    }

    /// <summary>
    /// ISC-132. The tally counts on the same numbers the rebuild lays the recording out on, and it
    /// has to: a microphone numbering its frames in its own rate advances its counter by a third of
    /// what it hands over, so read as the device's own the second of meeting this covers reads as a
    /// third of a second with two thirds of it lost — a person watching a meeting record being told
    /// live that most of it is going missing while the file it becomes loses nothing at all.
    /// </summary>
    [Fact]
    public void A_source_numbering_its_frames_at_its_own_rate_covers_the_meeting_and_loses_none_of_it()
    {
        var tally = Take(Fabricated.Packets(
            AudioChannel.Loopback, StereoFloat, 48_000, 0, 1, Fabricated.Quiet, PacketFrames, countsAtRate: 16_000));

        tally.Packets.ShouldBe(100);
        tally.Covers.Milliseconds.ShouldBe(1_000);
        tally.Lost.Milliseconds.ShouldBe(0);
    }

    /// <summary>A tally nothing reached says so rather than saying a length it cannot know.</summary>
    [Fact]
    public void A_source_that_never_spoke_covers_nothing()
    {
        var tally = new PacketTally(StereoFloat);

        tally.Packets.ShouldBe(0);
        tally.Covers.Milliseconds.ShouldBe(0);
        tally.Lost.Milliseconds.ShouldBe(0);
        tally.Closest.Milliseconds.ShouldBe(0);
        tally.Furthest.Milliseconds.ShouldBe(0);
    }

    /// <summary>One second of a 48 kHz device, in the packets it would hand over.</summary>
    private static IEnumerable<CapturePacket> Packets(Func<long, bool>? delivers = null) =>
        Fabricated.Packets(
            AudioChannel.Loopback,
            StereoFloat,
            48_000,
            0,
            1,
            Fabricated.Quiet,
            PacketFrames,
            delivers);

    private static PacketTally Take(IEnumerable<CapturePacket> packets)
    {
        var tally = new PacketTally(StereoFloat);
        foreach (var packet in packets)
        {
            tally.Add(packet);
        }

        return tally;
    }
}
