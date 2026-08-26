using System.Text;
using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The automation tree of one window, walked once and written down.
/// </summary>
/// <remarks>
/// <para>
/// This is the artifact the tool exists for. A picture answers "this is broken"; only the tree
/// answers "this string is wrong", "this button is dead" and "this screen says nothing in
/// English" — which is what the claims about words and about accessibility are made of.
/// </para>
/// <para>
/// <see cref="Walk"/> is the only thing that decides what is in a tree, and both the dump and the
/// search go through it. That is not tidiness: an agent presses what it read out of a dump, so a
/// search that could reach an element the dump never printed would be a screen an agent cannot
/// read and can somehow operate. The same rule applies in reverse, which is why a walk that could
/// not finish is a fact both callers have to deal with rather than a boolean one of them drops.
/// </para>
/// </remarks>
internal static class UiTree
{
    /// <summary>
    /// A screen this deep or this wide is a runaway rather than a screen, and a tool that hung on
    /// it would look like the application hanging.
    /// </summary>
    private const int MostElements = 4000;

    private const int DeepestNesting = 60;

    /// <summary>
    /// What a partial walk means, said the same way to whoever reads a dump and to whoever gets a
    /// failure out of a search: a screen that is only partly known is not a screen anything may be
    /// concluded about.
    /// </summary>
    internal const string Cut =
        "... this tree is not all of the screen: it hit a ceiling, or a branch of it went away "
        + "while it was being read. Nothing may be concluded from what is missing.";

    /// <summary>
    /// The control view: what a person is offered, without the layout panels that hold it. The
    /// raw view would put a dozen borders and presenters between a heading and its window, which
    /// is noise in every line of every dump.
    /// </summary>
    private static readonly TreeWalker View = TreeWalker.ControlViewWalker;

    /// <summary>
    /// Every element under <paramref name="root"/>, itself first, depth first, with how deep each
    /// one is. Returns false when the walk did not reach the whole of it — a ceiling, or a branch
    /// that disappeared while it was being read.
    /// </summary>
    internal static bool Walk(AutomationElement root, Action<AutomationElement, int> visit)
    {
        var seen = 0;
        return Descend(root, 0, ref seen, visit);
    }

    private static bool Descend(
        AutomationElement element,
        int depth,
        ref int seen,
        Action<AutomationElement, int> visit)
    {
        if (seen >= MostElements || depth > DeepestNesting)
        {
            return false;
        }

        seen++;
        visit(element, depth);

        // Caught here rather than through Reading, because here the difference matters: null is
        // "no more", and a throw is "the rest was never looked at". Collapsing the two is how a
        // half-read screen gets written out as a whole one.
        AutomationElement? child;
        try
        {
            child = View.GetFirstChild(element);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        var whole = true;
        while (child is not null)
        {
            whole &= Descend(child, depth + 1, ref seen, visit);

            try
            {
                child = View.GetNextSibling(child);
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return whole;
    }

    /// <summary>
    /// One window as text: a header saying which window and when, then a line per element.
    /// </summary>
    internal static string Render(AutomationElement window)
    {
        var text = new StringBuilder();
        // Through Reading like every other read in this tool. It was not, and that mattered: a
        // press that closes the window it was on races this line, and an unguarded throw here
        // comes back as "the probe broke" about a press that worked.
        text.Append("window \"").Append(ElementWords.Name(window)).Append('"')
            .Append(" hwnd=0x").Append(AppWindows.Handle(window).ToString("X8"))
            .Append(" pid=").Append(Reading.Of(() => window.Current.ProcessId.ToString()) ?? "gone")
            .Append(" read ").Append(DateTime.UtcNow.ToString("O"))
            .AppendLine();

        var whole = Walk(window, (element, depth) =>
            text.Append(' ', depth * 2).AppendLine(LineFor(element)));

        if (!whole)
        {
            text.AppendLine(Cut);
        }

        return text.ToString();
    }

    /// <summary>
    /// What the element is, what it is called in the source, and every string on it a person can
    /// read — plus the two states that decide whether reading it is the whole story.
    /// </summary>
    private static string LineFor(AutomationElement element)
    {
        var line = new StringBuilder(ElementWords.Line(element));

        Say(line, "value", ValueOf(element));
        Say(line, "help", Reading.Of(() => element.Current.HelpText));
        Say(line, "status", Reading.Of(() => element.Current.ItemStatus));

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            line.Append("  disabled");
        }

        if (Reading.Flag(() => element.Current.IsOffscreen) == true)
        {
            line.Append("  offscreen");
        }

        return line.ToString();
    }

    private static void Say(StringBuilder line, string label, string? said)
    {
        if (!string.IsNullOrEmpty(said))
        {
            line.Append("  ").Append(label).Append("=\"").Append(said).Append('"');
        }
    }

    /// <summary>
    /// What a field holds. It is not the <c>Name</c> — a field's name is its label and its value
    /// is what somebody typed, and a dump that showed only the first could not tell a form filled
    /// in from a form empty.
    /// </summary>
    private static string? ValueOf(AutomationElement element) =>
        Search.Supports(element, ValuePattern.Pattern)
            ? Reading.Of(() => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value)
            : null;
}
