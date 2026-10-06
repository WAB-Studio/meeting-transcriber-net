using System.Diagnostics;

namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// Whether the program channel 0 follows is still running, read off a handle held on it. Every
/// fact here is about a real operating system process, because the claim is about what Windows
/// signals and no fabricated id can stand in for that.
/// </summary>
public sealed class FollowedProgramTests
{
    /// <summary>
    /// A program that is running has not gone. This test's own process is the one that is certainly
    /// running for as long as the fact is.
    /// </summary>
    [Fact]
    public void A_program_that_is_running_has_not_gone()
    {
        using var me = Process.GetCurrentProcess();
        using var watch = FollowedProgram.Watching(new AudioProcess(me.Id, me.ProcessName, 0));

        watch.HasGone.ShouldBeFalse();
    }

    /// <summary>
    /// The case the whole type exists for: a program that ends while it is followed is gone, read
    /// off the handle opened while it ran.
    /// </summary>
    [Fact]
    public void A_program_that_ends_while_it_is_followed_has_gone()
    {
        using var program = AnotherProcess.Waiting();
        using var watch = FollowedProgram.Watching(new AudioProcess(program.Id, "powershell", 0));

        try
        {
            watch.HasGone.ShouldBeFalse("the program was still running when it was first asked.");
        }
        finally
        {
            AnotherProcess.Kill(program);
        }

        SpinWait.SpinUntil(() => watch.HasGone, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    /// <summary>
    /// A launcher-style program: the root ends and the process it started goes on playing. The
    /// program has not gone, because the tree is what is followed.
    /// </summary>
    [Fact]
    public void A_root_that_exits_while_its_child_plays_has_not_gone()
    {
        using var program = AnotherProcess.Waiting();
        var child = new AudioProcess(program.Id + 1_000_000, "player", StartedBy: program.Id);
        using var watch = FollowedProgram.Watching(
            new AudioProcess(program.Id, "powershell", 0), () => [child]);

        AnotherProcess.Kill(program);

        watch.HasGone.ShouldBeFalse("a child of the root is still running.");
    }

    /// <summary>
    /// The root has ended and nothing in the list descends from it, however far down the chain
    /// goes, so the program has gone. A process that merely runs does not keep it.
    /// </summary>
    [Fact]
    public void A_root_and_its_children_gone_has_gone()
    {
        using var program = AnotherProcess.Waiting();
        var unrelated = new AudioProcess(program.Id + 1_000_000, "other", StartedBy: 4);
        using var watch = FollowedProgram.Watching(
            new AudioProcess(program.Id, "powershell", 0), () => [unrelated]);

        AnotherProcess.Kill(program);

        watch.HasGone.ShouldBeTrue();
    }

    /// <summary>
    /// An id nothing runs as is a program that has already gone, and following it is not an error:
    /// the list a person picked from can be a second old.
    /// </summary>
    [Fact]
    public void A_program_no_longer_running_when_it_is_followed_has_already_gone()
    {
        int id;
        using (var program = AnotherProcess.Waiting())
        {
            id = program.Id;
            AnotherProcess.Kill(program);
        }

        using var watch = FollowedProgram.Watching(new AudioProcess(id, "powershell", 0));

        SpinWait.SpinUntil(() => watch.HasGone, TimeSpan.FromSeconds(10)).ShouldBeTrue();
    }

    /// <summary>
    /// A watch that was let go of is asked about by a screen that has not noticed yet. It says the
    /// program has not gone, because nothing is known, and it throws nothing.
    /// </summary>
    [Fact]
    public void A_watch_let_go_of_answers_without_throwing()
    {
        using var me = Process.GetCurrentProcess();
        var watch = FollowedProgram.Watching(new AudioProcess(me.Id, me.ProcessName, 0));

        watch.Dispose();
        watch.Dispose();

        watch.HasGone.ShouldBeFalse();
    }
}
