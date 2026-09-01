using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The one thing that turns a word written in an instruction into an element on the screen.
/// </summary>
/// <remarks>
/// <para>
/// Every instruction asks this and none of them has a rule of its own, so what
/// <c>press RecordButton</c> means and what <c>wait RecordButton</c> means cannot drift apart.
/// The rule is three tiers, tried in order and never mixed: the name the source gave it, then the
/// exact words on it, then any element whose words contain what was asked for. The first tier
/// with anything in it decides, and a tier holding more than one match is an error rather than a
/// coin toss — a script that pressed whichever button UI Automation happened to list first would
/// pass until the day a screen grew a second one.
/// </para>
/// <para>
/// What differs between the verbs is not the rule but where it is applied, and each of the three
/// scopes is deliberate: <c>press</c> looks at the screen, because that is where a person is
/// looking; <c>choose</c> looks inside the list it was given, because two lists on one screen
/// really do offer the same word and neither of them is wrong; and <c>wait</c> looks at every
/// window, because it is the verb whose job is finding which window a script is on.
/// </para>
/// </remarks>
internal static class Search
{
    /// <summary>Enough of the screen to see what was there instead, and not the whole dump.</summary>
    private const int EnoughToShow = 24;

    /// <summary>
    /// Everything under <paramref name="root"/> that answers to <paramref name="target"/>, in the
    /// tier that decided. Empty when nothing does.
    /// </summary>
    internal static IReadOnlyList<AutomationElement> Matching(
        AutomationElement root,
        string target,
        AutomationPattern? mustSupport = null) =>
        Among(Everything(root, mustSupport), target);

    /// <summary>
    /// The single element <paramref name="target"/> names, or a failure saying what was on the
    /// screen instead — built out of the same walk that failed to find it, so the list cannot
    /// contain the thing the search just said was not there.
    /// </summary>
    internal static AutomationElement One(
        AutomationElement root,
        string target,
        AutomationPattern? mustSupport = null)
    {
        var considered = Everything(root, mustSupport);
        var matches = Among(considered, target);

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            var all = string.Join(Environment.NewLine + "  ", matches.Select(ElementWords.Line));
            throw new ProbeFailed(
                $"\"{target}\" names {matches.Count} things on this screen:{Environment.NewLine}  {all}");
        }

        var some = string.Join(
            Environment.NewLine + "  ",
            considered.Take(EnoughToShow).Select(ElementWords.Line));
        var rest = considered.Count > EnoughToShow
            ? $"{Environment.NewLine}  ... and {considered.Count - EnoughToShow} more"
            : string.Empty;

        throw new ProbeFailed(
            $"Nothing on \"{ElementWords.Name(root)}\" answers to \"{target}\". "
            + $"What is there:{Environment.NewLine}  {some}{rest}");
    }

    /// <summary>
    /// The three tiers applied to a set somebody else gathered. Handed the set rather than a root
    /// because one caller cannot get its own by walking: the items of a virtualised list are not
    /// in the tree until they are scrolled to, and <c>choose</c> asks the list itself for them.
    /// The rule stays here either way — a second caller with a second rule is how
    /// <c>press RecordButton</c> and <c>choose SourcePicker Record</c> would come to mean
    /// different things.
    /// </summary>
    internal static List<AutomationElement> Among(List<AutomationElement> considered, string target)
    {
        var byId = considered
            .Where(element => ElementWords.Id(element).Equals(target, StringComparison.Ordinal))
            .ToList();
        if (byId.Count > 0)
        {
            return byId;
        }

        var byName = considered
            .Where(element => ElementWords.Name(element).Equals(target, StringComparison.Ordinal))
            .ToList();
        if (byName.Count > 0)
        {
            return byName;
        }

        return considered
            .Where(element => ElementWords.Name(element).Contains(target, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Everything the rule is allowed to choose between. A walk that did not reach the whole
    /// screen stops here rather than being searched: "nothing answers to that" said about a screen
    /// only half of which was read is the quiet wrong answer this whole tool exists to refuse.
    /// </summary>
    internal static List<AutomationElement> Everything(
        AutomationElement root,
        AutomationPattern? mustSupport = null)
    {
        var considered = new List<AutomationElement>();
        var whole = UiTree.Walk(root, (element, _) =>
        {
            if (mustSupport is null || Supports(element, mustSupport))
            {
                considered.Add(element);
            }
        });

        return whole
            ? considered
            : throw new ProbeFailed(
                $"\"{ElementWords.Name(root)}\" could not be read whole, so nothing on it can be "
                + $"looked for.{Environment.NewLine}{UiTree.Cut}");
    }

    internal static bool Supports(AutomationElement element, AutomationPattern pattern) =>
        Reading.Flag(() => element.TryGetCurrentPattern(pattern, out _)) == true;
}
