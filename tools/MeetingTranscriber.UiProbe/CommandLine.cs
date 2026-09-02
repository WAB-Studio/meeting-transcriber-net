using System.Diagnostics;
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
/// <para>
/// Freshness is asked once, at <see cref="Session.Open"/>, and by no verb after it — where
/// <see cref="McpHost"/> asks <see cref="Session.MustStillBeUsable"/> every turn. That is the
/// difference between the two hosts and not an omission in this one. <see cref="Freshness"/> reads
/// the build stamp once and re-walks the sources on every call, so the only thing re-asking can
/// catch is a source file edited while the run is going — and a script is a fixed list handed to
/// one process, which edits nothing. The refusal would end a walk that had already spent six
/// minutes of real recording over an edit that changed nothing the window is showing, since what a
/// window can contain was settled when the process started. An agent over the server is itself the
/// one editing, which is why that host pays for it every turn.
/// </para>
/// <para>
/// What a caller gives up: a long script running through somebody else's edit says nothing about
/// it, so what a run's trees are evidence about is the commit it was started at and not the tree on
/// disk when it ended.
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

    /// <summary>
    /// How much of a <c>sleep</c> is slept at a time, so that a hold notices an application that
    /// has gone within a slice of it rather than at the end. Coarse enough that twenty minutes of
    /// holding costs twelve hundred cheap questions, and fine enough that a crash is reported
    /// while it is still news.
    /// </summary>
    private static readonly TimeSpan ASlice = TimeSpan.FromSeconds(1);

    private const string Usage = """
        usage: dotnet run --project tools/MeetingTranscriber.UiProbe -- --out <folder> <instruction>...
               dotnet run --project tools/MeetingTranscriber.UiProbe -- --mcp

          see <name>          write <name>.tree.txt and <name>.png of the screen
          press <element>     do to it what pressing it does
          type <element> <text>  put text in a field
          choose <list> <item>  pick a named thing out of a list
          wait <element>      stop until it is on a screen, and make that screen the one the
                              rest of the script is about
          sleep <seconds>     let that long pass, touching nothing
          kill                end the application the way a crash does

        An element is named by the x:Name the XAML gave it, or by the words on it. A press whose
        effect is about to be photographed needs a wait after it — that is the only thing here
        that synchronises. For example, opening the application and walking to the meetings:

          --out probe see recorder press MeetingsButton wait RefreshButton see meetings

        sleep is for the one screen that is a function of elapsed real time — a meeting running —
        because wait is capped at fifteen seconds and returns on the first frame that matches.
        kill is for what a start finds after a crash: nothing works after it, so it is a script's
        last instruction and the next run is what reads what it left behind. Give that run an --out
        of its own — a folder is emptied of trees and pictures when a run starts, so the second half
        of a two-run walk would take the first half's with it.

          --out crash choose MicrophonePicker fifine choose SourcePicker "Everything this machine
            plays" choose SpokenPicker English press RecordButton sleep 60 see running kill

        Record is disabled until the microphone, what channel 0 follows and what will be spoken
        have each been chosen, so a script that presses it says all three first.

        --mcp serves the same verbs to an agent over the Model Context Protocol instead, holding
        one application open across turns; there a turn is how time passes, so it has no sleep.
        See docs/ui-probe.md.

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
                See(session, folder, step.Subject);
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
            case Verb.Sleep:
                // The host's own and not the core's: an agent driving over the server lets time
                // pass by taking a turn, so a sleep on Session would be a verb only one of its two
                // hosts could ever mean.
                Hold(session, Instruction.Seconds(step.Subject));
                break;
            case Verb.Kill:
                session.Kill();
                break;
            case Verb.Wait:
                Console.WriteLine($"    on \"{session.Wait(step.Subject)}\"");
                break;
            default:
                // Not unreachable, and that is the point: a verb added to the enum and forgotten
                // here would otherwise parse, print, do nothing and exit 0 saying "done".
                throw new ProbeFailed($"\"{step.Verb}\" is not wired into the command line.");
        }
    }

    /// <summary>
    /// Lets the time pass, in slices, asking between them whether there is still an application to
    /// be holding a screen of.
    /// </summary>
    /// <remarks>
    /// One <c>Thread.Sleep</c> of the whole stretch was the first shape and it hid the one thing
    /// worth hearing about early: the application crashing at minute two of a twenty-minute hold
    /// went unnoticed until the next verb, so a run nobody is watching spent eighteen more minutes
    /// on a screen that had gone. Liveness only, and not <see cref="Session.MustStillBeUsable"/>:
    /// no verb of this host asks the freshness half, for the reason on the class, and a hold is not
    /// the exception to a rule this host has.
    /// </remarks>
    private static void Hold(Session session, TimeSpan wanted)
    {
        var spent = Stopwatch.StartNew();

        while (true)
        {
            if (session.HasGone)
            {
                throw new ProbeFailed(
                    $"The application went {spent.Elapsed.TotalSeconds:0} seconds into a "
                    + $"{wanted.TotalSeconds:0} second hold — it crashed, or something closed it.");
            }

            var left = wanted - spent.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                // Asked once more after the last slice rather than only between them. Every hold
                // written so far is a minute or less, which is one or two slices, so the tail was
                // most of the window — and a hold whose last verb is `kill` has no next verb to
                // notice for it.
                return;
            }

            Thread.Sleep(left < ASlice ? left : ASlice);
        }
    }

    /// <summary>
    /// Files both halves of a screen, or the half there was and the reason for the other.
    /// </summary>
    /// <remarks>
    /// The name was held to being a file name by <see cref="Instruction.Named"/>, before anything
    /// was started — so nothing here re-checks it, and a script that would have failed on one has
    /// not recorded a meeting first.
    /// </remarks>
    private static void See(Session session, string folder, string name)
    {
        try
        {
            Keep(folder, name, session.See());
        }
        catch (ScreenWouldNotBePhotographed noPicture)
        {
            var tree = Path.Combine(folder, $"{name}.tree.txt");
            File.WriteAllText(tree, noPicture.Tree);

            throw new ProbeFailed(
                $"{noPicture.Message} {Path.GetFileName(tree)} was written anyway — the tree read "
                + "whole, and only the picture is missing.");
        }
    }

    private static void Keep(string folder, string name, Screen screen)
    {
        var picture = Path.Combine(folder, $"{name}.png");
        File.WriteAllBytes(picture, screen.Picture);

        var tree = Path.Combine(folder, $"{name}.tree.txt");
        File.WriteAllText(tree, screen.Tree);

        Console.WriteLine(
            $"    {Path.GetFileName(tree)} and {Path.GetFileName(picture)} ({screen.Size})");
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
}
