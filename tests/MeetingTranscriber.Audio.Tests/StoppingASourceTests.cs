using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The one rule <c>CaptureSource.Finish</c> owns: which endings refuse to stop a meeting. A stream
/// still inside its device does, because it is still the thread that would write the next block. A
/// stream that ended by itself does not — it is over, and the meeting it is half of gets saved.
/// </summary>
/// <remarks>
/// <para>
/// A tripwire and not a proof, and worth being exact about which. Nothing in this solution can
/// construct a <c>CaptureSource</c>: <c>CaptureTarget</c>'s constructor is <c>private protected</c>
/// and its <c>Open</c> is <c>internal abstract</c>, <c>WasapiStream</c> is <c>internal sealed</c>
/// with a private constructor and a private loop, <c>Finish</c> is itself <c>internal</c>, and
/// there is no <c>InternalsVisibleTo</c> anywhere. Driving this needs a WASAPI endpoint, and no
/// build agent has one. So this reads the source and checks the shape, which catches the line
/// somebody deletes and not the rewrite somebody argues for.
/// </para>
/// <para>
/// Three things it does not reach, all of them true of <c>StoppingARecordingTests</c> in the
/// Recording suite as well, which spells the same comment strip. It reads a <b>file on disk</b> at
/// the path <c>[CallerFilePath]</c> resolved when this assembly was compiled, so it says nothing
/// about the assembly under test. It reads <b>one</b> file, so a refusal reinstated in
/// <c>CaptureSession.Stop</c> or in <c>MeetingRecording.Stop</c> is invisible to it. And it reads
/// past whole-line comments — every line whose first characters are <c>//</c>, <c>*</c> or
/// <c>/*</c> is dropped, which is what keeps the prose explaining this rule from satisfying it, but
/// a comment trailing a line of code survives the strip and could still be matched.
/// </para>
/// </remarks>
public partial class StoppingASourceTests
{
    [Fact]
    public void Only_a_stream_still_inside_its_device_refuses_the_stop()
    {
        var body = TheBodyOfFinish().Match(CodeOf(TheSource()));

        body.Success.ShouldBeTrue(
            "CaptureSource.Finish is not where this reads for any more. What it guards is which "
            + "endings refuse a stop, and a guard that cannot find the method passes over "
            + "anything.");

        Throws().Matches(body.Value).Select(match => match.Groups[1].Value).ShouldBe(
            ["AudioDeviceWedgedException"],
            "CaptureSource.Finish refuses over something other than a stream still inside its "
            + "device, or has stopped refusing at all. A refusal added back is a source that died "
            + "and stayed dead refusing the stop again, so pressing stop on a meeting whose "
            + "microphone was unplugged refuses the meeting and leaves it to a recovery pass. The "
            + "wedged refusal taken away is worse: Finish would flush a spool the abandoned "
            + "draining thread is still writing through, and the truncated audio.wav that lands in "
            + "the corpus is hashed on the way in, after which MeetingRecordings.Filed refuses the "
            + "correct recording forever. ISC-129 is the claim on the sentence that refusal "
            + "carries.");
    }

    /// <summary>
    /// Everything from <c>Finish</c>'s declaration up to the member after it. With whole-line
    /// comments gone, the next line that opens with an attribute or an accessibility keyword is
    /// that member, so the non-greedy match ends on <c>Finish</c>'s closing brace. The attribute is
    /// in the alternation because without it a <c>[SuppressMessage]</c> on the member below would
    /// silently widen the body to cover that member too.
    /// </summary>
    [GeneratedRegex(
        @"internal void Finish\(\)[\s\S]*?(?=\n\s*(?:\[|(?:public|internal|private|protected)\s))")]
    private static partial Regex TheBodyOfFinish();

    /// <summary>
    /// Every refusal, however it is spelled. <c>new</c> is optional because this codebase throws
    /// through static factories as readily — <c>AudioDeviceWedgedException.NoAnswerFrom</c>,
    /// <c>AudioAsk.Answering</c> — and a refusal reintroduced through one would otherwise pass.
    /// </summary>
    [GeneratedRegex(@"\bthrow\s+(?:new\s+)?(\w+)")]
    private static partial Regex Throws();

    /// <summary>The file with every line that is only a comment taken out.</summary>
    private static string CodeOf(FileInfo file) =>
        string.Join('\n', File.ReadLines(file.FullName).Where(line => !IsProse(line)));

    private static bool IsProse(string line) =>
        line.TrimStart() is var start
        && (start.StartsWith("//", StringComparison.Ordinal)
            || start.StartsWith('*')
            || start.StartsWith("/*", StringComparison.Ordinal));

    private static FileInfo TheSource() => new(Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFile())!, "..", "..",
        "src", "MeetingTranscriber.Audio", "CaptureSource.cs")));

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
