using System.IO;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// One host over <see cref="Session"/>: a whole script, in one process, against an application
/// that is started and closed around it.
/// </summary>
/// <remarks>
/// <para>
/// The other is <see cref="McpHost"/>, and <see cref="Program"/> says why both. What this one is
/// for is a finding that can be repeated: a walk written on one line, pasted into a pull request,
/// run again by somebody who was not there, and answering with an exit code a script can branch on.
/// </para>
/// <para>
/// The artifacts are files here and only here. The core hands back a tree and a picture; naming
/// them, emptying the folder they go in and printing where they went are this host's, because they
/// are what a script leaves behind and a turn does not.
/// </para>
/// </remarks>
internal static class CommandLine
{
    /// <summary>The script was wrong. Nothing was started and nothing was written.</summary>
    private const int BadScript = 2;

    /// <summary>The screen was wrong, or the application was. This is a finding.</summary>
    private const int BadScreen = 1;

    /// <summary>The probe itself broke. This is a bug in here, not a finding about a screen.</summary>
    internal const int BadProbe = 3;

    private const string Usage = """
        usage: dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...
               dotnet run --project tools/MeetingTranscriber.UiProbe -- --mcp

          see <name>          write <name>.tree.txt and <name>.png of the screen
          press <element>     do to it what pressing it does
          type <element> <text>  put text in a field
          choose <list> <item>  pick a named thing out of a list
          wait <element>      stop until it is on a screen, and make that screen the one the
                              rest of the script is about

        An element is named by the x:Name the XAML gave it, or by the words on it. A press whose
        effect is about to be photographed needs a wait after it — that is the only thing here
        that synchronises. For example, opening the application and walking to the meetings:

          --out probe see recorder press MeetingsButton wait RefreshButton see meetings

        --mcp serves the same verbs to an agent over the Model Context Protocol instead, holding
        one application open across turns. See docs/ui-probe.md.

        Exit: 0 it ran, 1 the screen or the application failed it, 2 the script was wrong,
        3 the probe broke.
        """;

    internal static int Run(string[] args)
    {
        string outFolder;
        IReadOnlyList<Instruction> script;
        try
        {
            if (!Ready(args, out outFolder, out script))
            {
                Console.Error.WriteLine(Usage);
                return BadScript;
            }
        }
        catch (ProbeFailed wrong)
        {
            Console.Error.WriteLine(wrong.Message);
            return BadScript;
        }

        try
        {
            Walk(outFolder, script);
            return 0;
        }
        catch (ProbeFailed failure)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(failure.Message);
            return BadScreen;
        }
        catch (Exception broke)
        {
            // Caught rather than left unhandled, and that is not tidiness: an exception that
            // escapes Main terminates the process from the throw point, so the `using` in Walk
            // that closes the application would not run and the window would be left open.
            Console.Error.WriteLine();
            Console.Error.WriteLine($"The probe broke rather than finding anything: {broke}");
            return BadProbe;
        }
    }

    private static bool Ready(string[] args, out string outFolder, out IReadOnlyList<Instruction> script)
    {
        outFolder = string.Empty;
        script = [];

        string? folder = null;
        var words = new List<string>();

        for (var at = 0; at < args.Length; at++)
        {
            switch (args[at])
            {
                case "--out":
                    folder = Next(args, ref at);
                    break;
                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return false;
                default:
                    words.Add(args[at]);
                    break;
            }
        }

        if (folder is null || words.Count == 0)
        {
            return false;
        }

        outFolder = folder;
        script = Instruction.Read(words);

        // Here rather than in Walk, so that "you pointed --out somewhere wrong" comes back as the
        // script being wrong, which is what it is, and nothing has been started when it does.
        Clear(folder);

        return true;
    }

    private static void Walk(string folder, IReadOnlyList<Instruction> script)
    {
        using var session = Session.Open();
        Console.WriteLine(session.StartedAs);

        foreach (var step in script)
        {
            Console.WriteLine($"  {step}");
            Do(session, folder, step);
        }

        Console.WriteLine($"done, in {Path.GetFullPath(folder)}");
    }

    private static void Do(Session session, string folder, Instruction step)
    {
        switch (step.Verb)
        {
            case Verb.See:
                Keep(folder, step.Subject, session.See());
                break;
            case Verb.Press:
                session.Press(step.Subject);
                break;
            case Verb.Type:
                session.Type(step.Subject, step.Detail);
                break;
            case Verb.Choose:
                session.Choose(step.Subject, step.Detail);
                break;
            case Verb.Wait:
                Console.WriteLine($"    on \"{session.Wait(step.Subject)}\"");
                break;
            default:
                // Not unreachable, and that is the point: a sixth verb wired into `Session` and
                // into the server and forgotten here would otherwise parse, print, do nothing and
                // exit 0 saying "done".
                throw new ProbeFailed($"\"{step.Verb}\" is not wired into the command line.");
        }
    }

    private static void Keep(string folder, string name, Screen screen)
    {
        var called = Named(name);

        var picture = Path.Combine(folder, $"{called}.png");
        File.WriteAllBytes(picture, screen.Picture);

        var tree = Path.Combine(folder, $"{called}.tree.txt");
        File.WriteAllText(tree, screen.Tree);

        Console.WriteLine(
            $"    {Path.GetFileName(tree)} and {Path.GetFileName(picture)} ({screen.Size})");
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

    private static string Next(string[] args, ref int at)
    {
        at++;
        return at < args.Length
            ? args[at]
            : throw new ProbeFailed($"{args[at - 1]} wants a value after it.");
    }

    /// <summary>
    /// Emptied of what a run writes, so nothing a previous run left can be read as this one's — a
    /// tree from yesterday looks exactly like a tree from a minute ago. And only ever a folder
    /// that holds nothing else, because the path comes off a command line and deleting every
    /// picture in a folder somebody chose badly is not a thing to be casual about.
    /// </summary>
    private static void Clear(string folder)
    {
        Directory.CreateDirectory(folder);

        var artifacts = new List<string>();
        foreach (var there in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Directory.Exists(there)
                || !(there.EndsWith(".tree.txt", StringComparison.OrdinalIgnoreCase)
                    || there.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ProbeFailed(
                    $"{Path.GetFullPath(folder)} holds {Path.GetFileName(there)}, which is not "
                    + "something a probe wrote. Point --out at a folder of its own.");
            }

            artifacts.Add(there);
        }

        artifacts.ForEach(File.Delete);
    }
}
