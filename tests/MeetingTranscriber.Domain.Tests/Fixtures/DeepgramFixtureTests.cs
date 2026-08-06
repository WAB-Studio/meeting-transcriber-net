using System.Text.Json;
using System.Text.RegularExpressions;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Domain.Tests.Fixtures;

/// <summary>
/// What the fixtures promise. Nothing here reads a renderer or a parser — those do not exist
/// yet — but a fixture whose claim nobody checks is one that quietly stops being true, and the
/// privacy claim in particular is not one to leave as a promise in a README.
/// </summary>
public partial class DeepgramFixtureTests
{
    [Fact]
    public void The_set_covers_every_case_it_was_built_for()
    {
        DeepgramFixtures.All.ShouldBe(
            [
                DeepgramFixtures.TwoChannelLong,
                DeepgramFixtures.TwoChannelShort,
                DeepgramFixtures.SingleTrackDiarized,
                DeepgramFixtures.TwoChannelSilentMe,
            ],
            ignoreOrder: true);

        foreach (var name in DeepgramFixtures.All)
        {
            File.Exists(Path.Combine(DeepgramFixtures.Directory.FullName, name + ".json"))
                .ShouldBeTrue($"{name} is missing");
        }
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void A_fixture_has_the_channels_its_profile_promises(string name)
    {
        using var response = DeepgramFixtures.Read(name);
        var profile = DeepgramFixtures.ProfileOf(name);
        var channels = response.RootElement.GetProperty("results").GetProperty("channels");

        Should.NotThrow(() => profile.EnsureChannelCount(channels.GetArrayLength()));
        response.RootElement.GetProperty("metadata").GetProperty("channels").GetInt32()
            .ShouldBe(profile.ChannelCount());
    }

    [Fact]
    public void The_long_fixture_is_a_long_meeting()
    {
        using var response = DeepgramFixtures.Read(DeepgramFixtures.TwoChannelLong);

        Seconds(response).ShouldBeGreaterThan(45 * 60);
    }

    [Fact]
    public void The_diarized_fixture_has_speakers_to_tell_apart()
    {
        using var response = DeepgramFixtures.Read(DeepgramFixtures.SingleTrackDiarized);

        // A single track says nothing about who is who, so the labels are all a renderer gets and
        // one of them is not a diarized meeting.
        Turns(response).Select(turn => turn.GetProperty("speaker").GetInt32()).Distinct()
            .Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void The_silent_fixture_has_a_channel_nobody_spoke_on()
    {
        using var response = DeepgramFixtures.Read(DeepgramFixtures.TwoChannelSilentMe);
        var channels = response.RootElement.GetProperty("results").GetProperty("channels");

        var me = Alternative(channels[CapturedAudio.IndexOf(AudioChannel.Me)]);
        me.GetProperty("words").GetArrayLength().ShouldBe(0);
        me.GetProperty("transcript").GetString().ShouldBeEmpty();

        // The meeting is still there. An empty capture on both channels is a different case.
        Alternative(channels[CapturedAudio.IndexOf(AudioChannel.Others)])
            .GetProperty("words").GetArrayLength().ShouldBeGreaterThan(0);

        Turns(response).ShouldAllBe(turn => turn.GetProperty("channel").GetInt32() == 0);
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    public void A_two_channel_fixture_answers_on_both_of_the_numbers_the_contract_fixes(string name)
    {
        using var response = DeepgramFixtures.Read(name);

        Turns(response).Select(turn => turn.GetProperty("channel").GetInt32()).Distinct().Order()
            .ShouldBe([CapturedAudio.IndexOf(AudioChannel.Others), CapturedAudio.IndexOf(AudioChannel.Me)]);
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void Turns_run_forwards_and_stay_inside_the_meeting(string name)
    {
        using var response = DeepgramFixtures.Read(name);
        var seconds = Seconds(response);
        var previous = 0.0;

        foreach (var turn in Turns(response))
        {
            var start = turn.GetProperty("start").GetDouble();
            var end = turn.GetProperty("end").GetDouble();

            start.ShouldBeGreaterThanOrEqualTo(previous);
            end.ShouldBeGreaterThanOrEqualTo(start);
            end.ShouldBeLessThanOrEqualTo(seconds);
            previous = start;
        }
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void Nothing_anybody_said_is_still_in_a_fixture(string name)
    {
        var vocabulary = DeepgramFixtures.Vocabulary();
        using var response = DeepgramFixtures.Read(name);

        // Closed by construction: every word was replaced by one of these. Checking the list is
        // complete rather than checking real names are absent is what keeps this test from being
        // a place the names live on.
        var intruders = Spoken(response.RootElement)
            .SelectMany(Tokens)
            .Where(token => !vocabulary.Contains(token))
            .Distinct()
            .Take(5)
            .ToArray();

        intruders.ShouldBeEmpty($"{name} holds words that were not substituted");
    }

    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void No_fixture_carries_the_call_that_was_paid_for(string name)
    {
        using var response = DeepgramFixtures.Read(name);
        var metadata = response.RootElement.GetProperty("metadata");

        metadata.GetProperty("request_id").GetString().ShouldBe("00000000-0000-0000-0000-000000000000");
        metadata.GetProperty("sha256").GetString().ShouldBe(new string('0', 64));
        metadata.GetProperty("created").GetString().ShouldBe("2020-01-01T00:00:00.000Z");
    }

    [GeneratedRegex(@"^(Channel \d+: )?(Speaker \d+: )?")]
    private static partial Regex SpeakerMarker { get; }

    private static double Seconds(JsonDocument response) =>
        response.RootElement.GetProperty("metadata").GetProperty("duration").GetDouble();

    private static JsonElement Alternative(JsonElement channel) => channel.GetProperty("alternatives")[0];

    private static IEnumerable<JsonElement> Turns(JsonDocument response) =>
        response.RootElement.GetProperty("results").GetProperty("utterances").EnumerateArray();

    /// <summary>Every string in a response that holds something a person said.</summary>
    private static IEnumerable<string> Spoken(JsonElement root)
    {
        var results = root.GetProperty("results");

        foreach (var channel in results.GetProperty("channels").EnumerateArray())
        {
            var alternative = Alternative(channel);
            yield return alternative.GetProperty("transcript").GetString()!;
            foreach (var text in Words(alternative))
            {
                yield return text;
            }

            foreach (var text in Paragraphs(alternative))
            {
                yield return text;
            }
        }

        foreach (var turn in results.GetProperty("utterances").EnumerateArray())
        {
            yield return turn.GetProperty("transcript").GetString()!;
            foreach (var text in Words(turn))
            {
                yield return text;
            }
        }

        foreach (var text in Paragraphs(results))
        {
            yield return text;
        }
    }

    private static IEnumerable<string> Words(JsonElement holder)
    {
        foreach (var word in holder.GetProperty("words").EnumerateArray())
        {
            yield return word.GetProperty("word").GetString()!;
            yield return word.GetProperty("punctuated_word").GetString()!;
        }
    }

    private static IEnumerable<string> Paragraphs(JsonElement holder)
    {
        if (!holder.TryGetProperty("paragraphs", out var paragraphs))
        {
            yield break;
        }

        yield return paragraphs.GetProperty("transcript").GetString()!;
        foreach (var paragraph in paragraphs.GetProperty("paragraphs").EnumerateArray())
        {
            foreach (var sentence in paragraph.GetProperty("sentences").EnumerateArray())
            {
                yield return sentence.GetProperty("text").GetString()!;
            }
        }
    }

    /// <summary>
    /// The words of a passage, stripped of the punctuation around them and of the
    /// <c>Channel 0: Speaker 1:</c> a paragraph transcript opens a line with, which is structure
    /// and stays as the provider wrote it.
    /// </summary>
    private static IEnumerable<string> Tokens(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var spoken = line[SpeakerMarker.Match(line).Length..];
            foreach (var token in spoken.Split(' '))
            {
                var core = token.Trim().Trim(token.Where(c => !char.IsLetterOrDigit(c)).ToArray());
                if (core.Length > 0)
                {
                    yield return core;
                }
            }
        }
    }
}
