namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// What <c>capture</c> refuses before it opens anything. Nothing here records: a test that
/// touched this machine's microphone would pass or fail on which hardware ran it, and the run
/// that proves two streams open at once is a probe somebody watches, recorded in the ISA.
/// </summary>
/// <remarks>
/// Which leaves the half that is worth a suite anyway. A recording that starts on a mistyped line
/// is worse here than anywhere else in this program: it takes minutes or hours, and what comes
/// back is a meeting somebody has to be told they have to hold again.
/// </remarks>
public class CaptureCommandTests
{
    [Fact]
    public void Recording_without_saying_for_how_long_is_a_misuse()
    {
        var run = CommandLine.Of("capture", "--out", "spike");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--seconds");
    }

    [Fact]
    public void Recording_without_saying_where_it_goes_is_a_misuse()
    {
        var run = CommandLine.Of("capture", "--seconds", "30");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--out");
    }

    [Fact]
    public void A_length_that_is_not_a_length_is_a_misuse()
    {
        var run = CommandLine.Of("capture", "--out", "spike", "--seconds", "a while");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("a while");
    }

    /// <summary>
    /// A misspelled flag has to stop the command rather than be ignored: <c>--mic</c> silently
    /// dropped records the wrong microphone for as long as the meeting lasts.
    /// </summary>
    [Fact]
    public void A_flag_this_command_does_not_read_is_a_misuse()
    {
        var run = CommandLine.Of("capture", "--out", "spike", "--seconds", "30", "--mic", "fifine");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--mic");
    }

    [Fact]
    public void Listing_devices_takes_nothing_of_its_own()
    {
        var run = CommandLine.Of("devices", "--corpus", ".");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--corpus");
    }
}
