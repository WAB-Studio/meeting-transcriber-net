using System.IO;
using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// One script running against one open application: what each verb actually does, and where the
/// artifacts land.
/// </summary>
internal sealed class Session(LaunchedApp app, string folder, TextWriter log)
{
    /// <summary>
    /// A hedge, and named as one. A screen answers a press on its own thread and the answer is
    /// not there when <c>Invoke</c> returns; this makes the common small change likely to have
    /// landed and guarantees nothing. <see cref="Verb.Wait"/> is the only thing in this tool that
    /// synchronises, and a press whose effect is about to be photographed needs one.
    /// </summary>
    private static readonly TimeSpan HedgeAfterAPress = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan ForAList = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ForAScreen = TimeSpan.FromSeconds(15);

    internal void Do(Instruction step)
    {
        log.WriteLine($"  {step}");

        switch (step.Verb)
        {
            case Verb.See:
                See(step.Subject);
                break;
            case Verb.Press:
                Press(step.Subject);
                break;
            case Verb.Choose:
                Choose(step.Subject, step.Detail);
                break;
            case Verb.Wait:
                Wait(step.Subject);
                break;
        }
    }

    /// <summary>
    /// Both artifacts come off one window in one moment, and neither of them disturbs it: the
    /// picture is printed out of the window rather than copied off the desktop, so nothing is
    /// raised, focused or moved by looking.
    /// </summary>
    private void See(string name)
    {
        var called = Named(name);
        var window = app.Windows.Active();

        var picture = Path.Combine(folder, $"{called}.png");
        var size = WindowPicture.WriteTo(picture, window);

        var tree = Path.Combine(folder, $"{called}.tree.txt");
        File.WriteAllText(tree, UiTree.Render(window));

        log.WriteLine($"    {Path.GetFileName(tree)} and {Path.GetFileName(picture)} ({size})");
    }

    /// <summary>
    /// A name off the command line becomes two file names, so it is held to being a file name and
    /// nothing else — a name with a path in it would otherwise write outside the folder it was
    /// given.
    /// </summary>
    private static string Named(string name) =>
        name.Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name is not ("." or "..")
            ? name
            : throw new ProbeFailed($"\"{name}\" is not a name a pair of files can be called.");

    /// <summary>
    /// Pressing is <c>Invoke</c> and nothing else. A control that offers something else instead —
    /// a switch that toggles, a list that expands — is named along with what it does offer, so the
    /// screen that first needs one says which verb to add rather than this guessing on its behalf.
    /// Disabled is checked first because a dead button is the defect this tool exists to find, and
    /// finding it should read as a sentence and not as a stack trace.
    /// </summary>
    private void Press(string target)
    {
        var element = Search.One(app.Windows.Active(), target);

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

    private static string Offers(AutomationElement element)
    {
        var patterns = Reading.Of(() => element.GetSupportedPatterns()) ?? [];

        return patterns.Length > 0
            ? string.Join(", ", patterns.Select(one => one.ProgrammaticName))
            : "nothing";
    }

    /// <summary>
    /// A list has to be open before what is in it exists: the items of a closed combo box are not
    /// in the tree, so the container is expanded, the item is waited for, and the list is shut
    /// again — which leaves the screen as somebody choosing would have left it. The element the
    /// wait found is the one selected: looking it up a second time is a second chance for a
    /// light-dismissing popup to have shut in between.
    /// </summary>
    private void Choose(string container, string item)
    {
        var window = app.Windows.Active();
        var list = Search.One(window, container);

        var opens = Search.Supports(list, ExpandCollapsePattern.Pattern)
            ? (ExpandCollapsePattern)list.GetCurrentPattern(ExpandCollapsePattern.Pattern)
            : null;
        opens?.Expand();

        var chosen = Patience.Until(ForAList, () =>
            Search.Matching(list, item, SelectionItemPattern.Pattern) is [var only] ? only : null);

        if (chosen is null)
        {
            opens?.Collapse();
            throw new ProbeFailed(
                $"\"{item}\" is not one thing in {ElementWords.Line(list)}. What is in it:"
                + Environment.NewLine + "  "
                + string.Join(
                    Environment.NewLine + "  ",
                    Search.Everything(list, SelectionItemPattern.Pattern)
                        .Select(ElementWords.Line)));
        }

        ((SelectionItemPattern)chosen.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

        if (opens is not null
            && Reading.Flag(() => opens.Current.ExpandCollapseState == ExpandCollapseState.Expanded) == true)
        {
            opens.Collapse();
        }

        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// Across every window, because the thing being waited for is usually on one that did not
    /// exist when the press happened — and the window it turns up on becomes the screen the rest
    /// of the script is about. On more than one it stops: naming the screen is this verb's whole
    /// job, and a verb that guessed would put every instruction after it on a window chosen by
    /// z-order.
    /// </summary>
    private void Wait(string target)
    {
        string? trouble = null;

        var arrived = Patience.Until(ForAScreen, () =>
        {
            var on = new List<AutomationElement>();
            foreach (var window in app.Windows.All())
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

        app.Windows.TheScreenIs(arrived[0]);
        log.WriteLine($"    on \"{ElementWords.Name(arrived[0])}\"");
    }

    private string WindowsOpen()
    {
        var windows = app.Windows.All();

        return windows.Count > 0 ? AppWindows.Names(windows) : "none";
    }
}
