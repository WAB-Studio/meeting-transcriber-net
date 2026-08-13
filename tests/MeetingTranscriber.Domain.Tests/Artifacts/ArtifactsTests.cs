using MeetingTranscriber.Domain.Artifacts;

namespace MeetingTranscriber.Domain.Tests.Artifacts;

/// <summary>
/// The two questions asked of a kind of artifact, which sound like one and are not: what a backup
/// carries and a deletion spares, and whether writing it again destroys anything.
/// </summary>
public class ArtifactsTests
{
    /// <summary>
    /// Both are exhaustive switches over the same enum, so a kind added to one and forgotten in the
    /// other would otherwise be found by whichever write happened to reach it first, in whatever
    /// state that left behind.
    /// </summary>
    [Fact]
    public void Every_kind_of_artifact_has_an_answer_to_both()
    {
        foreach (var kind in Enum.GetValues<ArtifactKind>())
        {
            Should.NotThrow(() => kind.OriginOf(), $"{kind} is on neither side of the source line.");
            Should.NotThrow(() => kind.MayBeReplaced(), $"{kind} does not say whether it may be written again.");
        }
    }

    /// <summary>
    /// The line the whole deletion and backup policy hangs off, spelled out rather than computed,
    /// so moving a kind across it is a failing test and not a quiet change of what a backup holds.
    /// </summary>
    [Theory]
    [InlineData(ArtifactKind.SpoolBlock, ArtifactOrigin.Source)]
    [InlineData(ArtifactKind.Audio, ArtifactOrigin.Source)]
    [InlineData(ArtifactKind.DeepgramResponse, ArtifactOrigin.Source)]
    [InlineData(ArtifactKind.Extraction, ArtifactOrigin.Source)]
    [InlineData(ArtifactKind.Manifest, ArtifactOrigin.Source)]
    [InlineData(ArtifactKind.Transcript, ArtifactOrigin.Derived)]
    [InlineData(ArtifactKind.Utterances, ArtifactOrigin.Derived)]
    [InlineData(ArtifactKind.Summary, ArtifactOrigin.Derived)]
    public void Which_side_of_the_source_line_each_kind_is_on(ArtifactKind kind, ArtifactOrigin origin) =>
        kind.OriginOf().ShouldBe(origin);

    /// <summary>
    /// The other line, and the one place the two disagree. The manifest is a source the corpus can
    /// produce again from the meetings row, so a second write costs nothing — and refusing it would
    /// cost the only thing the card is for.
    /// </summary>
    [Theory]
    [InlineData(ArtifactKind.SpoolBlock, false)]
    [InlineData(ArtifactKind.Audio, false)]
    [InlineData(ArtifactKind.DeepgramResponse, false)]
    [InlineData(ArtifactKind.Extraction, false)]
    [InlineData(ArtifactKind.Manifest, true)]
    [InlineData(ArtifactKind.Transcript, true)]
    [InlineData(ArtifactKind.Utterances, true)]
    [InlineData(ArtifactKind.Summary, true)]
    public void Which_kinds_a_second_write_may_replace(ArtifactKind kind, bool replaceable) =>
        kind.MayBeReplaced().ShouldBe(replaceable);

    /// <summary>
    /// The manifest stated as the exception rather than left implied, because the moment a second
    /// one appears the reasoning above stops holding and somebody has to say why.
    /// </summary>
    [Fact]
    public void The_manifest_is_the_only_source_a_second_write_may_replace() =>
        Enum.GetValues<ArtifactKind>()
            .Where(kind => kind.OriginOf() is ArtifactOrigin.Source && kind.MayBeReplaced())
            .ShouldBe([ArtifactKind.Manifest]);
}
