using System.Text;
using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// The cases the committed responses do not have, because a response Deepgram actually answered is
/// well formed by construction. What is proved here is what happens when one is not.
/// </summary>
public class DeepgramTranscriptParserTests
{
    [Fact]
    public void A_response_that_stops_early_is_refused()
    {
        var whole = Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 1, 2, "Lento.")]);

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(whole[..(whole.Length / 2)], SourceProfile.Multichannel));
    }

    [Fact]
    public void Something_that_is_not_a_response_at_all_is_refused()
    {
        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse("[1, 2, 3]", SourceProfile.Multichannel));
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("results")]
    public void A_response_missing_a_half_names_the_half(string missing)
    {
        var response = Without(
            Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 1, 2, "Lento.")]),
            missing);

        Should.Throw<DeepgramResponseException>(
                () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel))
            .Message.ShouldContain(missing);
    }

    /// <summary>
    /// A response transcribed without <c>utterances</c> has the words and not the stretches of
    /// speech, so there is nothing to group. Falling back to the whole transcript would produce
    /// one turn for a meeting and citations that point at all of it.
    /// </summary>
    [Fact]
    public void A_response_with_no_utterances_in_it_is_refused()
    {
        var response = JsonSerializer.Serialize(new
        {
            metadata = new { channels = 2, duration = 12.5 },
            results = new { channels = new[] { Channel("Lento."), Channel("Lento.") } },
        });

        Should.Throw<DeepgramResponseException>(
                () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel))
            .Message.ShouldContain("utterances");
    }

    [Fact]
    public void A_speaker_the_response_never_numbered_is_refused()
    {
        var response = Without(
            Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 1, 2, "Lento.")]),
            "speaker");

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel));
    }

    /// <summary>
    /// The audio contract, and it stays the audio contract's exception: two channels read as a
    /// single track would put both sides of a call on one nameless speaker.
    /// </summary>
    [Fact]
    public void A_response_whose_channel_count_is_not_the_profiles_is_refused()
    {
        var response = Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 1, 2, "Lento.")]);

        Should.Throw<AudioContractException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Diarize));
    }

    [Fact]
    public void A_response_carrying_fewer_channels_than_it_claims_is_refused()
    {
        var response = JsonSerializer.Serialize(new
        {
            metadata = new { channels = 2, duration = 12.5 },
            results = new
            {
                channels = new[] { Channel("Lento.") },
                utterances = new[] { Utterance(0, 0, 1, 2, "Lento.") },
            },
        });

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel));
    }

    [Fact]
    public void An_utterance_on_a_channel_that_was_not_sent_is_refused()
    {
        var response = Response(2, ["Lento.", "Lento."], [Utterance(4, 0, 1, 2, "Lento.")]);

        Should.Throw<DeepgramResponseException>(
                () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel))
            .Message.ShouldContain("4");
    }

    [Fact]
    public void An_utterance_that_ends_before_it_starts_is_refused()
    {
        var response = Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 9, 2, "Lento."), Utterance(1, 0, 1, 2, "Lento.")]);

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel));
    }

    /// <summary>
    /// A response says twice whether a channel carried anything, and the two halves are read by
    /// different parts of the system. Letting them disagree silently is how a channel that was
    /// paid for turns into a channel nobody notices is missing.
    /// </summary>
    [Fact]
    public void A_channel_with_utterances_and_an_empty_transcript_is_refused()
    {
        var response = Response(2, ["Lento.", ""], [Utterance(0, 0, 1, 2, "Lento."), Utterance(1, 0, 3, 4, "Lento.")]);

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel));
    }

    [Fact]
    public void A_channel_with_a_transcript_and_no_utterances_is_refused()
    {
        var response = Response(2, ["Lento.", "Lento."], [Utterance(0, 0, 1, 2, "Lento.")]);

        Should.Throw<DeepgramResponseException>(
            () => DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel));
    }

    /// <summary>
    /// The case the contract used to deny by calling channel 1 "the user": two people in the same
    /// room share one microphone, and the provider tells them apart. Folding them into one speaker
    /// is attributing to somebody what the person next to them said.
    /// </summary>
    [Fact]
    public void Two_people_on_the_microphone_are_two_speakers()
    {
        var response = Response(
            2,
            ["Lento.", "Lento. Lento."],
            [Utterance(0, 0, 1, 2, "Lento."), Utterance(1, 0, 3, 4, "Lento."), Utterance(1, 1, 5, 6, "Lento.")]);

        var transcript = DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel);

        transcript.Channels[CapturedAudio.IndexOf(AudioChannel.Microphone)].SpeakerLabels
            .ShouldBe(["ch1:speaker_0", "ch1:speaker_1"]);
    }

    /// <summary>
    /// A provider numbers speakers within a channel, so both sides of a call start at zero. The
    /// label is the key <c>speaker_assignments</c> hangs off, and two speakers sharing one would
    /// put a person's name on somebody else's words.
    /// </summary>
    [Fact]
    public void The_same_speaker_number_on_two_channels_is_two_labels()
    {
        var response = Response(
            2,
            ["Lento.", "Lento."],
            [Utterance(0, 0, 1, 2, "Lento."), Utterance(1, 0, 3, 4, "Lento.")]);

        var transcript = DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel);

        transcript.Segments.Select(segment => segment.SpeakerLabel).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void A_channel_nobody_spoke_on_is_read_and_not_lost()
    {
        var response = Response(2, ["Lento.", ""], [Utterance(0, 0, 1, 2, "Lento.")]);

        var transcript = DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel);

        transcript.Channels.Count.ShouldBe(2);
        transcript.SilentChannels.Select(channel => channel.Channel).ShouldBe([AudioChannel.Microphone]);
        transcript.Segments.ShouldAllBe(segment => segment.Channel == AudioChannel.Loopback);
    }

    [Fact]
    public void A_single_track_has_no_side_to_call_a_speaker_by()
    {
        var response = Response(1, ["Lento. Lento."], [Utterance(0, 0, 1, 2, "Lento."), Utterance(0, 1, 3, 4, "Lento.")]);

        var transcript = DeepgramTranscriptParser.Parse(response, SourceProfile.Diarize);

        transcript.Channels.ShouldHaveSingleItem().Channel.ShouldBeNull();
        transcript.Segments.ShouldAllBe(segment => segment.Channel == null);
        transcript.Channels[0].SpeakerLabels.ShouldBe(["speaker_0", "speaker_1"]);
    }

    [Fact]
    public void What_the_provider_measured_survives_the_reading()
    {
        var response = Response(1, ["Lento."], [Utterance(0, 3, 1.2345, 2.5, "Lento.")]);

        var segment = DeepgramTranscriptParser.Parse(response, SourceProfile.Diarize).Segments.ShouldHaveSingleItem();

        segment.Start.ShouldBe(Duration.FromMilliseconds(1_235));
        segment.End.ShouldBe(Duration.FromMilliseconds(2_500));
        segment.SpeakerLabel.ShouldBe("speaker_3");
        segment.Text.ShouldBe("Lento.");
        segment.Confidence.ShouldBe(0.75);
    }

    [Fact]
    public void A_response_without_confidences_is_read_all_the_same()
    {
        var response = JsonSerializer.Serialize(new
        {
            metadata = new { channels = 1, duration = 12.5 },
            results = new
            {
                channels = new[] { Channel("Lento.") },
                utterances = new[] { new { channel = 0, speaker = 0, start = 1.0, end = 2.0, transcript = "Lento." } },
            },
        });

        DeepgramTranscriptParser.Parse(response, SourceProfile.Diarize)
            .Segments.ShouldHaveSingleItem()
            .Confidence.ShouldBeNull();
    }

    /// <summary>
    /// Grouping is the domain's and stays there. What this proves is that the parser hands over
    /// something it can group: the two sides interleave into one conversation.
    /// </summary>
    [Fact]
    public void What_is_read_is_what_the_domain_groups()
    {
        var response = Response(
            2,
            ["Lento. Lento.", "Lento."],
            [Utterance(0, 0, 1, 2, "Lento."), Utterance(0, 0, 30, 31, "Lento."), Utterance(1, 0, 10, 11, "Lento.")]);

        var turns = Turns.Group(DeepgramTranscriptParser.Parse(response, SourceProfile.Multichannel).Segments);

        turns.Select(turn => turn.Channel)
            .ShouldBe([AudioChannel.Loopback, AudioChannel.Microphone, AudioChannel.Loopback]);
    }

    private static string Response(int channels, string[] transcripts, object[] utterances) =>
        JsonSerializer.Serialize(new
        {
            metadata = new { channels, duration = 12.5 },
            results = new
            {
                channels = transcripts.Select(Channel).ToArray(),
                utterances,
            },
        });

    private static object Channel(string transcript) =>
        new { alternatives = new[] { new { transcript, confidence = 0.75 } } };

    private static object Utterance(int channel, int speaker, double start, double end, string transcript) =>
        new { channel, speaker, start, end, transcript, confidence = 0.75 };

    /// <summary>The same response with one property taken out, wherever it appears.</summary>
    private static string Without(string response, string property)
    {
        using var document = JsonDocument.Parse(response);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Copy(document.RootElement, writer, property);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Copy(JsonElement element, Utf8JsonWriter writer, string dropped)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().Where(p => p.Name != dropped))
                {
                    writer.WritePropertyName(property.Name);
                    Copy(property.Value, writer, dropped);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Copy(item, writer, dropped);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}
