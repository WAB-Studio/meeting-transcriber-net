namespace MeetingTranscriber.Domain.Tests.Fixtures;

/// <summary>
/// The fixture set in the shape a theory wants it, for the tests of this suite.
/// </summary>
/// <remarks>
/// A projection over <see cref="DeepgramFixtures"/> and never a second list: a fixture added there
/// arrives here without anybody touching this file. It is per-suite because xunit's
/// <c>MemberDataShouldReferenceValidMember</c> analyzer throws on a <c>MemberData</c> pointing at
/// another assembly, and a crashed analyzer is a warning the build fails on.
/// </remarks>
public static class FixtureTheories
{
    /// <summary>
    /// Every fixture, for a theory that is about all of them. Declared once rather than repeated
    /// as a list of attributes per test: the set grows, and a fixture nobody remembered to add to
    /// the ninth list is one whose case is covered everywhere except where it fails.
    /// </summary>
    public static TheoryData<string> Each => new(DeepgramFixtures.All);

    /// <summary>
    /// The fixtures whose two channels both caught somebody, which is what a theory about two
    /// sides of a conversation needs.
    /// </summary>
    public static TheoryData<string> EachWithBothSidesHeard => new(DeepgramFixtures.WithBothSidesHeard);
}
