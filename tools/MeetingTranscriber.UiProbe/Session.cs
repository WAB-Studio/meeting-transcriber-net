using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>What one screen was, at the moment it was looked at.</summary>
/// <remarks>
/// Both artifacts, and neither of them written down. Where they go is the host's: the command line
/// files them under a name somebody chose, the server hands them straight back inside the turn
/// that asked. A core that wrote files would have made the second host read what it had just
/// caused to be written, which is a round trip through the disk to move bytes between two methods.
/// </remarks>
internal sealed record Screen(string Tree, byte[] Picture, string Size);

/// <summary>
/// One running application, and everything that can be done to it.
/// </summary>
/// <remarks>
/// <para>
/// This is what both hosts are hosts over. It says nothing about how it was asked and nothing
/// about who is listening: every verb answers with what it did — a screen, the name of the window
/// arrived at, or nothing — and the host decides whether that becomes a file, a line of output or
/// a turn.
/// </para>
/// <para>
/// It owns the application, so disposing it closes what it started. That is the whole of the
/// lifetime for the command line, which opens one and walks a script; the server opens one and
/// keeps it across turns, which is what <see cref="MustStillBeFresh"/> is for.
/// </para>
/// </remarks>
internal sealed class Session : IDisposable
{
    /// <summary>
    /// A hedge, and named as one. A screen answers a press on its own thread and the answer is
    /// not there when <c>Invoke</c> returns; this makes the common small change likely to have
    /// landed and guarantees nothing. <see cref="Wait"/> is the only thing in this tool that
    /// synchronises, and a press whose effect is about to be looked at needs one.
    /// </summary>
    private static readonly TimeSpan HedgeAfterAPress = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan ForAList = TimeSpan.FromSeconds(5);

    /// <summary>Enough of a list to say what was there instead, rather than every entry of it.</summary>
    private const int EnoughOfAList = 24;

    /// <summary>
    /// How far one list is asked before the answer is taken for a provider that will not end.
    /// Every list this drives is a picker somebody reads down.
    /// </summary>
    private const int MostItems = 2000;

    private static readonly TimeSpan ForAScreen = TimeSpan.FromSeconds(15);

    private readonly LaunchedApp _app;

    private readonly Freshness _freshness;

    private Session(LaunchedApp app, Freshness freshness)
    {
        _app = app;
        _freshness = freshness;
    }

    /// <summary>
    /// Which application this is driving, and what it is running — plus anything about this
    /// session somebody has to know before they use it. Today that is only a refused leash, and
    /// it goes here because this line is the one thing both hosts say when an application opens.
    /// </summary>
    internal string StartedAs =>
        $"{_app.AppUserModelId} is process {_app.ProcessId}, from {_app.RunningFrom}"
        + (_app.Unleashed is null ? string.Empty : Environment.NewLine + _app.Unleashed);

