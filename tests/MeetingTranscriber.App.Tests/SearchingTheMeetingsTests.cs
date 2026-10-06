namespace MeetingTranscriber.App.Tests;

/// <summary>
/// How the drawer searches: where the magnifier is, that a keystroke never runs on the thread that
/// draws or queues behind older ones, and that closing gives the position back.
/// </summary>
/// <remarks>
/// The window cannot be run here, so this reads source. What it holds is the seams: each fact is
/// the line the behaviour hangs off, and each goes red with that line taken out. What the field
/// looks like and how the results read is the owner's walk.
/// </remarks>
public class SearchingTheMeetingsTests
{
    [Fact]
    public void The_header_holds_the_magnifier_before_the_caret()
    {
        var markup = Read("MeetingsDrawer.xaml");

        var search = markup.IndexOf("x:Name=\"SearchButton\"", StringComparison.Ordinal);
        var openness = markup.IndexOf("x:Name=\"OpennessButton\"", StringComparison.Ordinal);

        search.ShouldBeGreaterThan(0);
        openness.ShouldBeGreaterThan(search);
        markup.ShouldContain("x:Name=\"SearchBox\"");
        markup.ShouldContain("TextChanged=\"OnSearchTyped\"");
        markup.ShouldContain("KeyDown=\"OnSearchKeyDown\"");
    }

    [Fact]
    public void What_is_typed_is_searched_off_the_thread_that_draws_with_at_most_one_in_flight()
    {
        var typed = Body("private void OnSearchTyped(");
        var run = Body("private async void RunTheSearch(");

        typed.ShouldContain("RunTheSearch(SearchBox.Text)");
        run.ShouldContain("if (_searchIsRunning)");
        run.ShouldContain("_searchWaiting = typed;");
        run.ShouldContain("await Task.Run(() => AskTheCorpus(");
        run.ShouldContain("RunTheSearch(next)");
        Body("private static IReadOnlyList<MeetingFound> AskTheCorpus(").ShouldContain("MeetingSearch.Find(context, typed)");
    }

    [Fact]
    public void A_failure_reaches_the_status_line_and_nothing_else()
    {
        var run = Body("private async void RunTheSearch(");

        run.ShouldContain("catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))");
        run.ShouldContain("_status.Says(UiTexts.ThatDidNotGoThrough, failed);");
    }

    [Fact]
    public void Closing_gives_the_drawer_back_the_position_it_was_in()
    {
        Body("private void OnSearchPressed(").ShouldContain("_wholeBeforeTheSearch = HasTheWholeWindow;");
        Body("private void CloseTheSearch(").ShouldContain("TakeTheWholeWindow(_wholeBeforeTheSearch);");
        Body("private void OnSearchTyped(").ShouldContain("CloseTheSearch();");
        Body("private void OnSearchKeyDown(").ShouldContain("Windows.System.VirtualKey.Escape");
    }

    [Fact]
    public void A_search_is_drawn_from_what_it_found_and_never_from_the_listed_meetings()
    {
        Body("private void Render(").ShouldContain("Found(one)");

        var found = Body("private UIElement Found(");
        found.ShouldNotContain("_meetings");
        found.ShouldContain("OpenPress(found.Meeting)");
    }

    [Fact]
    public void The_list_is_searched_again_when_it_is_read_again_under_an_open_search()
    {
        Body("public void Read(").ShouldContain("RunTheSearch(SearchBox.Text)");
    }

    private static string Read(string file) =>
        File.ReadAllText(AppSources.At(Path.Combine("MeetingTranscriber.App", file)).FullName);

    /// <summary>
    /// A method's own body, from its signature to the brace that balances it, so an assertion over
    /// it says nothing about the rest of the drawer.
    /// </summary>
    private static string Body(string signature)
    {
        var screen = Read("MeetingsDrawer.xaml.cs");
        var start = screen.IndexOf(signature, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"MeetingsDrawer no longer has '{signature}'.");

        var depth = 0;
        for (var at = screen.IndexOf('{', start); at < screen.Length; at++)
        {
            if (screen[at] == '{')
            {
                depth++;
            }
            else if (screen[at] == '}' && --depth == 0)
            {
                return screen[start..(at + 1)];
            }
        }

        throw new InvalidOperationException($"'{signature}' never closes.");
    }
}
