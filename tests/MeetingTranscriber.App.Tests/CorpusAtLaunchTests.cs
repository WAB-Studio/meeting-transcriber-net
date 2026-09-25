namespace MeetingTranscriber.App.Tests;

/// <summary>
/// What <c>App.xaml.cs</c> does about the corpus, apart from <see cref="LaunchWorkTests"/>: which
/// question about it this file answers, and how many times it opens a window over the answer.
/// </summary>
/// <remarks>
/// <para>
/// Read off the source, like every other check in this project: there is no
/// <c>ProjectReference</c> to the application, for the reason <see cref="LaunchWorkTests"/> gives.
/// </para>
/// <para>
/// A file of its own and not a fact added to <c>LaunchWorkTests</c>: that class is about where
/// background work starts, this is about the corpus, and the two go red for different reasons.
/// </para>
/// </remarks>
public sealed class CorpusAtLaunchTests
{
    private static readonly string Launch =
        File.ReadAllText(AppSources.At("MeetingTranscriber.App/App.xaml.cs").FullName);

    /// <summary>
    /// The corpus is resolved once before any window is built, so the first window this
    /// application shows is already drawing the answer.
    /// </summary>
    [Fact]
    public void The_corpus_is_resolved_once_before_any_window_is_built()
    {
        var resolved = Occurrences("CorpusLocation.OfThisUser().Resolve()").ToArray();
        var built = Occurrences("new MainWindow(").ToArray();

        resolved.ShouldNotBeEmpty("App.xaml.cs no longer resolves where the corpus is.");
        built.ShouldNotBeEmpty("App.xaml.cs no longer builds a MainWindow.");

        built.Min().ShouldBeGreaterThan(
            resolved.Min(),
            "App.xaml.cs builds a window before the corpus it is handed has been resolved.");
    }

    /// <summary>
    /// The window that closed only clears the one on screen, which matters the moment a second
    /// window can exist: without the comparison, closing the window a corpus change just replaced
    /// clears <c>_main</c> out from under the one that is now on screen.
    /// </summary>
    [Fact]
    public void The_window_that_closed_only_clears_the_one_on_screen()
    {
        Occurrences("if (ReferenceEquals(sender, _main))").ShouldNotBeEmpty(
            "App.xaml.cs's Closed subscription no longer compares its sender before clearing "
            + "_main, so replacing the window silently breaks whatever the old one was still "
            + "wired to.");

        Occurrences("(_, _) => _main = null").ShouldBeEmpty(
            "App.xaml.cs's Closed subscription clears _main unconditionally again, which is only "
            + "safe while there is exactly one window.");
    }

    /// <summary>
    /// Nothing starts the launch's work but the one method that says what order it is in, however
    /// many times the corpus it runs over changes.
    /// </summary>
    /// <remarks>
    /// What this does not re-check is that there is only one <c>Task.Run</c> in this file:
    /// <c>LaunchWorkTests.The_application_starts_background_work_in_one_place_only</c> already
    /// holds the whole file to exactly one spelling of starting work, of any of five kinds, and a
    /// second assertion of the same fact here would be a second place for that guard to be
    /// forgotten in rather than a second thing proven.
    /// </remarks>
    [Fact]
    public void Nothing_starts_the_launch_work_but_the_one_method_that_says_what_order_it_is_in()
    {
        // Three and not two: the method's own declaration is one occurrence of its name, and
        // OnLaunched and OnCorpusChosen each call it once more.
        Occurrences("StartWhatThisLaunchOwesTheCorpus(").Count().ShouldBe(
            3,
            "App.xaml.cs declares or calls StartWhatThisLaunchOwesTheCorpus a number of times "
            + "other than its own declaration plus the one call each OnLaunched and OnCorpusChosen "
            + "make.");
    }

    /// <summary>
    /// The new window is up and activated before the old one closes, so there is never a moment
    /// with no window open — which is the one thing that would end the process outright, since
    /// nothing here handles an application with no window left.
    /// </summary>
    [Fact]
    public void The_new_window_replaces_the_old_one_before_the_old_one_closes()
    {
        var opened = Occurrences("OpenMainWindow(_corpus)").ToArray();
        var closed = Occurrences("closing?.Close()").ToArray();

        opened.ShouldNotBeEmpty("App.xaml.cs no longer builds a replacement window on a corpus change.");
        closed.ShouldNotBeEmpty("App.xaml.cs no longer closes the window a corpus change replaced.");

        opened.Max().ShouldBeLessThan(
            closed.Min(),
            "App.xaml.cs closes the old window before the new one is up, which is a moment with no "
            + "window open at all.");
    }

    /// <summary>
    /// Only the launch resolves where the corpus is. The settings screen is the one other file
    /// that names <see cref="MeetingTranscriber.Infrastructure.Storage.CorpusLocation.OfThisUser"/>,
    /// and it does so to write a folder somebody picked, never to read one.
    /// </summary>
    [Fact]
    public void Only_the_launch_resolves_where_the_corpus_is()
    {
        var offenders = AppSources.With(".cs")
            .Where(file => file.Name is not ("App.xaml.cs" or "Configuracion.xaml.cs"))
            .Where(file => File.ReadAllText(file.FullName)
                .Contains("CorpusLocation.OfThisUser()", StringComparison.Ordinal))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "these files resolve where the corpus is themselves, which is a second answer to the "
            + "one question this application cannot be wrong about: " + string.Join(", ", offenders));

        File.ReadAllText(AppSources.At("MeetingTranscriber.App/Configuracion.xaml.cs").FullName)
            .ShouldNotContain(
                "CorpusLocation.OfThisUser().Resolve()",
                customMessage: "the settings screen reads where the corpus is rather than only "
                + "writing a folder somebody picked, which is the launch's question and not this "
                + "screen's.");
    }

    private static IEnumerable<int> Occurrences(string what) => SourceLines.Occurrences(Launch, what);
}
