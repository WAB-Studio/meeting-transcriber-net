using MeetingTranscriber.Domain.Artifacts;

namespace MeetingTranscriber.Domain.Tests.Artifacts;

/// <summary>
/// The one rule that says which version a response's file name is, and the one it is not.
/// </summary>
public class ResponseVersionsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    public void Every_version_is_named_and_read_back_as_itself(int version)
    {
        var named = ResponseVersions.Named(version);

        ResponseVersions.VersionOf(named).ShouldBe(version);
    }

    [Theory]
    [InlineData("deepgram.v1.json")]
    [InlineData("deepgram.v0.json")]
    [InlineData("deepgram.v02.json")]
    [InlineData("deepgram.v99999999999.json")]
    [InlineData("deepgram.vx.json")]
    [InlineData("deepgram.refused.0123456789abcdef0123456789abcdef.json")]
    [InlineData("deepgram.json.partial")]
    [InlineData("transcript.md")]
    public void A_name_outside_the_series_is_no_version(string name)
    {
        ResponseVersions.VersionOf(name).ShouldBeNull();
    }

    [Fact]
    public void Case_is_not_what_tells_a_version_apart()
    {
        ResponseVersions.VersionOf("DEEPGRAM.JSON").ShouldBe(1);
        ResponseVersions.VersionOf("Deepgram.V2.Json").ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void There_is_no_version_below_the_first(int version)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ResponseVersions.Named(version));
    }

    [Theory]
    [InlineData("", null, 0, 1, null)]
    [InlineData("deepgram.json", "deepgram.json", 1, 2, null)]
    [InlineData("deepgram.v3.json,deepgram.json,deepgram.v2.json", "deepgram.v3.json", 3, 4, null)]
    [InlineData("deepgram.json,deepgram.v02.json,deepgram.v2.json", "deepgram.v2.json", 2, 3, "deepgram.v02.json")]
    [InlineData("deepgram.v02.json,deepgram.v0.json", null, 0, 1, "deepgram.v0.json")]
    public void The_highest_placed_version_is_current_and_an_unplaced_name_is_named(
        string names, string? highest, int highestVersion, int next, string? unplaced)
    {
        var rows = names.Length == 0
            ? []
            : names.Split(',').Select(name => new Artifact
            {
                RelativePath = $"meetings/{Guid.Empty}/{name}",
                Sha256 = new string('a', 64),
            }).ToList();

        var series = ResponseVersions.Highest(rows);

        Path.GetFileName(series.Highest?.RelativePath).ShouldBe(highest);
        series.HighestVersion.ShouldBe(highestVersion);
        series.Next.ShouldBe(next);
        Path.GetFileName(series.Unplaced?.RelativePath).ShouldBe(unplaced);
    }
}
