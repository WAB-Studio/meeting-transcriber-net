namespace MeetingTranscriber.App.Tests;

/// <summary>
/// Reading a source file the way a guard over one has to: which line something is on, and whether
/// what was found is code or a sentence about code.
/// </summary>
/// <remarks>
/// One place and not one per guard. Every check in this project that greps the application's own
/// source runs into the same thing — the rule it enforces is one somebody writes down next to the
/// code it governs, with the example that makes it clear, and the example is the thing being
/// banned. A guard that goes red over its own explanation is a guard somebody edits around, and
/// noise is how a guard stops being believed.
/// </remarks>
internal static class SourceLines
{
    /// <summary>Which line of <paramref name="source"/> the character at <paramref name="at"/> is on.</summary>
    public static int LineOf(string source, int at) => source.AsSpan(0, at).Count('\n') + 1;

    /// <summary>Whether what was found at <paramref name="at"/> stands on a line that is all comment.</summary>
    /// <remarks>
    /// What it reads is the line, not the language: a line that opens a comment, and not a comment
    /// opened after code on the same line. Finding that second one means telling <c>//</c> from the
    /// <c>//</c> inside <c>"http://…"</c>, which is a scanner and not a line test, and getting it
    /// wrong would silently drop a real finding rather than merely report a false one. So a
    /// trailing comment is still read as code, and that is the smaller mistake.
    /// <para>
    /// XML's own comment opener is here beside C#'s because the same guards read both: a
    /// <c>&lt;!--</c> paragraph explaining why a screen does not do something names the thing it
    /// does not do, in the file where it does not do it.
    /// </para>
    /// </remarks>
    public static bool StandsInACommentedLine(string source, int at)
    {
        var opens = at == 0 ? 0 : source.LastIndexOf('\n', at - 1) + 1;
        var before = source[opens..at].TrimStart();

        return before.StartsWith("//", StringComparison.Ordinal)
            || before.StartsWith("/*", StringComparison.Ordinal)
            || before.StartsWith('*')
            || before.StartsWith("<!--", StringComparison.Ordinal)
            || IsInsideAnXmlComment(source, at);
    }

    /// <summary>
    /// Whether <paramref name="at"/> falls inside an XML comment that opened on an earlier line.
    /// </summary>
    /// <remarks>
    /// The C# side needs no equivalent because the house style there is <c>///</c> and <c>//</c> on
    /// every line, which the line test already sees. A XAML comment is one <c>&lt;!--</c> and then
    /// twenty lines of prose, so all but its first line look like code to a test that only reads
    /// the line it found something on.
    /// </remarks>
    private static bool IsInsideAnXmlComment(string source, int at)
    {
        var opened = source.LastIndexOf("<!--", at, StringComparison.Ordinal);

        return opened >= 0 && source.IndexOf("-->", opened, StringComparison.Ordinal) is var closed
            && (closed < 0 || closed > at);
    }
}
