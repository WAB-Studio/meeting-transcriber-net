using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a recording's own folder says about it, which is everything the blocks beside it cannot
/// know: which meeting it was, when, and what was on each channel.
/// </summary>
/// <remarks>
/// No device is opened. The card is written from values a capture would have had, and read back
/// with nothing else open — which is the situation it exists for, and the only one it is ever
/// read in.
/// </remarks>
public sealed class SpoolManifestTests : IDisposable
{
    private static readonly UtcTimestamp Started = UtcTimestamp.Parse("2026-08-15T09:41:07.250Z");

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public SpoolManifestTests() => folder.Create();

    /// <summary>ISC-119. The four things a folder full of blocks cannot answer for itself.</summary>
    [Fact]
    public void A_recording_says_which_meeting_it_is_when_it_started_and_under_what_profile()
    {
        var written = Card();

        SpoolManifest.Write(folder, written);

        var read = SpoolManifest.Find(folder).ShouldNotBeNull();
        read.MeetingId.ShouldBe(written.MeetingId);
        read.CaptureRunId.ShouldBe(written.CaptureRunId);
        read.StartedAt.ShouldBe(Started);
        read.Profile.ShouldBe(SourceProfile.Multichannel);
    }

    /// <summary>
    /// ISC-120. What fed which channel, in the channel order the contract fixes. Channel 1 is a
    /// device and names one; channel 0 is what this machine was playing, which is no device, so
    /// what it carries is the words a person reads and no id.
    /// </summary>
    [Fact]
    public void A_recording_says_what_fed_each_of_its_channels()
    {
        SpoolManifest.Write(folder, Card());

        var read = SpoolManifest.Find(folder).ShouldNotBeNull();

        read.Sources.Select(source => source.Channel)
            .ShouldBe([AudioChannel.Loopback, AudioChannel.Microphone]);
        read.On(AudioChannel.Loopback).Heard.ShouldBe("everything this machine plays");
        read.On(AudioChannel.Loopback).DeviceId.ShouldBeNull();
        read.On(AudioChannel.Microphone).Heard.ShouldBe("Jabra Evolve 65");
        read.On(AudioChannel.Microphone).DeviceId.ShouldBe("{0.0.1.00000000}.jabra");
        read.Mode.ShouldBe(CaptureMode.FullLoopback);
    }

    /// <summary>
    /// ISC-121. Which of the two channel 0 opened as is a field of the card's own, because neither
    /// of them names a device and there is nothing else left to tell them apart by.
    /// </summary>
    [Theory]
    [InlineData(CaptureMode.ProcessLoopback, "teams (pid 8124)")]
    [InlineData(CaptureMode.FullLoopback, "everything this machine plays")]
    public void Which_of_the_two_channel_zero_opened_as_is_the_cards_own_field(
        CaptureMode mode, string heard)
    {
        SpoolManifest.Write(folder, Card() with
        {
            Mode = mode,
            Sources =
            [
                new SpooledSource(AudioChannel.Loopback, heard, null),
                new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.00000000}.jabra"),
            ],
        });

        var read = SpoolManifest.Find(folder).ShouldNotBeNull();

