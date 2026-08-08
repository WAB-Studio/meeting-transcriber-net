using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Fixtures;

/// <summary>
/// The committed responses as the domain sees them.
/// </summary>
/// <remarks>
/// Which responses exist is <see cref="DeepgramFixtures"/>; reading one is here, and deliberately
/// not shared with the parser's tests. The domain cannot reference the parser, and a rule proved
/// against the parser's own output would only be proving the two agree.
/// </remarks>
public static class FixtureResponses
{
    /// <summary>
    /// What the provider returned, as the domain sees it. Enough to drive a rule and no more:
    /// the parser that has to survive a truncated file, a missing field or a channel count that
    /// disagrees with the profile is its own piece of work, and this is not a draft of it.
    /// </summary>
    public static IReadOnlyList<SpeechSegment> Segments(string name)
    {
        using var response = Read(name);
        var multichannel = DeepgramFixtures.ProfileOf(name) is SourceProfile.Multichannel;

        return response.RootElement.GetProperty("results").GetProperty("utterances").EnumerateArray()
            .Select(turn =>
            {
                var channel = multichannel
                    ? CapturedAudio.ChannelAt(turn.GetProperty("channel").GetInt32())
                    : (AudioChannel?)null;

                // The label rule itself is not a detail of this reader: it is the key
                // speaker_assignments hangs off, so a shortcut here would be testing the rules
                // against labels the corpus never sees.
                return new SpeechSegment(
                    Duration.FromSeconds(turn.GetProperty("start").GetDouble()),
                    Duration.FromSeconds(turn.GetProperty("end").GetDouble()),
                    channel,
                    SpeakerLabels.For(channel, turn.GetProperty("speaker").GetInt32()),
                    turn.GetProperty("transcript").GetString()!);
            })
            .ToArray();
    }

    public static JsonDocument Read(string name)
    {
        using var stream = File.OpenRead(DeepgramFixtures.PathOf(name));
        return JsonDocument.Parse(stream);
    }
}
