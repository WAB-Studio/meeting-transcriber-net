using System.Text;

namespace MeetingTranscriber.Processing.Summaries.ClaudeCode;

/// <summary>
/// The folder one run is given, and the prompt built from exactly what it holds — a fresh
/// <c>&lt;workspaces&gt;/&lt;new guid&gt;/</c> that carries nothing but this request's own files, so
/// nothing else on this machine is ever within reach of a run that has no tools to reach with.
/// </summary>
/// <remarks>
/// Every file is written twice over: once to the folder, as the file's own bytes and nothing else,
/// and once into <see cref="Written.Prompt"/> — the text sent over standard input, since a run with
/// no tools cannot open a file from the folder it was started in. The folder exists so the working
/// directory is somewhere of its own — the settings source Decides 4 asks for, and what
/// <c>--setting-sources project</c> reads from — and so a person auditing a run afterward can see
/// exactly what it held, before it was deleted.
/// </remarks>
internal static class ClaudeCodeWorkspace
{
    private static readonly UTF8Encoding NoBom = new(false);

    /// <summary>One run's own folder, and the prompt built from what it holds, in order.</summary>
    public sealed record Written(DirectoryInfo Folder, string Prompt);

    public static Written Build(DirectoryInfo workspaces, ExtractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(request);

        var folder = new DirectoryInfo(Path.Combine(workspaces.FullName, Guid.NewGuid().ToString()));
        try
        {
            folder.Create();

            var prompt = new StringBuilder();
            WriteText(folder, prompt, "instructions.md", request.Instructions);
            WriteText(folder, prompt, "schema.md", request.Schema.Document);
            WriteBytes(folder, prompt, "meeting.json", request.Input.Bytes());

            return new Written(folder, prompt.ToString());
        }
        catch
        {
            // A run never started over a folder this half-built: the deletion ClaudeCodeSummaries
            // otherwise owns for a folder a run actually used never runs for one that failed before
            // it got that far, because there is no Written to hand back and put in its finally. So
            // this catches for itself, best-effort the same way, and lets the original exception go.
            TryDelete(folder);
            throw;
        }
    }

    private static void TryDelete(DirectoryInfo folder)
    {
        try
        {
            if (Directory.Exists(folder.FullName))
            {
                Directory.Delete(folder.FullName, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteText(DirectoryInfo folder, StringBuilder prompt, string name, string content)
    {
        File.WriteAllText(Path.Combine(folder.FullName, name), content, NoBom);
        Append(prompt, name, content);
    }

    private static void WriteBytes(DirectoryInfo folder, StringBuilder prompt, string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(folder.FullName, name), content);
        Append(prompt, name, NoBom.GetString(content));
    }

    /// <summary>One file's block of the prompt: the header line Decides 5 asks for, the file's own
    /// content, and the one line break that follows it before the next file's header.</summary>
    private static void Append(StringBuilder prompt, string name, string content) =>
        prompt.Append("--- ").Append(name).Append(" ---\n").Append(content).Append('\n');
}
