using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// Reads a <c>deepgram.json</c> into the speech the domain knows how to group.
/// </summary>
/// <remarks>
/// <para>
/// It only reads. The response is a paid artifact and is never rewritten, corrected or normalised
/// in place, so every correction the system makes later lands on something derived from this and
/// the file stays comparable to what was billed.
/// </para>
/// <para>
/// It decides nothing either. A speaker number becomes a label by the rule in
/// <see cref="SpeakerLabels"/>, a channel number becomes a position through
/// <see cref="CapturedAudio"/>, and what counts as a turn is <see cref="Turns.Group"/>'s. What is
/// left here is the reading itself and the refusals: a file that is cut short, one that is missing
/// something the parser needs, one that says two things about itself that disagree, and one whose
/// channel count is not the count its profile promises.
/// </para>
/// </remarks>
public static class DeepgramTranscriptParser
{
    /// <summary>Reads the response at <paramref name="path"/>, without opening it for writing.</summary>
    public static DeepgramTranscript ParseFile(string path, SourceProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var response = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(response, profile);
    }

    public static DeepgramTranscript Parse(Stream response, SourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = Read(() => JsonDocument.Parse(response));
        return Parse(document.RootElement, profile);
    }

    public static DeepgramTranscript Parse(string response, SourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(response);

        using var document = Read(() => JsonDocument.Parse(response));
        return Parse(document.RootElement, profile);
    }

