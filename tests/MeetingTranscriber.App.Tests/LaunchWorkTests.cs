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
    /// Every way this file could put work on another thread, so the count below cannot be walked
    /// around by spelling one of them differently.
    /// </summary>
    /// <remarks>
    /// Worth writing as a list, unlike the exceptions a file system can raise, because this one is
    /// closed: these are the ways .NET hands work to the pool or to a thread. A guard that knew
    /// only the first of them stayed green with a second detached corpus writer sitting in
    /// <c>OnLaunched</c> under <c>Task.Factory.StartNew</c>, which is how this list came to exist.
    /// If a sixth spelling appears it belongs here; the cost of missing one is that this file goes
    /// back to being somewhere launch work can be added quietly.
    /// </remarks>
    private static readonly string[] StartsWork =
    [
        "Task.Run(",
        "Task.Factory.StartNew(",
        "ThreadPool.QueueUserWorkItem(",
        "ThreadPool.UnsafeQueueUserWorkItem(",
        "new Thread(",
    ];

    /// <summary>
    /// <c>App.xaml.cs</c> starts background work in exactly one place, and that place is the
    /// launch's ordered list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is the whole file's and not the launch method's: the one start left sits in a
    /// helper rather than in <c>OnLaunched</c>, so a count scoped to that body would find zero and
    /// hold nothing. A second one anywhere in this file is either a third piece of launch work
    /// added the way the first two were — the regression this card exists to stop — or work that
    /// needs a home of its own, and the message says both.
    /// </para>
    /// <para>
    /// What it does not cover, said plainly so nobody reads it as more than it is: this file only,
    /// and only work started by name. <c>MainWindow.xaml.cs</c> and <c>MeetingsDrawer.xaml.cs</c>
    /// start their own background passes and are none of this guard's business — both read files
    /// and neither writes the corpus, and both hang off a screen rather than off a launch. A corpus
    /// writer started from either would be this same defect wearing a different hat, and nothing
    /// here would see it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_application_starts_background_work_in_one_place_only()
    {
        StartsWork.SelectMany(Occurrences).ShouldHaveSingleItem(
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
    /// The count above is not enough on its own: one start pointed straight at the sweep is exactly
    /// the state this card started from, minus one task.
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
    private static IEnumerable<int> Occurrences(string what) =>
        SourceLines.Occurrences(Launch, what);
}