        read.Mode.ShouldBe(mode);
        read.On(AudioChannel.Loopback).Heard.ShouldBe(heard);
        read.On(AudioChannel.Loopback).DeviceId.ShouldBeNull();
    }

    /// <summary>
    /// ISC-122. The card is the recording's own account of how it started, and there is no corpus
    /// to produce it again from — so a second write could only be something later overwriting it.
    /// </summary>
    [Fact]
    public void What_a_recording_says_about_itself_is_written_once()
    {
        var first = Card();
        SpoolManifest.Write(folder, first);

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Write(folder, Card()))
            .Message.ShouldContain("written once");

        SpoolManifest.Find(folder).ShouldNotBeNull().MeetingId.ShouldBe(first.MeetingId);
    }

    /// <summary>
    /// A recording that starts is a recording that goes on, so the folder it would write into is
    /// refused before a device is opened rather than by a card that will not be replaced.
    /// </summary>
    [Fact]
    public void A_folder_that_already_says_what_it_holds_takes_no_recording()
    {
        SpoolManifest.Write(folder, Card());

        Should.Throw<AudioCaptureException>(() => BlockSpool.EnsureNothingRecordedIn(folder))
            .Message.ShouldContain(SpoolManifest.FileName);
    }

    /// <summary>
    /// A folder with no card still holds a recording — every block declares its own format — so
    /// the absence is an answer and never a reason to pass over what is there.
    /// </summary>
    [Fact]
    public void A_recording_whose_card_never_landed_reads_as_one_without_a_card()
    {
        SpoolManifest.Find(folder).ShouldBeNull();
    }

    [Fact]
    public void A_card_that_does_not_read_as_json_is_refused_naming_the_file()
    {
        File.WriteAllText(SpoolManifest.In(folder).FullName, "{ this is not");

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Find(folder))
            .Message.ShouldContain(SpoolManifest.FileName);
    }

    /// <summary>
    /// Every field is required because the whole worth of this file is being trusted when there is
    /// nothing left to check it against. A card answering four of the five questions is one
    /// somebody would act on.
    /// </summary>
    [Theory]
    [InlineData("\"meeting\"", "meeting")]
    [InlineData("\"capture_run\"", "capture_run")]
    [InlineData("\"started_at\"", "started_at")]
    [InlineData("\"source_profile\"", "source_profile")]
    [InlineData("\"others_capture_mode\"", "others_capture_mode")]
    public void A_card_missing_something_that_identifies_the_recording_is_refused(string key, string says)
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(card.FullName, File.ReadAllText(card.FullName).Replace(key, "\"was\"", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card)).Message.ShouldContain(says);
    }

    /// <summary>
    /// The card's job is to say what fed each channel, so a list naming one of them twice has
    /// stopped being able to do it — and what comes back would be a recording with a channel whose
    /// device is the other one's, which is worse than one with none.
    /// </summary>
    [Fact]
    public void A_card_naming_one_of_its_channels_twice_is_refused()
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(
            card.FullName,
            File.ReadAllText(card.FullName).Replace("\"channel\": 1", "\"channel\": 0", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card))
            .Message.ShouldContain("twice");
    }

    /// <summary>
    /// Neither way of obtaining channel 0 is a device, so a card saying one fed it describes a
    /// recording this application cannot have made. Refused rather than read: read, its device
    /// would land on the run row beside a mode saying the recording was of something else, and the
    /// corpus checks the mode and not the pair.
    /// </summary>
    [Fact]
    public void A_card_saying_a_device_fed_channel_zero_is_refused()
    {
        SpoolManifest.Write(folder, Card() with
        {
            Sources =
            [
                new SpooledSource(
                    AudioChannel.Loopback, "Speakers (Realtek)", "{0.0.0.00000000}.speakers"),
                new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.00000000}.jabra"),
            ],
        });

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Find(folder))
            .Message.ShouldContain("{0.0.0.00000000}.speakers");
    }

    /// <summary>
    /// A source that says nothing about which channel it was is not channel 0 by default. Reading
    /// it as one would put the microphone's device on the loopback's line.
    /// </summary>
    [Fact]
    public void A_card_whose_source_names_no_channel_is_refused_rather_than_read_as_channel_0()
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(
            card.FullName,
            File.ReadAllText(card.FullName).Replace("\"channel\": 1", "\"was\": 1", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card))
            .Message.ShouldContain("channel");
    }

    /// <summary>
    /// However many sources it lists, it has to be the number the profile promises: a capture is
    /// two channels, and a card naming one of them describes audio this application never made.
    /// </summary>
    [Fact]
    public void A_card_naming_fewer_channels_than_its_profile_promises_is_refused()
    {
        SpoolManifest.Write(folder, Card() with
        {
            Sources = [new SpooledSource(AudioChannel.Loopback, "everything this machine plays", null)],
        });

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Find(folder))
            .Message.ShouldContain("sources");
    }

    [Fact]
    public void A_card_naming_no_source_at_all_is_refused()
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(
            card.FullName,
            File.ReadAllText(card.FullName).Replace("\"sources\"", "\"was\"", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card))
            .Message.ShouldContain("sources");
    }

    /// <summary>
    /// A mode this build does not know is not guessed at either. What it decides is whether the
    /// file holds one program or the whole machine, and a recording described as the wrong one of
    /// those is exactly what somebody would act on without checking.
    /// </summary>
    [Fact]
    public void A_card_naming_a_capture_mode_this_build_does_not_have_is_refused()
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(
            card.FullName,
            File.ReadAllText(card.FullName).Replace("full_loopback", "FullLoopback", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card))
            .Message.ShouldContain("others_capture_mode");
    }

    [Fact]
    public void A_card_naming_a_channel_this_build_does_not_have_is_refused()
    {
        SpoolManifest.Write(folder, Card());
        var card = SpoolManifest.In(folder);
        File.WriteAllText(
            card.FullName,
            File.ReadAllText(card.FullName).Replace("\"channel\": 1", "\"channel\": 7", StringComparison.Ordinal));

        Should.Throw<AudioCaptureException>(() => SpoolManifest.Read(card)).Message.ShouldContain("channel");
    }

    /// <summary>
    /// It is read by somebody looking at a folder with no application to hand, so the fields are
    /// the names this product uses everywhere else and the file is laid out to be read.
    /// </summary>
    [Fact]
    public void A_card_is_readable_by_a_person_holding_nothing_but_the_folder()
    {
        SpoolManifest.Write(folder, Card());

        var written = File.ReadAllText(SpoolManifest.In(folder).FullName);

        written.ShouldContain("\"meeting\"");
        written.ShouldContain("\"capture_run\"");
        written.ShouldContain("\"started_at\": \"2026-08-15T09:41:07.250Z\"");
        written.ShouldContain("\"source_profile\": \"multichannel\"");
        written.ShouldContain("\"others_capture_mode\": \"full_loopback\"");
        written.ShouldContain("\"channel\": 0");
        written.ShouldContain(Environment.NewLine);
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    private static SpoolCard Card() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Started,
        SourceProfile.Multichannel,
        CaptureMode.FullLoopback,
        [
            new SpooledSource(AudioChannel.Loopback, "everything this machine plays", null),
            new SpooledSource(AudioChannel.Microphone, "Jabra Evolve 65", "{0.0.1.00000000}.jabra"),
        ]);
}
