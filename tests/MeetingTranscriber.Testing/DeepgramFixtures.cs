using System.Runtime.CompilerServices;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Testing;

/// <summary>
/// The committed Deepgram responses, and what each one is there to cover.
/// </summary>
/// <remarks>
/// <para>
/// The only place the set is written down. Every test project that walks the fixtures reads this
/// one, so a sixth response is one edit here and not a file per project to remember — a fixture
/// missing from the ninth list is one whose case is covered everywhere except where it fails.
/// </para>
/// <para>
/// It says which responses exist and what each was transcribed under, and nothing about what is
/// inside them. Reading one is left to whoever is asking: the parser's own tests go through the
/// parser, and the domain's cannot, so a shared reader would only prove the two agree.
/// </para>
/// <para>
/// It carries no <c>TheoryData</c> either, and that is not taste: xunit's own analyzer crashes on
/// a <c>MemberData</c> whose member lives in another assembly, and an analyzer that crashes is a
/// warning, which CI fails on. Each suite wraps this set in the shape a theory wants — one line
/// over <see cref="All"/>, so a sixth response is still one edit here.
/// </para>
/// <para>
/// The files are read out of the repository rather than copied into the test output: they are
/// megabytes of paid response, they change only when the tool that builds them is run, and a copy
/// per configuration buys nothing but a slower build. See tests/fixtures/deepgram/README.md.
/// </para>
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
    /// where <c>Speakers.Resolve</c> naming the wrong label would sign somebody else's words — and
    /// the four fixtures before it left that branch with no real response behind it.
    /// </summary>
    public const string TwoChannelOneVoiceMe = "two-channel-one-voice-me";

    /// <summary>One track with six diarized speakers, the shape a <see cref="SourceProfile.Diarize"/> meeting has.</summary>
    public const string SingleTrackDiarized = "single-track-diarized";

    /// <summary>Both channels, and the microphone caught nothing at all.</summary>
    public const string TwoChannelSilentMe = "two-channel-silent-me";

    /// <summary>
    /// The set itself. Everything below is a view over this, so adding a response is this list and
    /// the constant naming it — never a second list somewhere that has to be kept in step.
    /// </summary>
    private static readonly Fixture[] Inventory =
    [
        new(TwoChannelLong, SourceProfile.Multichannel, BothSidesHeard: true),
        new(TwoChannelShort, SourceProfile.Multichannel, BothSidesHeard: true),
        new(TwoChannelOneVoiceMe, SourceProfile.Multichannel, BothSidesHeard: true),
        new(SingleTrackDiarized, SourceProfile.Diarize, BothSidesHeard: false),
        new(TwoChannelSilentMe, SourceProfile.Multichannel, BothSidesHeard: false),
    ];

    public static IReadOnlyList<string> All => [.. Inventory.Select(fixture => fixture.Name)];

    /// <summary>
    /// The fixtures whose two channels both caught somebody, which is what a theory about two
    /// sides of a conversation needs. <see cref="TwoChannelSilentMe"/> is multichannel and is
    /// deliberately not one of them.
    /// </summary>
    public static IReadOnlyList<string> WithBothSidesHeard =>
        [.. Inventory.Where(fixture => fixture.BothSidesHeard).Select(fixture => fixture.Name)];

    /// <summary>The directory the responses and the vocabulary are committed in.</summary>
    public static DirectoryInfo Directory => Locate();

    /// <summary>
    /// The fixtures transcribed under one profile, for a test that is about what that profile
    /// produces. Asked of the set rather than listed by the caller: a list would be right on the
    /// day it was written and would let the sixth fixture through untested afterwards.
    /// </summary>
    public static IReadOnlyList<string> WithProfile(SourceProfile profile) =>
        [.. Inventory.Where(fixture => fixture.Profile == profile).Select(fixture => fixture.Name)];

    /// <summary>The profile whose audio each fixture is a transcription of.</summary>
    public static SourceProfile ProfileOf(string name) => Entry(name).Profile;

    public static string PathOf(string name) => Path.Combine(Directory.FullName, name + ".json");

    /// <summary>The closed list of words the fixtures are allowed to be made of.</summary>
    public static IReadOnlySet<string> Vocabulary() => File
        .ReadAllLines(Path.Combine(Directory.FullName, "vocabulary.txt"))
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Fixture Entry(string name) =>
        Inventory.SingleOrDefault(fixture => fixture.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Not a fixture.");

    private static DirectoryInfo Locate([CallerFilePath] string thisFile = "") => new(
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "fixtures", "deepgram")));

    /// <summary>One committed response: its name, and what a test needs to know before opening it.</summary>
    private sealed record Fixture(string Name, SourceProfile Profile, bool BothSidesHeard);
}
