using System.Runtime.CompilerServices;
using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Tests.Fixtures;

/// <summary>
/// The committed Deepgram responses, and what each one is there to cover.
/// </summary>
/// <remarks>
/// They are read from the repository rather than copied into the test output: they are megabytes
/// of paid response, they change only when the tool that builds them is run, and a copy per
/// configuration buys nothing but a slower build. See tests/fixtures/deepgram/README.md.
/// </remarks>
public static class DeepgramFixtures
{
    /// <summary>Both channels, 58 minutes, and two people sharing the microphone.</summary>
    public const string TwoChannelLong = "two-channel-long";

    /// <summary>Both channels, 34 minutes. The one to reach for when length is not the point.</summary>
    public const string TwoChannelShort = "two-channel-short";

    /// <summary>
    /// Both channels, 13 minutes, and one voice on the microphone against three on the meeting.
    /// The only shape in which the system settles a speaker without asking, so it is the only one
    /// where <see cref="Speakers.Resolve"/> naming the wrong label would sign somebody else's
    /// words — and the four fixtures before it left that branch with no real response behind it.
    /// </summary>
    public const string TwoChannelOneVoiceMe = "two-channel-one-voice-me";

    /// <summary>One track with six diarized speakers, the shape a <see cref="SourceProfile.Diarize"/> meeting has.</summary>
    public const string SingleTrackDiarized = "single-track-diarized";

    /// <summary>Both channels, and the microphone caught nothing at all.</summary>
    public const string TwoChannelSilentMe = "two-channel-silent-me";

    public static IEnumerable<string> All =>
        [TwoChannelLong, TwoChannelShort, TwoChannelOneVoiceMe, SingleTrackDiarized, TwoChannelSilentMe];

    /// <summary>
    /// Every fixture, for a theory that is about all of them. Declared here rather than repeated
    /// as a list of attributes per test: the set grows, and a fixture nobody remembered to add to
    /// the ninth list is one whose case is covered everywhere except where it fails.
    /// </summary>
    public static TheoryData<string> Each => new(All);

    /// <summary>
    /// The fixtures whose two channels both caught somebody, which is what a theory about two
    /// sides of a conversation needs. <see cref="TwoChannelSilentMe"/> is multichannel and is
    /// deliberately not one of them.
    /// </summary>
    public static TheoryData<string> EachWithBothSidesHeard =>
        new(TwoChannelLong, TwoChannelShort, TwoChannelOneVoiceMe);

    /// <summary>The profile whose audio each fixture is a transcription of.</summary>
    public static SourceProfile ProfileOf(string name) => name switch
    {
        SingleTrackDiarized => SourceProfile.Diarize,
        TwoChannelLong or TwoChannelShort or TwoChannelOneVoiceMe or TwoChannelSilentMe =>
            SourceProfile.Multichannel,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a fixture."),
    };

    /// <summary>
    /// What the provider returned, as the domain sees it. Enough to drive a rule and no more:
    /// the parser that has to survive a truncated file, a missing field or a channel count that
    /// disagrees with the profile is its own piece of work, and this is not a draft of it.
    /// </summary>
    public static IReadOnlyList<SpeechSegment> Segments(string name)
    {
        using var response = Read(name);
        var multichannel = ProfileOf(name) is SourceProfile.Multichannel;

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
        using var stream = File.OpenRead(Path.Combine(Directory.FullName, name + ".json"));
        return JsonDocument.Parse(stream);
    }

    /// <summary>The closed list of words the fixtures are allowed to be made of.</summary>
    public static IReadOnlySet<string> Vocabulary() => File
        .ReadAllLines(Path.Combine(Directory.FullName, "vocabulary.txt"))
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static DirectoryInfo Directory => Locate();

    private static DirectoryInfo Locate([CallerFilePath] string thisFile = "") => new(
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "fixtures", "deepgram")));
}
