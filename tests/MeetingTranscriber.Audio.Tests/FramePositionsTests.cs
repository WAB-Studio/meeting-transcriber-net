namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Where a packet goes when its device numbers no frames, which is what the virtual device behind
/// a program's audio does. The instants are the device's; what is being tested is the arithmetic
/// that turns them into the positions everything downstream lays a recording out by.
/// </summary>
public class FramePositionsTests
{
    private const int Rate = 48_000;
    private const int Packet = 480;

    [Fact]
    public void The_first_packet_starts_the_recording_at_nothing()
    {
        var positions = new FramePositions(Rate);

        positions.For(At(1_234.5), timingIsSound: true, Packet).ShouldBe(0);
    }

    [Fact]
    public void A_packet_lands_where_the_instant_the_device_read_it_says()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);

        positions.For(At(150), timingIsSound: true, Packet).ShouldBe(50 * Rate / 1000);
    }

    /// <summary>
    /// Packets ten milliseconds long arriving instants that jitter by less than that would overlap
    /// if the instant were taken literally, and an overlap is a recording that cannot be laid out.
    /// </summary>
    [Fact]
    public void Jitter_never_puts_a_packet_back_over_the_one_before_it()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);

        positions.For(At(104), timingIsSound: true, Packet).ShouldBe(Packet);
    }

    /// <summary>
    /// The whole reason the instant is read at all. Bytes that arrived would say these two packets
    /// are next to each other; the device says a second of the meeting passed between them, and a
    /// second of silence is what the recording has to keep.
    /// </summary>
    [Fact]
    public void A_stretch_the_device_never_delivered_stays_the_gap_it_was()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);
        positions.For(At(110), timingIsSound: true, Packet);

        positions.For(At(1_110), timingIsSound: true, Packet).ShouldBe(1_010 * Rate / 1000);
    }

    /// <summary>
    /// An instant the device would not vouch for is no instant at all, and the samples beside it
    /// are still the meeting — so the packet goes on the end rather than being dropped or placed
    /// by a number nothing stands behind.
    /// </summary>
    [Fact]
    public void A_packet_whose_instant_the_device_will_not_vouch_for_goes_straight_after_the_last()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);

        positions.For(At(999_999), timingIsSound: false, Packet).ShouldBe(Packet);
    }

    /// <summary>
    /// And the one after it is placed by its own instant again, measured from where the recording
    /// opened — so one unvouched packet costs one placement and not the rest of the meeting.
    /// </summary>
    [Fact]
    public void The_packet_after_an_unvouched_one_is_placed_by_its_instant_again()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);
        positions.For(At(999_999), timingIsSound: false, Packet);

        positions.For(At(300), timingIsSound: true, Packet).ShouldBe(200 * Rate / 1000);
    }

    [Fact]
    public void A_source_arriving_at_no_rate_at_all_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FramePositions(0));
    }

    private static MonotonicInstant At(double milliseconds) => MonotonicInstant.FromMilliseconds(milliseconds);
}
