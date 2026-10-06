using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Testing;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What channel 0 is watched for follows what channel 0 listens to, and nothing else. Real
/// processes, because the watch is a handle Windows signals; and real targets, because the rule is
/// about them.
/// </summary>
public sealed class ProgramWatchTests
{
    /// <summary>
    /// A channel moved from one program to another is watched on the second, and the first ending
    /// afterwards is not news.
    /// </summary>
    [Fact]
    public void A_channel_moved_onto_another_program_watches_that_one_and_not_the_one_before()
    {
        using var a = AnotherProcess.Waiting();
        using var b = AnotherProcess.Waiting();
        using var watch = new ProgramWatch();

        try
        {
            watch.Watch(new CaptureTarget.Program(new AudioProcess(a.Id, "powershell", 0)));
            watch.Watch(new CaptureTarget.Program(new AudioProcess(b.Id, "powershell", 0)));

            AnotherProcess.Kill(a);
            watch.HasGone.ShouldBeFalse();

            AnotherProcess.Kill(b);

            // Waited for and not read once: a killed tree stays in the machine's list for a moment
            // while Windows lets go of it, which the screen's next look a second later never sees.
            SpinWait.SpinUntil(() => watch.HasGone, TimeSpan.FromSeconds(10)).ShouldBeTrue();
        }
        finally
        {
            AnotherProcess.Kill(a);
            AnotherProcess.Kill(b);
        }
    }

    /// <summary>
    /// The whole machine has no program in it to end, so a program that ended before the channel
    /// moved there is no longer what is being said.
    /// </summary>
    [Fact]
    public void A_channel_moved_onto_the_whole_machine_watches_nothing()
    {
        using var a = AnotherProcess.Waiting();
        using var watch = new ProgramWatch();

        try
        {
            watch.Watch(new CaptureTarget.Program(new AudioProcess(a.Id, "powershell", 0)));
            AnotherProcess.Kill(a);
            watch.HasGone.ShouldBeTrue();

            watch.Watch(new CaptureTarget.TheWholeMachine());

            watch.HasGone.ShouldBeFalse();
        }
        finally
        {
            AnotherProcess.Kill(a);
        }
    }

    /// <summary>A microphone is channel 1's, and naming a program closed over it would be a lie.</summary>
    [Fact]
    public void A_microphone_is_never_handed_to_channel_zeros_watch()
    {
        using var watch = new ProgramWatch();

        Should.Throw<AudioContractException>(() =>
            watch.Watch(new CaptureTarget.Endpoint(new AudioDevice("{id}", "Micrófono", true))));
    }

    /// <summary>
    /// The watch changes hands where channel 0 moves and when a session opens, and in no other
    /// place: a path that moved the channel by hand would be a path that could name the wrong
    /// program as closed.
    /// </summary>
    [Fact]
    public void Channel_zeros_watch_is_handed_over_where_the_channel_moves_and_nowhere_else()
    {
        var code = SourceText.WithoutProse(
            RepositoryTree.At("src/MeetingTranscriber.Audio/CaptureSession.cs"));

        code.ShouldNotContain("FollowedProgram.Watching");
        code.Split("channelZero.Watch(").Length.ShouldBe(3, "once in Opening and once in Move.");
        BodyOf(code, "private static CaptureSession Opening(").ShouldContain("channelZero.Watch(");
        BodyOf(code, "private void Move(").ShouldContain("channelZero.Watch(");
    }

    /// <summary>
    /// A method's text up to the next private member. A text guard and not a proof: it cannot see
    /// a call from another class, and a member added after the method with another access would be
    /// read as part of it.
    /// </summary>
    private static string BodyOf(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0, $"{signature} is not in CaptureSession.cs.");
        var body = code[start..];
        var end = body.IndexOf("\n    private ", 1, StringComparison.Ordinal);
        return end < 0 ? body : body[..end];
    }
}
