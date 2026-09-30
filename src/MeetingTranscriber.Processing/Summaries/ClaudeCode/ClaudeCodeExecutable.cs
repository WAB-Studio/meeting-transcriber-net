namespace MeetingTranscriber.Processing.Summaries.ClaudeCode;

/// <summary>Where Claude Code's own executable is on this machine, when it says where to look.</summary>
/// <remarks>
/// Nothing here reads a setting or an environment variable: the chosen path and the search path are
/// both handed in, because who keeps a chosen path — <c>ClaudeCodeLocation</c>, under
/// <c>Infrastructure/Storage/</c> — is a later card's, and this file may not reach for it.
/// </remarks>
public static class ClaudeCodeExecutable
{
    /// <summary>
    /// The file names searched for, in order: a compiled executable is taken over a script of the
    /// same name in the same folder.
    /// </summary>
    public static readonly IReadOnlyList<string> Names = ["claude.exe", "claude.cmd"];

    /// <summary>
    /// <paramref name="chosen"/> wins outright, whether or not the file it names exists — a chosen
    /// file that is gone is for <see cref="ClaudeCodeSummaries.IsAvailableAsync"/> to answer
    /// <c>NotOnThisMachine</c> over, not for this to paper over by falling back to the path.
    /// Otherwise each folder of <paramref name="path"/>, split on <c>;</c>, is searched in order for
    /// <see cref="Names"/>. Nowhere on the path is nothing.
    /// </summary>
    public static FileInfo? Find(FileInfo? chosen, string? path)
    {
        if (chosen is not null)
        {
            return chosen;
        }

        foreach (var folder in (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in Names)
            {
                var candidate = new FileInfo(Path.Combine(folder, name));
                if (candidate.Exists)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
