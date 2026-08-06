using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.CorpusImport;

/// <summary>
/// Reads a Python corpus into a .NET one. Run by hand, once per machine that has an old corpus,
/// and then deleted whole — README.md in this folder says what deleting it means.
/// </summary>
internal static class Program
{
    private const string Usage = """
        usage: corpus-import <corpus-directory> --database <corpus.db> [options]

          --copy <directory>   copy the sources into this corpus instead of pointing at
                               where they already are
          --language <code>    the language of a meeting whose rendered transcript does not
                               say (default: es)

        The corpus it reads is only ever read.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return Import(args);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Import(string[] args)
    {
        if (args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("The first argument is the directory of the corpus to read.");
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
                    Console.Error.WriteLine($"'{args[index]}' is not an option.");
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
            Console.Error.WriteLine("--database is needed: the corpus to import into.");
            return 2;
        }

        using var context = CorpusDatabase.OpenMigrated(database);
        var importer = new CorpusImporter(context, TimeProvider.System);
        var report = importer.Import(new LegacyCorpus(source), new ImportOptions(copyTo, language));

        Console.Write(report.ToString());
        return 0;
    }
}
