using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.CorpusImport;

/// <summary>
/// Reads a Python corpus into a .NET one. Run by hand, once per machine that has an old corpus,
/// and then deleted whole — README.md in this folder says what deleting it means.
/// </summary>
internal static class Program
{
    private const string Usage = """
        usage: corpus-import <python-corpus> --corpus <directory> [options]

          --language <code>    the language of a meeting whose rendered transcript does not
                               say (default: es)

        The sources are copied into the corpus, which is made if it is not there. The Python
        corpus it reads is only ever read, so it may not be the corpus written into.
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
        DirectoryInfo? destination = null;
        var language = "es";

        for (var index = 1; index < args.Length; index++)
        {
            // An option's value is never another option. Without this, '--corpus --language es'
            // makes a corpus in a directory called '--language' and says nothing about it.
            var value = index + 1 < args.Length && !args[index + 1].StartsWith('-')
                ? args[index + 1]
                : null;

            switch (args[index])
            {
                case "--corpus" when value is not null:
                    destination = new DirectoryInfo(value);
                    index++;
                    break;
                case "--language" when value is not null:
                    language = value;
                    index++;
                    break;
                default:
                    Console.Error.WriteLine($"'{args[index]}' is not an option, or is missing its value.");
                    return 2;
            }
        }

        if (!source.Exists)
        {
            Console.Error.WriteLine($"There is no corpus at '{source.FullName}'.");
            return 1;
        }

        if (destination is null)
        {
            Console.Error.WriteLine("--corpus is needed: the folder of the corpus to import into.");
            return 2;
        }

        // One way, and this is where that is enforced rather than assumed. The corpus written into
        // is a database and a folder of copies, so naming the Python corpus as the destination
        // would put both inside the thing this tool promises only to read.
        if (Inside(destination, source))
        {
            Console.Error.WriteLine(
                $"'{destination.FullName}' is inside the corpus being read. This tool never writes "
                + "into the Python corpus, so the corpus it imports into has to be somewhere else.");
            return 2;
        }

        // The corpus is a folder, and one flag names it. It used to be two — a database and a
        // folder to copy into — which meant a run could write rows into one corpus and files into
        // another, and both halves would report success.
        destination.Create();

        using var context = CorpusDatabase.OpenMigrated(destination);
        var importer = new CorpusImporter(context, TimeProvider.System);
        var report = importer.Import(new LegacyCorpus(source), new ImportOptions(language));

        Console.Write(report.ToString());
        return 0;
    }

    /// <summary>Whether one folder is the other or sits under it, however each was spelled.</summary>
    private static bool Inside(DirectoryInfo folder, DirectoryInfo other)
    {
        var under = Path.TrimEndingDirectorySeparator(folder.FullName);
        var outer = Path.TrimEndingDirectorySeparator(other.FullName);

        return under.Equals(outer, StringComparison.OrdinalIgnoreCase)
            || under.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
