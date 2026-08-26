using System.IO;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Starts this application, does what it is told to its windows, writes down what they said, and
/// closes it.
/// </summary>
/// <remarks>
/// <para>
/// It exists because everything else that checks a screen reads the screen's source. Source says
/// what a screen was written to do; only a running window says what it does — that it opened at
/// all, that the button is alive, that the second screen is reached by pressing the first.
/// </para>
/// <para>
/// It needs a desktop somebody is logged into, so it is run by hand and never by a build. Nothing
/// under <c>src/</c> or <c>tests/</c> knows it exists. <c>docs/ui-probe.md</c> is how to use it.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>The script was wrong. Nothing was started and nothing was written.</summary>
    private const int BadScript = 2;

    /// <summary>The screen was wrong, or the application was. This is a finding.</summary>
    private const int BadScreen = 1;

    /// <summary>The probe itself broke. This is a bug in here, not a finding about a screen.</summary>
    private const int BadProbe = 3;

    private const string Usage = """
        usage: dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...

          see <name>          write <name>.tree.txt and <name>.png of the screen
          press <element>     do to it what pressing it does
          choose <list> <item>  pick a named thing out of a list
          wait <element>      stop until it is on a screen, and make that screen the one the
                              rest of the script is about

        An element is named by the x:Name the XAML gave it, or by the words on it. A press whose
        effect is about to be photographed needs a wait after it — that is the only thing here
        that synchronises. For example, opening the application and walking to the meetings:

          --out probe see recorder press MeetingsButton wait RefreshButton see meetings

        Exit: 0 it ran, 1 the screen or the application failed it, 2 the script was wrong,
        3 the probe broke.
        """;

    private static int Main(string[] args)
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
            // escapes Main terminates the process from the throw point, so the `finally` inside
            // Walk that closes the application would not run and the window would be left open.
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
        var repository = RepositoryRoot();
        var manifest = Path.Combine(repository, "src", "MeetingTranscriber.App", "Package.appxmanifest");
        if (!File.Exists(manifest))
        {
            throw new ProbeFailed($"There is no package manifest at {manifest}.");
        }

        // Before any window is measured. Without it Windows reports this process a made-up
        // desktop, and every picture comes out cropped on a display that is not at 100%.
        if (!Native.SetProcessDpiAwarenessContext(Native.PerMonitorAwareV2))
        {
            throw new ProbeFailed(
                "Windows would not let this process see real pixels, so every picture it took "
                + "would be the wrong size on any display that is not at 100%.");
        }

        using var app = LaunchedApp.Start(manifest);
        Console.WriteLine($"{app.AppUserModelId} is process {app.ProcessId}, from {app.RunningFrom}");

        // Before a single artifact is written. A tree of the wrong build is read as a tree.
        Freshness.MustNotPredate(manifest, app.RunningFrom);
        app.OpenAWindow();

        var session = new Session(app, folder, Console.Out);
        foreach (var step in script)
        {
            session.Do(step);
        }

        Console.WriteLine($"done, in {Path.GetFullPath(folder)}");
    }

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

    private static string RepositoryRoot()
    {
        var here = new DirectoryInfo(Environment.CurrentDirectory);
        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "MeetingTranscriber.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new ProbeFailed(
            $"{Environment.CurrentDirectory} is not inside the repository, so there is no "
            + "application to start. Run it from the repository.");
    }
}
