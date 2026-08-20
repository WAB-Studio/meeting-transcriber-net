namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Which channel a recording follows onto another device without anybody being asked, and which
/// device is worth opening when it does.
/// </summary>
/// <remarks>
/// The rule the thread that follows reads, probed here because that thread cannot be run on a
/// machine with no devices. What it decides is the boundary between the recording keeping half of a
/// conversation and the recording taking on a file full of somebody's notifications, and the bound
/// between following once and following every two seconds for the rest of a meeting.
/// </remarks>
public class ReplacedDeviceTests
{
    private static readonly AudioDevice Jabra = new("{0.0.1.0}.jabra", "Jabra Evolve 65", false);
    private static readonly AudioDevice Realtek = new("{0.0.1.0}.realtek", "Realtek Array", true);

    /// <summary>
    /// ISC-78 and ISC-139. A microphone Windows takes away is followed at once, because Windows has
    /// already chosen what replaced it and the person is still speaking. Neither way of obtaining
    /// channel 0 ever is: what it would be moved onto is everything the machine plays, and nothing
    /// takes that on because a stream ended.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhatEndedAndWhetherItIsFollowed))]
    public void Only_a_microphone_that_went_away_is_followed(
        CaptureTarget listening, bool ended, bool followed)
    {
        ReplacedDevice.IsFollowed(listening, ended).ShouldBe(followed);
    }

    /// <summary>
    /// ISC-78. A device Windows keeps naming and that keeps dying is the ordinary shape of a
    /// failing hub, and without a bound the recording moves onto it every two seconds for the rest
    /// of the meeting: a line in the folder's changes and a stretch in the spool for each, none of
    /// it audio.
    /// </summary>
    [Fact]
    public void The_same_device_is_not_opened_again_after_a_move_that_brought_nothing()
    {
        ReplacedDevice.IsWorthTrying(Jabra, Jabra.Id, broughtNothing: true).ShouldBeFalse();
    }

    /// <summary>
    /// The other side of the same bound, and what keeps it from costing the meeting: what says a
    /// device is worth opening is that it recorded something, not that it has a different id. A
    /// driver that resets is the same endpoint and a different open.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhatIsWorthTrying))]
    public void A_device_is_worth_opening_unless_opening_it_already_brought_nothing(
        string? lastTried, bool broughtNothing, bool worth)
    {
        ReplacedDevice.IsWorthTrying(Jabra, lastTried, broughtNothing).ShouldBe(worth);
    }

    public static TheoryData<CaptureTarget, bool, bool> WhatEndedAndWhetherItIsFollowed() =>
        new()
        {
            { new CaptureTarget.Endpoint(Jabra), true, true },
            { new CaptureTarget.Endpoint(Jabra), false, false },
            { new CaptureTarget.Program(new AudioProcess(8124, "teams", StartedBy: 1084)), true, false },
            { new CaptureTarget.TheWholeMachine(), true, false },
        };

    public static TheoryData<string?, bool, bool> WhatIsWorthTrying() =>
        new()
        {
            // Nothing has been followed onto yet, which is every meeting until a device goes away.
            { null, false, true },

            // The same endpoint, after a move that recorded something: a driver that reset.
            { Jabra.Id, false, true },

            // Another endpoint, after a move that brought nothing: Windows has moved on too.
            { Realtek.Id, true, true },
        };
}
