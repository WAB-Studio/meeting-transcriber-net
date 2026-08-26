using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// How one element is spelled — in the dumped tree, and in the message that says it could not be
/// found. The same spelling both times on purpose: what an agent reads out of a tree is what it
/// writes back into the next instruction.
/// </summary>
internal static class ElementWords
{
    internal static string Name(AutomationElement element) =>
        Reading.Of(() => element.Current.Name) ?? string.Empty;

    internal static string Id(AutomationElement element) =>
        Reading.Of(() => element.Current.AutomationId) ?? string.Empty;

    /// <summary>
    /// <c>ControlType.Button</c> reads as <c>Button</c>: the prefix is on every line of every
    /// tree and says nothing.
    /// </summary>
    internal static string Type(AutomationElement element) =>
        (Reading.Of(() => element.Current.ControlType.ProgrammaticName) ?? string.Empty)
            .Replace("ControlType.", string.Empty);

    /// <summary>One element as a message names it.</summary>
    internal static string Line(AutomationElement element)
    {
        var id = Id(element);
        var name = Name(element);

        return Type(element)
            + (id.Length > 0 ? $" #{id}" : string.Empty)
            + (name.Length > 0 ? $" \"{name}\"" : string.Empty);
    }
}
