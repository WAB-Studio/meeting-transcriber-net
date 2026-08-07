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
    /// <summary>Where the provider names the models that answered, keyed by their ids.</summary>
    private const string ModelInfo = "metadata.model_info";

    private const string Uuid = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

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

        var me = Alternative(channels[CapturedAudio.IndexOf(AudioChannel.Microphone)]);
        me.GetProperty("words").GetArrayLength().ShouldBe(0);
        me.GetProperty("transcript").GetString().ShouldBeEmpty();

        // The meeting is still there. An empty capture on both channels is a different case.
        Alternative(channels[CapturedAudio.IndexOf(AudioChannel.Loopback)])
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
            .ShouldBe([CapturedAudio.IndexOf(AudioChannel.Loopback), CapturedAudio.IndexOf(AudioChannel.Microphone)]);
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

    /// <summary>
    /// Walks the whole document, not the fields the tool substitutes. Those two lists used to be
    /// the same one, so anything the tool never reached — a passage under an option nobody has
    /// switched on yet, the provider's own summary and topic blocks, a field added next release —
    /// was also something nothing checked. Now a string at a path this test does not know about
    /// has to be made of vocabulary words like everything else, so the hole fails loudly instead
    /// of widening quietly.
    /// </summary>
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
        var intruders = Strings(response.RootElement, string.Empty)
            .Where(found => Structure(found.Path) is null)
            .SelectMany(found => Tokens(found.Text).Select(token => (found.Path, Token: token)))
            .Where(found => !vocabulary.Contains(found.Token))
            .Distinct()
            .Take(5)
            .Select(found => $"{found.Path}: {found.Token}")
            .ToArray();

        intruders.ShouldBeEmpty($"{name} holds words that were not substituted");
    }

    /// <summary>
    /// The other half of it: what this test does treat as structure has to still look like
    /// structure. Without it, "not speech" would be a list anybody could widen to make a failure
    /// go away, and a field holding a sentence could be waved through by adding its path.
    /// </summary>
    [Theory]
    [InlineData(DeepgramFixtures.TwoChannelLong)]
    [InlineData(DeepgramFixtures.TwoChannelShort)]
    [InlineData(DeepgramFixtures.SingleTrackDiarized)]
    [InlineData(DeepgramFixtures.TwoChannelSilentMe)]
    public void What_a_fixture_holds_that_is_not_speech_is_the_provider_describing_itself(string name)
    {
        using var response = DeepgramFixtures.Read(name);

        foreach (var (path, text) in Strings(response.RootElement, string.Empty))
        {
            if (Structure(path) is { } shape)
            {
                Regex.IsMatch(text, shape).ShouldBeTrue($"{name}: {path} holds '{text}'");
            }
        }
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

    /// <summary>
    /// Every string in a response, with the path it sits at — <c>results.utterances[].transcript</c>
    /// and so on. Arrays collapse to <c>[]</c> because the position says nothing, and so do the
    /// keys under <c>model_info</c>, which are the provider's model ids and make a different path
    /// per response rather than naming a field.
    /// </summary>
    private static IEnumerable<(string Path, string Text)> Strings(JsonElement node, string path)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.EnumerateObject())
                {
                    var name = path == ModelInfo ? "*" : property.Name;
                    var below = path.Length == 0 ? name : $"{path}.{name}";
                    foreach (var found in Strings(property.Value, below))
                    {
                        yield return found;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var found in Strings(item, path + "[]"))
                    {
                        yield return found;
                    }
                }

                break;

            case JsonValueKind.String:
                yield return (path, node.GetString()!);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// What a string at this path has to look like when it is the provider describing its own
    /// call rather than repeating what somebody said, or null when it is not one of those.
    /// </summary>
    /// <remarks>
    /// Short on purpose, and it is a shape and not a value: a path is only in here because the
    /// thing at it cannot be a sentence. Everything else in the document — including whatever the
    /// next release of the API adds — falls through to the vocabulary check.
    /// </remarks>
    private static string? Structure(string path) => path switch
    {
        "metadata.request_id" => "^0{8}-0{4}-0{4}-0{4}-0{12}$",
        "metadata.sha256" => "^0{64}$",
        "metadata.created" => @"^2020-01-01T00:00:00\.000Z$",

        // Deepgram's own constant. It says 'deprecated' and has for years.
        "metadata.transaction_key" => "^deprecated$",

        "metadata.models[]" or "results.utterances[].id" => Uuid,

        // The model that answered: a name, an architecture and a build, none of which have a
        // space in them, which is what stops this line from waving a sentence through.
        $"{ModelInfo}.*.name" or $"{ModelInfo}.*.arch" or $"{ModelInfo}.*.version"
            => "^[A-Za-z0-9._-]+$",

        _ => null,
    };

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
