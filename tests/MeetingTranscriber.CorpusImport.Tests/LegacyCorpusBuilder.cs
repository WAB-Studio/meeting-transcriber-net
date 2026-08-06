using System.Globalization;
using System.Text;

namespace MeetingTranscriber.CorpusImport.Tests;

/// <summary>
/// A Python corpus on disk, built for one test. Not the user's: no test may need that one, and a
/// corpus written here can hold the cases a real one happens not to have.
/// </summary>
public sealed class LegacyCorpusBuilder : IDisposable
{
    private readonly DirectoryInfo root = System.IO.Directory.CreateTempSubdirectory("legacy-corpus-");

    public DirectoryInfo Directory => root;

    public LegacyCorpusBuilder WithCatalog(string yaml = DefaultCatalog)
    {
        Write("catalog.yaml", yaml);
        return this;
    }

    public LegacyCorpusBuilder WithCorrections(string yaml = DefaultCorrections)
    {
        Write("corrections.yaml", yaml);
        return this;
    }

    /// <summary>A meeting folder with a response in it, and whatever else the test asked for.</summary>
    public LegacyCorpusBuilder WithMeeting(
        string id,
        int channels = 2,
        double seconds = 1800.5,
        string? meta = DefaultMeta,
        bool extraction = true,
        bool transcript = true)
    {
        Write($"{id}/deepgram.json", Response(channels, seconds));

        if (meta is not null)
        {
            Write($"{id}/meta.yaml", meta);
        }

        if (extraction)
        {
            Write($"{id}/extraction.json", """{"response": {"abstract": "something happened"}}""");
        }

        if (transcript)
        {
            Write($"{id}/transcript.md", "---\nsource: x.mkv\nlanguage: es\nchannels: 2\n---\n\n# x\n");
            Write($"{id}/utterances.jsonl", """{"i": 0, "text": "hola"}""" + "\n");
        }

        return this;
    }

    /// <summary>Every file in the corpus with the hash it has right now.</summary>
    public IReadOnlyDictionary<string, string> Fingerprint()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            using var stream = file.OpenRead();
            files[Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/')] =
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
        }

        return files;
    }

    public void Dispose()
    {
        try
        {
            root.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is noise, not a failed test.
        }
    }

    private void Write(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content, new UTF8Encoding(false));
    }

    private static string Response(int channels, double seconds)
    {
        var duration = seconds.ToString("0.0###", CultureInfo.InvariantCulture);
        var alternatives = string.Join(
            ",",
            Enumerable.Range(0, channels).Select(channel => $$"""
                {"alternatives":[{"transcript":"turno {{channel}}","confidence":0.9,"words":[]}]}
                """));

        // Three dollars, because the response ends on two closing braces and two is what an
        // interpolation would take.
        return $$$"""
            {"metadata":{"transaction_key":"deprecated","request_id":"r","sha256":"s","created":"2026-01-01T00:00:00.000Z","duration":{{{duration}}},"channels":{{{channels}}},"models":["m"]},"results":{"channels":[{{{alternatives}}}],"utterances":[]}}
            """;
    }

    public const string DefaultCatalog = """
        companies:
          acme:
            name: Acme
        projects:
          orchard:
            name: orchard
            company: acme
        people:
          rene:
            name: Renée
            company: acme
          sam:
            name: Sam
        """;

    public const string DefaultCorrections = """
        version: 1
        terms:
          orchard-co:
            canonical: Orchard Co
            kind: company
            aliases:
            - Orchard Company
            - orchard co.
            active: true
          retired:
            canonical: Nobody
            aliases:
            - nadie
            active: false
        """;

    public const string DefaultMeta = """
        company: "acme"
        project: "orchard"
        meeting_type: "review"
        title: "the one about the orchard"
        speakers:
          # Speaker 1 says: something
          Speaker 1: rene
          Speaker 2:
        """;
}
