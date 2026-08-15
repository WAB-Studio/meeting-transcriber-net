using MeetingTranscriber.Audio;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Rendering;

using Microsoft.Data.Sqlite;

namespace MeetingTranscriber.Cli;

/// <summary>
/// A command that could not do what it was asked, where the reason is the corpus or the input
/// rather than the way the command was typed.
/// </summary>
public sealed class CommandException(string message) : Exception(message);

/// <summary>
/// The command line itself: what the commands are, which one was asked for, and what a failure
/// comes back as.
/// </summary>
/// <remarks>
/// <para>
/// Every command is a call into the same services the application uses — there is no second
/// pipeline here, and a diagnostic that read the corpus its own way would be a diagnostic of
/// something other than the corpus the application sees.
/// </para>
/// <para>
/// The entry point is this rather than <c>Main</c> so that a test drives the whole thing through
/// the argument strings a person would type, and reads what came back rather than a process's
/// output. <c>Main</c> is the two lines that hand it the console.
/// </para>
/// </remarks>
public static class Cli
{
    /// <summary>Everything went as asked.</summary>
    public const int Ok = 0;

    /// <summary>The corpus or the input said no. The message says which.</summary>
    public const int Refused = 1;

    /// <summary>The command line does not name something this program can do.</summary>
    public const int Misused = 2;

    private static readonly Command[] Commands =
    [
        new(
            "migrate",
            $"migrate {Corpus.Option} <directory>",
            "make the corpus if there is none and bring its schema up to this build",
            DiagnosticCommands.Migrate),
        new(
            "status",
            $"status {Corpus.Option} <directory>",
            "what the corpus holds",
            DiagnosticCommands.Status),
        new(
            "check",
            $"check {Corpus.Option} <directory> [--verify-contents]",
            "whether it is sound, and what the database and the disk disagree about",
            DiagnosticCommands.Check),
        new(
            "sweep",
            $"sweep {Corpus.Option} <directory>",
            "remove the writes that never finished, which were never artifacts",
            DiagnosticCommands.Sweep),
        new(
            "restore",
            $"restore <file> {Corpus.Option} <directory>",
            "put a file the corpus records and the disk has lost back, from bytes it already describes",
            DiagnosticCommands.Restore),
        new(
            "compact",
            $"compact {Corpus.Option} <directory>",
            "reclaim space and put the search indexes back, which is never one without the other",
            DiagnosticCommands.Compact),
        new(
            "import",
            $"import <{MeetingIntake.ResponseFileName}> {Corpus.Option} <directory> --started-at <instant>"
            + " --profile <multichannel|diarize> [--title <text>] [--context <text>] [--language <code>]",
            "file a paid response as a meeting and render everything derived from it",
            MeetingCommands.Import),
        new(
            "render",
            $"render <meeting-id> {Corpus.Option} <directory>",
            "produce one meeting's turns and files again from its response",
            MeetingCommands.Render),
        new(
            "rebuild",
            $"rebuild {Corpus.Option} <directory>",
            "the same for every meeting, then both search indexes",
            MeetingCommands.Rebuild),
        new(
            "devices",
            "devices",
            "what this machine can record from, under the names Windows gives them",
            AudioCommands.Devices),
        new(
            "capture",
            "capture --out <directory> --seconds <n> [--meeting <id>] [--microphone <name-or-id>]"
            + " [--process <name-or-pid>]",
            "record what the machine plays, or one program, and what the microphone hears at once",
            AudioCommands.Capture),
        new(
            "recordings",
            "recordings --spool <directory>",
            "which recordings nobody got to stop are waiting, and what each one is",
            AudioCommands.Recordings),
        new(
            "recover",
            "recover --in <directory> (--keep | --export <directory> | --discard)",
            "keep one of them as it stands, take its audio out, or throw it away",
            AudioCommands.Recover),
        new(
            "search",
            $"search <query> {Corpus.Option} <directory> [--limit <n>]",
            "ask the corpus, in the index's own query syntax",
            MeetingCommands.Search),
    ];

    /// <summary>Runs one command line and answers with the exit code it earned.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (arguments.Count == 0)
        {
            error.WriteLine(Usage());
            return Misused;
        }

        if (arguments[0] is "-h" or "--help" or "help")
        {
            output.WriteLine(Usage());
            return Ok;
        }

        try
        {
            var name = arguments[0];
            var command = Array.Find(Commands, candidate => candidate.Name == name)
                ?? throw new UsageException($"'{name}' is not a command.");

            return command.Run(Arguments.Parse(arguments.Skip(1)), output);
        }
        catch (UsageException misused)
        {
            error.WriteLine(misused.Message);
            error.WriteLine();
            error.WriteLine(Usage());
            return Misused;
        }
        catch (Exception refused) when (IsRefusal(refused))
        {
            error.WriteLine(refused.Message);
            return Refused;
        }
    }

    /// <summary>
    /// What this program does, built from the command table so a command cannot be added without
    /// appearing here.
    /// </summary>
    public static string Usage()
    {
        var lines = Commands.SelectMany(command => new[] { $"  {command.Usage}", $"      {command.Summary}" });

        return string.Join(
            Environment.NewLine,
            ["usage: meeting-transcriber <command> [options]", string.Empty, .. lines]);
    }

    /// <summary>
    /// The failures that are answers rather than defects: a corpus that is not there or not sound,
    /// a response that cannot be read, a meeting that cannot be rendered, a query the index
    /// refuses, a machine with no microphone to give, a disk that will not give the file up.
    /// Anything else is a bug and comes out as one.
    /// </summary>
    private static bool IsRefusal(Exception exception) => exception
        is CommandException
        or AudioCaptureException
        or CorpusIntegrityException
        or CorpusSearchException
        or IntakeException
        or RenderException
        or DeepgramResponseException
        or AudioContractException
        or ArtifactWriteException
        or ArtifactRestoreException
        or SqliteException
        or IOException
        or UnauthorizedAccessException;

    /// <summary>One command: how it is typed, what it does, and what runs it.</summary>
    private sealed record Command(
        string Name,
        string Usage,
        string Summary,
        Func<Arguments, TextWriter, int> Run);
}