    /// <summary>
    /// Starts the application and waits for it to have a window with something on it. The
    /// freshness check is inside the opening rather than beside it: a window of the wrong build
    /// reads exactly like a window, so nothing may be done to one before it has been refused.
    /// </summary>
    internal static Session Open()
    {
        var repository = Repository.Around();
        var app = LaunchedApp.Start(repository.AppUserModelId);

        try
        {
            // Whose window is this, and is it this old — in that order, because the second
            // question has no meaning until the first one is answered.
            repository.MustBeWhatWindowsStarted(app.RunningFrom);

            var freshness = Freshness.Of(repository, app.RunningFrom);
            freshness.MustNotPredateTheCode();

            app.OpenAWindow();

            return new Session(app, freshness);
        }
        catch
        {
            // An application started and then refused is still an application running.
            app.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asked again before every instruction, because a session outlives a build. Checking at
    /// launch was enough while a run was one cold start; once an application stays open across
    /// turns, somebody edits a screen halfway through and every answer after that is about a
    /// window that no longer matches the code — and an old window does not look old.
    /// </summary>
    internal void MustStillBeUsable()
    {
        if (_app.HasGone)
        {
            throw new ProbeFailed(
                "The application is not running any more — it was closed, or it crashed. Start it "
                + "again.");
        }

        _freshness.MustNotPredateTheCode();
    }

    /// <summary>Whether there is still an application here at all.</summary>
    internal bool HasGone => _app.HasGone;

    /// <summary>
    /// Both artifacts come off one window in one moment, and neither of them disturbs it: the
    /// picture is printed out of the window rather than copied off the desktop, so nothing is
    /// raised, focused or moved by looking.
    /// </summary>
    internal Screen See()
    {
        var window = _app.Windows.Active();
        var picture = WindowPicture.Of(window);

        return new Screen(UiTree.Render(window), picture.Png, picture.Size);
    }

    /// <summary>The tree alone, which is what a screen that has just changed is asked for.</summary>
    internal string Tree() => UiTree.Render(_app.Windows.Active());

    /// <summary>
    /// Ends the application the way a crash does, with nothing asked and nothing let finish.
    /// </summary>
    /// <remarks>
    /// The only verb here that is not about a screen, and it earns its place on what it reaches:
    /// a recording nobody stopped and a save the process died in the middle of are states the
    /// corpus arrives at only that way, and what a later start finds in them is a question no
    /// polite close can be asked. Disposing this afterwards is still correct and still what the
    /// host does — it finds an application that has already gone and returns.
    /// </remarks>
    internal void Kill() => _app.Kill();

    /// <summary>
    /// Pressing is <c>Invoke</c> and nothing else. A control that offers something else instead —
    /// a switch that toggles, a list that expands — is named along with what it does offer, so the
    /// screen that first needs one says which verb to add rather than this guessing on its behalf.
    /// Disabled is checked first because a dead button is the defect this tool exists to find, and
    /// finding it should read as a sentence and not as a stack trace.
    /// </summary>
    internal void Press(string target)
    {
        var element = Search.One(_app.Windows.Active(), target);

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is disabled: it cannot be pressed.");
        }

        if (!Search.Supports(element, InvokePattern.Pattern))
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} is not something that can be pressed. "
                + $"What it offers instead: {Offers(element)}.");
        }

        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// The value is set rather than typed key by key: what a probe wants in the field is what
    /// somebody meant to leave there, not the six intermediate strings on the way. Held to the
    /// same three questions <see cref="Press"/> asks — is it dead, does it take this at all, and
    /// if not what does it take — because a field that silently refused would be a screen this
    /// tool said nothing about.
    /// </summary>
    internal void Type(string target, string text)
    {
        var element = Search.One(_app.Windows.Active(), target);

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is disabled: nothing can be typed into it.");
        }

        if (!Search.Supports(element, ValuePattern.Pattern))
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} does not take a value. "
                + $"What it offers instead: {Offers(element)}.");
        }

        var field = (ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern);
        if (Reading.Flag(() => field.Current.IsReadOnly) != false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is read only.");
        }

        field.SetValue(text);
        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// A list has to be open before what is in it exists: the items of a closed combo box are not
    /// in the tree, so the container is expanded, the item is waited for, and the list is shut
    /// again — which leaves the screen as somebody choosing would have left it. The element the
    /// wait found is the one selected: looking it up a second time is a second chance for a
    /// light-dismissing popup to have shut in between.
    /// </summary>
    internal void Choose(string container, string item)
    {
        var window = _app.Windows.Active();
        var list = Search.One(window, container);

        var opens = Search.Supports(list, ExpandCollapsePattern.Pattern)
            ? (ExpandCollapsePattern)list.GetCurrentPattern(ExpandCollapsePattern.Pattern)
            : null;
        opens?.Expand();

        var chosen = Patience.Until(ForAList, () =>
            Search.Among(Offered(list), item) is [var only] ? only : null);

        if (chosen is null)
        {
            // Read while the list is still open and only then shut it. The items of a closed combo
            // box are not in the tree at all, so building this message after the collapse — which
            // is what it did until it was run against a list that really did not have the item —
            // named the list and then listed nothing, on the one failure whose whole job is to say
            // what the caller should have asked for instead.
            var all = Offered(list);
            var offered = string.Join(
                Environment.NewLine + "  ",
                all.Take(EnoughOfAList).Select(ElementWords.Line));
            var rest = all.Count > EnoughOfAList
                ? $"{Environment.NewLine}  ... and {all.Count - EnoughOfAList} more"
                : string.Empty;

            opens?.Collapse();

            throw new ProbeFailed(
                $"\"{item}\" is not one thing in {ElementWords.Line(list)}. What is in it:"
                + Environment.NewLine + "  " + offered + rest);
        }

        Realise(chosen);
        ((SelectionItemPattern)chosen.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

        if (opens is not null
            && Reading.Flag(() => opens.Current.ExpandCollapseState == ExpandCollapseState.Expanded) == true)
        {
            opens.Collapse();
        }

        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// Everything the open list offers, drawn or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list long enough to need scrolling draws a window of itself and no more, and what is not
    /// drawn is not in the automation tree — so a walk finds whatever the list happens to be
    /// scrolled to and calls the rest absent. That is this tool's answer being decided by a scroll
    /// position, and it is why a screen was once asked to stop virtualising a picker so that this
    /// could read it: the wrong half paying for a hole in the half that is only looking.
    /// </para>
    /// <para>
    /// <see cref="ItemContainerPattern"/> is what a list implements to be asked about items it has
    /// not drawn, and what it hands back carries the name whether or not it is on screen. A list
    /// that does not implement it is walked exactly as before, which is what every short picker on
    /// every screen here goes on doing.
    /// </para>
    /// </remarks>
    private static List<AutomationElement> Offered(AutomationElement list)
    {
        if (!Search.Supports(list, ItemContainerPattern.Pattern))
        {
            return Search.Everything(list, SelectionItemPattern.Pattern);
        }

        var container = (ItemContainerPattern)list.GetCurrentPattern(ItemContainerPattern.Pattern);
        var offered = new List<AutomationElement>();

        // A ceiling and not a trust: this walks as far as the application says it goes, and a
        // provider answering its own last item with itself would otherwise never come back.
        for (AutomationElement? found = null; offered.Count < MostItems;)
        {
            var previous = found;
            found = Reading.Of(() => container.FindItemByProperty(previous, null, null));
            if (found is null)
            {
                break;
            }

            offered.Add(found);
        }

        return offered;
    }

    /// <summary>
    /// Draws the one item that was chosen, which is what makes it selectable — an item a list
    /// never drew supports nothing else. Before the pattern that acts on it, never after.
    /// </summary>
    private static void Realise(AutomationElement item)
    {
        if (Search.Supports(item, VirtualizedItemPattern.Pattern))
        {
            ((VirtualizedItemPattern)item.GetCurrentPattern(VirtualizedItemPattern.Pattern)).Realize();
        }
    }

    /// <summary>
    /// Across every window, because the thing being waited for is usually on one that did not
    /// exist when the press happened — and the window it turns up on becomes the screen the rest
    /// of the session is about, which is the name this answers with. On more than one it stops:
    /// naming the screen is this verb's whole job, and a verb that guessed would put everything
    /// after it on a window chosen by z-order.
    /// </summary>
    internal string Wait(string target)
    {
        string? trouble = null;

        var arrived = Patience.Until(ForAScreen, () =>
        {
            var on = new List<AutomationElement>();
            foreach (var window in _app.Windows.All())
            {
                try
                {
                    if (Search.Matching(window, target).Count > 0)
                    {
                        on.Add(window);
                    }
                }
                catch (ProbeFailed unreadable)
                {
                    // A window in flux now may be readable in a hundred milliseconds, which is
                    // what waiting is. If it never settles, the budget says so below.
                    trouble = unreadable.Message;
                }
            }

            return on.Count > 0 ? on : null;
        });

        if (arrived is null)
        {
            throw new ProbeFailed(
                $"\"{target}\" never appeared within {ForAScreen.TotalSeconds:0} seconds. "
                + $"Windows open: {WindowsOpen()}."
                + (trouble is null ? string.Empty : Environment.NewLine + trouble));
        }

        if (arrived.Count > 1)
        {
            throw new ProbeFailed(
                $"\"{target}\" is on {arrived.Count} of the application's windows — "
                + $"{AppWindows.Names(arrived)} — so it does not say which screen this is. "
                + "Wait for something only the screen you mean has on it.");
        }

        _app.Windows.TheScreenIs(arrived[0]);

        return ElementWords.Name(arrived[0]);
    }

    public void Dispose() => _app.Dispose();

    private static string Offers(AutomationElement element)
    {
        var patterns = Reading.Of(() => element.GetSupportedPatterns()) ?? [];

        // `InvokePatternIdentifiers.Pattern` reads as `Invoke`. The point of this list is to tell
        // whoever hit it which verb the control wants, and the suffix is on every entry.
        return patterns.Length > 0
            ? string.Join(", ", patterns.Select(one =>
                one.ProgrammaticName.Replace("PatternIdentifiers.Pattern", string.Empty)))
            : "nothing";
    }

    private string WindowsOpen()
    {
        var windows = _app.Windows.All();

        return windows.Count > 0 ? AppWindows.Names(windows) : "none";
    }
}
