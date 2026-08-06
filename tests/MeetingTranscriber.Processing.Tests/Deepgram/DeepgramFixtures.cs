using System.Runtime.CompilerServices;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// Where the committed responses are, and which profile each one was transcribed under.
/// </summary>
/// <remarks>
/// It says nothing about what is inside them: reading that is the parser's job and this is the
/// project that tests it. <c>MeetingTranscriber.Domain.Tests</c> has a reader of its own for the
/// same files, and deliberately so — the domain cannot reference the parser, and a rule proved
/// against the parser's own output would only be proving the two agree.
/// </remarks>
public static class DeepgramFixtures
{
    /// <summary>Both channels, 58 minutes, and two people sharing the microphone.</summary>
    public const string TwoChannelLong = "two-channel-long";

    /// <summary>Both channels, 34 minutes. The one to reach for when length is not the point.</summary>
    public const string TwoChannelShort = "two-channel-short";

    /// <summary>One track with six diarized speakers, the shape a <see cref="SourceProfile.Diarize"/> meeting has.</summary>
    public const string SingleTrackDiarized = "single-track-diarized";

    /// <summary>Both channels, and the microphone caught nothing at all.</summary>
    public const string TwoChannelSilentMe = "two-channel-silent-me";

    public static IEnumerable<string> All =>
        [TwoChannelLong, TwoChannelShort, SingleTrackDiarized, TwoChannelSilentMe];

    /// <summary>The profile whose audio each fixture is a transcription of.</summary>
    public static SourceProfile ProfileOf(string name) => name switch
    {
        SingleTrackDiarized => SourceProfile.Diarize,
        TwoChannelLong or TwoChannelShort or TwoChannelSilentMe => SourceProfile.Multichannel,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a fixture."),
    };

    public static string PathOf(string name) => Path.Combine(Directory.FullName, name + ".json");

    public static DirectoryInfo Directory => Locate();

    private static DirectoryInfo Locate([CallerFilePath] string thisFile = "") => new(
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "fixtures", "deepgram")));
}
