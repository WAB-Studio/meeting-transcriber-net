namespace MeetingTranscriber.App.Tests;

/// <summary>
/// A launch used to start two detached tasks over one corpus, and the fix was to make the list of
/// what a launch owes the thing that decides the order. This holds the half of that the application
/// itself has to keep: one place where background work starts, pointed at that list.
/// </summary>
/// <remarks>
/// <para>
/// Read off the source, like every other check in this project: there is no
/// <c>ProjectReference</c> to the application, because touching a type from that assembly fires the
/// Windows App SDK's module initializer and throws outside a packaged host. What the order actually
/// does is proved next to the list, in <c>WhatALaunchOwesTests</c>; what is proved here is that the
/// application still goes through it.
/// </para>
/// <para>
/// Every read goes through <see cref="SourceLines.StandsInACommentedLine"/>, because the reasoning
/// beside this code names the very things these guards look for.
/// </para>
/// </remarks>
public sealed class LaunchWorkTests
{
    private static readonly string Launch =
        File.ReadAllText(AppSources.At("MeetingTranscriber.App/App.xaml.cs").FullName);

    /// <summary>
    /// <c>App.xaml.cs</c> starts background work in exactly one place, and that place is the
    /// launch's ordered list.
    /// </summary>
    /// <remarks>
    /// The rule is the whole file's and not the launch method's: the one <c>Task.Run</c> left sits
    /// in a helper rather than in <c>OnLaunched</c>, so a count scoped to that body would find zero
    /// and hold nothing. A second one anywhere in this file is either a third piece of launch work
    /// added the way the first two were — the regression this card exists to stop — or work that
    /// needs a home of its own, and the message says both.
    /// </remarks>
    [Fact]
    public void The_application_starts_background_work_in_one_place_only()
    {
        Occurrences("Task.Run(").ShouldHaveSingleItem(
            "App.xaml.cs starts background work in more than one place. If what was added is work a "
            + "launch owes the corpus, it belongs in WhatALaunchOwes.InOrder, which is what says "
            + "what order a launch's work runs in and is what keeps two writers off one corpus. If "
            + "it is not launch work, it needs a home and an argument of its own, and this file is "
            + "not it.");
    }

    /// <summary>
    /// The one thing a launch starts is the ordered list, and never a piece of the work directly.
    /// </summary>
    /// <remarks>
    /// The count above is not enough on its own: one <c>Task.Run</c> pointed straight at the sweep
    /// is exactly the state this card started from, minus one task.
    /// </remarks>
    [Fact]
    public void The_one_thing_a_launch_starts_is_the_one_that_says_what_order_it_is_in()
    {
        Occurrences("WhatALaunchOwes.RunIn(").ShouldNotBeEmpty(
            "App.xaml.cs no longer starts the launch's work through the list that says what order "
            + "it runs in.");

        Occurrences("MeetingsNobodyRecorded.").ShouldBeEmpty(
            "App.xaml.cs sweeps the meetings nobody recorded itself. What a launch owes the corpus "
            + "is decided in WhatALaunchOwes.InOrder, so that the order is stated somewhere a test "
            + "can run it.");

        Occurrences("OwedRenders.").ShouldBeEmpty(
            "App.xaml.cs catches up on the renders itself. What a launch owes the corpus is "
            + "decided in WhatALaunchOwes.InOrder, so that the order is stated somewhere a test "
            + "can run it.");
    }

    /// <summary>Where <paramref name="what"/> stands in the launch, ignoring prose about it.</summary>
    private static IEnumerable<int> Occurrences(string what)
    {
        for (var at = Launch.IndexOf(what, StringComparison.Ordinal);
            at >= 0;
            at = Launch.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            if (!SourceLines.StandsInACommentedLine(Launch, at))
            {
                yield return at;
            }
        }
    }
}
