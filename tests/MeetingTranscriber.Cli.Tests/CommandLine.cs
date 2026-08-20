namespace MeetingTranscriber.Cli.Tests;

/// <summary>What one command line did: the exit code, and everything it wrote.</summary>
public sealed record Run(int Code, string Output, string Error)
{
    /// <summary>
    /// What a labelled line of the report says, which is how a test reads a meeting id or a turn
    /// count back out. Reading the report rather than the corpus is the point: the command line is
    /// the only thing under test, so what it says has to be enough to carry on with.
    /// </summary>
    public string Value(string label) => Values(label).FirstOrDefault()
        ?? throw new InvalidOperationException($"Nothing in the report says '{label}':\n{Output}{Error}");

    /// <summary>
    /// What every line with that label says, in the order they were written. A listing repeats its
    /// labels once per recording, so this is how a test reads what was said about all of them
    /// rather than about whichever came first.
    /// </summary>
    public IReadOnlyList<string> Values(string label) =>
    [
        .. Output.Split('\n')
            .Select(text => text.TrimEnd('\r'))
            .Where(text => text.StartsWith(label, StringComparison.Ordinal)
                && text.Length > label.Length
                && text[label.Length] is ' ')
            .Select(text => text[label.Length..].Trim()),
    ];
}

/// <summary>
/// The program, driven exactly as somebody at a prompt drives it: a list of strings in, an exit
/// code and two streams out.
/// </summary>
/// <remarks>
/// It calls <see cref="Cli.Run"/> rather than starting the executable. The process boundary would
/// add the two lines of <c>Main</c> to what is covered and a build layout, a working directory and
/// a console encoding to what can go wrong; everything a person types and everything they read
/// back is on this side of it.
/// </remarks>
public static class CommandLine
{
    public static Run Of(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(arguments, output, error);
        return new Run(code, output.ToString(), error.ToString());
    }
}
