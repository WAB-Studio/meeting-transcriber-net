namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// How a name somebody typed becomes one device out of the ones there are. Nothing here opens
/// anything: which endpoints exist is the machine's answer, and this is the rule applied to it.
/// </summary>
public class AudioDevicesTests
{
    private static readonly AudioDevice Fifine = new("{0.0.1.0}.{fifine}", "Micrófono (fifine Microphone)", true);
    private static readonly AudioDevice Webcam = new("{0.0.1.0}.{webcam}", "Micrófono (GENERAL WEBCAM)", false);

    [Fact]
    public void Naming_nothing_takes_the_one_Windows_calls_the_default()
    {
        AudioDevices.Choose([Webcam, Fifine], null).ShouldBe(Fifine);
    }

    [Fact]
    public void A_whole_name_finds_its_device()
    {
        AudioDevices.Choose([Fifine, Webcam], "Micrófono (GENERAL WEBCAM)").ShouldBe(Webcam);
    }

    [Fact]
    public void An_id_finds_its_device()
    {
        AudioDevices.Choose([Fifine, Webcam], Webcam.Id).ShouldBe(Webcam);
    }

    /// <summary>
    /// The names Windows builds are long and parenthesised, and nobody types one of them whole.
    /// </summary>
    [Fact]
    public void The_word_somebody_recognises_finds_its_device()
    {
        AudioDevices.Choose([Fifine, Webcam], "webcam").ShouldBe(Webcam);
    }

    /// <summary>
    /// An exact name wins over a part of another one, so a device cannot be made unreachable by
    /// another whose name contains its own.
    /// </summary>
    [Fact]
    public void A_name_that_is_also_part_of_another_still_finds_its_own()
    {
        var headset = new AudioDevice("{0.0.1.0}.{headset}", "Headset", false);
        var dock = new AudioDevice("{0.0.1.0}.{dock}", "Headset on the dock", false);

        AudioDevices.Choose([headset, dock], "Headset").ShouldBe(headset);
    }

    /// <summary>
    /// Two identical devices really do share a name, and picking one of them would be picking
    /// which microphone somebody records on.
    /// </summary>
    [Fact]
    public void A_word_that_names_two_devices_is_refused_with_both_named()
    {
        var first = new AudioDevice("{0.0.1.0}.{one}", "Micrófono (GENERAL WEBCAM)", false);
        var second = new AudioDevice("{0.0.1.0}.{two}", "Micrófono (GENERAL WEBCAM)", false);

        var refused = Should.Throw<AudioCaptureException>(() => AudioDevices.Choose([first, second], "webcam"));

        refused.Message.ShouldContain("2 microphones");
        refused.Message.ShouldContain("id");
    }

    [Fact]
    public void A_name_no_device_answers_to_says_what_there_is()
    {
        var refused = Should.Throw<AudioCaptureException>(() => AudioDevices.Choose([Fifine, Webcam], "blue yeti"));

        refused.Message.ShouldContain("blue yeti");
        refused.Message.ShouldContain("fifine");
    }

    [Fact]
    public void A_machine_with_no_microphone_says_so()
    {
        Should.Throw<AudioCaptureException>(() => AudioDevices.Choose([], null))
            .Message.ShouldContain("no active microphone");
    }

    /// <summary>
    /// A machine can have microphones and no default among them — every one of them disabled for
    /// applications, say. Answering with the first is answering with an arbitrary one.
    /// </summary>
    [Fact]
    public void No_default_among_them_asks_for_a_name_rather_than_guessing()
    {
        var refused = Should.Throw<AudioCaptureException>(() => AudioDevices.Choose([Webcam], null));

        refused.Message.ShouldContain("no default microphone");
        refused.Message.ShouldContain("WEBCAM");
    }
}
