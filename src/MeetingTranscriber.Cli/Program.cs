using MeetingTranscriber.Infrastructure.Import;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The command line, which arquitectura.md §3 gives diagnosis, import, rebuild and recovery. It
/// shares the application's own services rather than implementing a second pipeline; today the
/// only command is the one that reads the Python corpus.
/// </summary>
internal static class Program
{
    private const string Usage = """
        usage: meeting-transcriber import <corpus-directory> --database <corpus.db> [options]

          --copy <directory>   copy the sources into this corpus instead of pointing at
                               where they already are
          --language <code>    the language of a meeting whose rendered transcript does not
                               say (default: es)

        The legacy corpus is only ever read.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        if (args[0] is not "import")
        {
            Console.Error.WriteLine($"'{args[0]}' is not a command.");
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            return Import(args[1..]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Import(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("import needs the directory of the corpus to read.");
            return 2;
        }

        var source = new DirectoryInfo(args[0]);
        string? database = null;
        DirectoryInfo? copyTo = null;
        var language = "es";

        for (var index = 1; index < args.Length; index++)
        {
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (args[index])
            {
                case "--database" when value is not null:
                    database = value;
                    index++;
                    break;
                case "--copy" when value is not null:
                    copyTo = new DirectoryInfo(value);
                    index++;
                    break;
                case "--language" when value is not null:
                    language = value;
                    index++;
                    break;
                default:
                    Console.Error.WriteLine($"'{args[index]}' is not an option of import.");
                    return 2;
            }
        }

        if (!source.Exists)
        {
            Console.Error.WriteLine($"There is no corpus at '{source.FullName}'.");
            return 1;
        }

        if (database is null)
        {
            Console.Error.WriteLine("import needs --database, the corpus to import into.");
            return 2;
        }

        using var context = CorpusDatabase.OpenMigrated(database);
        var importer = new CorpusImporter(context, TimeProvider.System);
        var report = importer.Import(new LegacyCorpus(source), new ImportOptions(copyTo, language));

        Console.Write(report.ToString());
        return 0;
    }
}