    private static DeepgramTranscript Parse(JsonElement root, SourceProfile profile)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw Malformed($"the whole response is {root.ValueKind} and has to be an object.");
        }

        var metadata = Property(root, "metadata", JsonValueKind.Object);
        var results = Property(root, "results", JsonValueKind.Object);

        // Checked against what the provider says it transcribed, not against what we believe we
        // sent: a response for two channels read as a single track would put both sides of a call
        // on one nameless speaker, and nothing downstream would notice.
        var channelCount = Integer(metadata, "channels");
        profile.EnsureChannelCount(channelCount);

        var transcripts = TranscriptPerChannel(results, channelCount);
        var speakers = new SortedSet<int>[channelCount];
        for (var index = 0; index < channelCount; index++)
        {
            speakers[index] = new SortedSet<int>();
        }

        var segments = new List<SpeechSegment>();
        foreach (var utterance in Property(results, "utterances", JsonValueKind.Array).EnumerateArray())
        {
            var index = Integer(utterance, "channel");
            if (index < 0 || index >= channelCount)
            {
                throw Malformed(
                    $"an utterance is on channel {index} and the response has {channelCount}.");
            }

            var start = Seconds(utterance, "start");
            var end = Seconds(utterance, "end");
            if (end < start)
            {
                throw Malformed($"an utterance starts at {start} and ends at {end}.");
            }

            var speaker = Integer(utterance, "speaker");
            var text = Text(utterance, "transcript");

            // Saying nothing is not being heard. It matters here and not only when grouping,
            // because a channel whose every utterance is blank is a channel nobody spoke on.
            if (!string.IsNullOrWhiteSpace(text))
            {
                speakers[index].Add(speaker);
            }

            var channel = ChannelAt(index, channelCount);
            segments.Add(new SpeechSegment(
                start,
                end,
                channel,
                SpeakerLabels.For(channel, speaker),
                text,
                Confidence(utterance)));
        }

        var channels = new TranscribedChannel[channelCount];
        for (var index = 0; index < channelCount; index++)
        {
            // The response says twice whether a channel carried anything — once as that channel's
            // whole transcript and once as the utterances on it — and the two halves disagreeing
            // means one of them is being read wrong.
            var silent = string.IsNullOrWhiteSpace(transcripts[index]);
            if (silent != (speakers[index].Count == 0))
            {
                throw Malformed(silent
                    ? $"channel {index} has an empty transcript and utterances on it."
                    : $"channel {index} has a transcript and no utterances on it.");
            }

            var channel = ChannelAt(index, channelCount);
            channels[index] = new TranscribedChannel(
                index,
                channel,
                speakers[index].Select(speaker => SpeakerLabels.For(channel, speaker)).ToArray());
        }

        return new DeepgramTranscript(profile, Seconds(metadata, "duration"), channels, segments);
    }

    /// <summary>
    /// The position a channel number names, or none at all: a single track has one channel because
    /// it is one recording, and neither side of <see cref="AudioChannel"/> is true of it.
    /// </summary>
    private static AudioChannel? ChannelAt(int index, int channelCount) =>
        channelCount > 1 ? CapturedAudio.ChannelAt(index) : null;

    /// <summary>What each channel came back as, whole, in channel order.</summary>
    private static string[] TranscriptPerChannel(JsonElement results, int channelCount)
    {
        var channels = Property(results, "channels", JsonValueKind.Array);
        if (channels.GetArrayLength() != channelCount)
        {
            throw Malformed(
                $"it claims {channelCount} channel(s) and carries {channels.GetArrayLength()}.");
        }

        return channels.EnumerateArray()
            .Select(channel =>
            {
                var alternatives = Property(channel, "alternatives", JsonValueKind.Array);
                return alternatives.GetArrayLength() > 0
                    ? Text(alternatives[0], "transcript")
                    : throw Malformed("a channel came back with no alternatives.");
            })
            .ToArray();
    }

    private static JsonDocument Read(Func<JsonDocument> parse)
    {
        try
        {
            return parse();
        }
        catch (JsonException broken)
        {
            throw Malformed("the JSON is not valid, and a response that stops early looks like this.", broken);
        }
    }

    private static JsonElement Property(JsonElement owner, string name, JsonValueKind kind)
    {
        if (!owner.TryGetProperty(name, out var property))
        {
            throw Malformed($"there is no '{name}' in it.");
        }

        if (property.ValueKind != kind)
        {
            throw Malformed($"'{name}' is {property.ValueKind} and has to be {kind}.");
        }

        return property;
    }

    private static int Integer(JsonElement owner, string name)
    {
        var property = Property(owner, name, JsonValueKind.Number);
        return property.TryGetInt32(out var value)
            ? value
            : throw Malformed($"'{name}' is {property} and has to be a whole number.");
    }

    private static Duration Seconds(JsonElement owner, string name)
    {
        var property = Property(owner, name, JsonValueKind.Number);
        return property.TryGetDouble(out var seconds) && seconds >= 0
            ? Duration.FromSeconds(seconds)
            : throw Malformed($"'{name}' is {property} and has to be a length in seconds.");
    }

    private static string Text(JsonElement owner, string name) =>
        Property(owner, name, JsonValueKind.String).GetString()!;

    /// <summary>
    /// Optional on purpose: it is the provider's own number about one stretch of speech, worth
    /// carrying because it was paid for, and not worth refusing a whole meeting over.
    /// </summary>
    /// <remarks>
    /// Unbounded here and bounded where it lands: <c>ck_utterances_confidence</c> holds the column
    /// to zero through one, so a number outside that is refused by the corpus as the turns are
    /// saved rather than by the parser as the response is read. That is the only number on this
    /// path the two halves disagree about, and it is left alone deliberately — no provider sends
    /// one, and a check here would be a refusal written for input nothing produces.
    /// </remarks>
    private static double? Confidence(JsonElement utterance) =>
        utterance.TryGetProperty("confidence", out var confidence)
        && confidence.ValueKind is JsonValueKind.Number
        && confidence.TryGetDouble(out var value)
            ? value
            : null;

    private static DeepgramResponseException Malformed(string what) =>
        new($"This is not a Deepgram response the parser can read: {what}");

    private static DeepgramResponseException Malformed(string what, Exception cause) =>
        new($"This is not a Deepgram response the parser can read: {what}", cause);
}
