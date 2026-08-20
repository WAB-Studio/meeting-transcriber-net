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

    /// <summary>
    /// Every later packet is measured from the first instant the device vouched for, so opening on
    /// one it disowned must not become the anchor: a reading nothing stands behind would put the
    /// whole recording wherever that number happened to fall.
    /// </summary>
    [Fact]
    public void A_stream_that_opens_with_an_unvouched_instant_is_not_anchored_to_it()
    {
        var positions = new FramePositions(Rate);

        positions.For(At(999_999), timingIsSound: false, Packet).ShouldBe(0);
        positions.For(At(100), timingIsSound: true, Packet).ShouldBe(Packet);
        positions.For(At(110), timingIsSound: true, Packet).ShouldBe(Packet + (10 * Rate / 1000));
    }

    /// <summary>
    /// Asking where a packet goes moves the sequence past it, and never back. That is what makes it
    /// a question only about a packet the recording is keeping: a channel being moved has two
    /// streams handing blocks over at once, and one asked about a block it is going to drop would
    /// push everything after it along by that block's length — permanently, because a packet is
    /// never placed before the end of the one before it.
    /// </summary>
    [Fact]
    public void Asking_where_a_packet_goes_moves_the_sequence_past_it()
    {
        var positions = new FramePositions(Rate);
        positions.For(At(100), timingIsSound: true, Packet);

        // The block the other stream handed over and nobody kept.
        positions.For(At(100), timingIsSound: true, Packet).ShouldBe(Packet);

        // Ten milliseconds on, which the clock says is one packet in — and it is two, because the
        // dropped one was asked about. The push is what the code must not let happen, not what this
        // arithmetic should forgive: the clamp is the same one that keeps a real dropout a gap.
        positions.For(At(110), timingIsSound: true, Packet).ShouldBe(2 * Packet);
    }

    /// <summary>
    /// Two device threads are alive over one sequence for the moment a channel takes to hand over,
    /// so it is asked from both. What comes out has to be a sequence: every position distinct, none
    /// overlapping the one before it, and the four numbers behind them never half written.
    /// </summary>
    [Fact]
    public void Two_callers_at_once_still_get_one_sequence()
    {
        const int each = 2_000;
        var positions = new FramePositions(Rate);
        var placed = new System.Collections.Concurrent.ConcurrentBag<long>();

        Parallel.For(0, 2, thread =>
        {
            for (var packet = 0; packet < each; packet++)
            {
                placed.Add(positions.For(At(100 + (packet * 10)), timingIsSound: true, Packet));
            }
        });

        var order = placed.Order().ToArray();
        order.Length.ShouldBe(2 * each);
        order.Distinct().Count().ShouldBe(order.Length);

        for (var index = 1; index < order.Length; index++)
        {
            (order[index] - order[index - 1]).ShouldBeGreaterThanOrEqualTo(
                Packet, $"packet {index} lands over the one before it");
        }
    }

    [Fact]
    public void A_source_arriving_at_no_rate_at_all_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FramePositions(0));
    }

    private static MonotonicInstant At(double milliseconds) => MonotonicInstant.FromMilliseconds(milliseconds);
}
