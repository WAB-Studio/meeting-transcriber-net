using System.Security.Cryptography;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// The parser against real responses, which is where the cases nobody would think to write by hand
/// are. Offline: it reads the committed fixtures and nothing else.
/// </summary>
public class FixtureParsingTests
{
    public static TheoryData<string> Fixtures => new(DeepgramFixtures.All);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void A_real_response_becomes_speech_the_domain_can_group(string name)
    {
        var turns = Turns.Group(Parse(name).Segments);

        turns.ShouldNotBeEmpty();
        turns.Select(turn => turn.Ordinal).ShouldBe(Enumerable.Range(0, turns.Count));
        turns.Select(turn => turn.Start).ShouldBeInOrder();
        turns.ShouldAllBe(turn => turn.End >= turn.Start);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_speaker_of_a_meeting_gets_a_label_of_their_own(string name)
    {
        var transcript = Parse(name);

        var labels = transcript.Channels.SelectMany(channel => channel.SpeakerLabels).ToArray();
        labels.ShouldBeUnique();
        transcript.Segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => segment.SpeakerLabel)
            .Distinct()
            .ShouldBe(labels, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void What_the_response_says_about_its_own_length_is_read(string name)
    {
        var transcript = Parse(name);

        transcript.Audio.ShouldBeGreaterThanOrEqualTo(transcript.Segments.Max(segment => segment.End));
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void A_captured_meeting_comes_back_as_the_two_channels_it_was_sent_as(string name)
    {
        var transcript = Parse(name);

        transcript.Channels.Select(channel => channel.Channel)
            .ShouldBe([AudioChannel.Loopback, AudioChannel.Microphone]);
        transcript.Channels.Select(channel => channel.Index)
            .ShouldBe([CapturedAudio.IndexOf(AudioChannel.Loopback), CapturedAudio.IndexOf(AudioChannel.Microphone)]);
    }

    /// <summary>
    /// A channel that came back with nothing was transcribed and charged for. The meeting still
    /// projects from the channel that did carry speech, and the empty one is a fact about the
    /// recording rather than a failure to read it.
    /// </summary>
    [Fact]
    public void A_microphone_that_caught_nothing_is_a_silent_channel_and_not_a_missing_one()
    {
        var transcript = Parse(DeepgramFixtures.TwoChannelSilentMe);

        transcript.Channels.Count.ShouldBe(2);
        transcript.SilentChannels.Select(channel => channel.Channel).ShouldBe([AudioChannel.Microphone]);
        transcript.Segments.ShouldNotBeEmpty();
        transcript.Segments.ShouldAllBe(segment => segment.Channel == AudioChannel.Loopback);
    }

    /// <summary>
    /// The contract used to say channel 1 was the user, and this is the response that disproves it:
    /// two people shared the microphone and the provider told them apart. Reading only channel 0's
    /// labels would have folded them into one speaker.
    /// </summary>
    [Fact]
    public void Two_people_sharing_the_microphone_come_back_as_two_speakers()
    {
        var microphone = Parse(DeepgramFixtures.TwoChannelLong)
            .Channels[CapturedAudio.IndexOf(AudioChannel.Microphone)];

        microphone.SpeakerLabels.ShouldBe(["ch1:speaker_0", "ch1:speaker_1"]);
    }

    [Fact]
    public void A_single_track_keeps_its_speakers_apart_without_a_channel_to_help()
    {
        var transcript = Parse(DeepgramFixtures.SingleTrackDiarized);

        transcript.Channels.ShouldHaveSingleItem().Channel.ShouldBeNull();
        transcript.Segments.ShouldAllBe(segment => segment.Channel == null);
        transcript.Channels[0].SpeakerLabels.ShouldBe(
            ["speaker_0", "speaker_1", "speaker_2", "speaker_3", "speaker_4", "speaker_5"]);
    }

    /// <summary>
    /// Projecting the same response twice has to put the same turns back at the same numbers,
    /// which starts here: reading it twice has to produce the same speech.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Reading_the_same_response_twice_reads_the_same_thing(string name)
    {
        Parse(name).Segments.ShouldBe(Parse(name).Segments);
    }

    /// <summary>
    /// <c>deepgram.json</c> is a paid artifact and the parser is the piece most often pointed at
    /// one. A file nothing can write to is the check that it stayed a reader.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Reading_a_response_does_not_touch_the_file(string name)
    {
        var copy = new FileInfo(Path.Combine(
            Path.GetTempPath(), $"{nameof(FixtureParsingTests)}-{Guid.NewGuid():N}.json"));
        try
        {
            File.Copy(DeepgramFixtures.PathOf(name), copy.FullName);
            copy.IsReadOnly = true;
            var before = Sha256(copy.FullName);

            DeepgramTranscriptParser.ParseFile(copy.FullName, DeepgramFixtures.ProfileOf(name))
                .Segments.ShouldNotBeEmpty();

            Sha256(copy.FullName).ShouldBe(before);
        }
        finally
        {
            copy.Refresh();
            if (copy.Exists)
            {
                copy.IsReadOnly = false;
                copy.Delete();
            }
        }
    }

    private static DeepgramTranscript Parse(string name) =>
        DeepgramTranscriptParser.ParseFile(DeepgramFixtures.PathOf(name), DeepgramFixtures.ProfileOf(name));

    private static string Sha256(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
