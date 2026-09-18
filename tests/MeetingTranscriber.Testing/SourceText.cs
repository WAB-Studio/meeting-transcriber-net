namespace MeetingTranscriber.Testing;

/// <summary>
/// A source file read the way a guard over one has to: the code, without the prose about it.
/// </summary>
/// <remarks>
/// <para>
/// One place and not one per guard. The rule a guard enforces is one somebody writes down next to
/// the code it governs, with the example that makes it clear — and the example is the thing being
/// banned. A guard that goes red over its own explanation is a guard somebody edits around, and
/// noise is how a guard stops being believed.
/// </para>
/// <para>
/// <b>There is a second answer to this in <c>tests/MeetingTranscriber.App.Tests/SourceLines.cs</c>,
/// and it stays.</b> That one asks whether a character at an offset stands in a commented line,
/// XAML comments included, which is a different question from <em>what is this file's code</em>; and
/// that project may not reference this one, for the reason <see cref="RepositoryTree"/> gives.
/// </para>
/// </remarks>
public static class SourceText
{
    /// <summary>The file's lines with every whole-line comment taken out, joined with <c>\n</c>.</summary>
    public static string WithoutProse(FileInfo file) =>
        string.Join('\n', File.ReadLines(file.FullName).Where(line => !IsProse(line)));

    /// <summary>Whether a line is a sentence about code rather than code.</summary>
    public static bool IsProse(string line) =>
        line.TrimStart() is var start
        && (start.StartsWith("//", StringComparison.Ordinal)
            || start.StartsWith('*')
            || start.StartsWith("/*", StringComparison.Ordinal));
}
