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
    /// <param name="response">
    /// The response bytes, when a test needs ones this would not have written. Nothing on the way
    /// in reads a <c>deepgram.json</c> past its metadata — it is copied and hashed — so a legacy
    /// corpus really does hold responses only the renderer ever finds out about, and this is the
    /// only way to put one of them in front of the importer.
    /// </param>
    public LegacyCorpusBuilder WithMeeting(
        string id,
        int channels = 2,
        double seconds = 1800.5,
        string? meta = DefaultMeta,
        string? extraction = DefaultExtraction,
        bool transcript = true,
        string? response = null)
    {
        Write($"{id}/deepgram.json", response ?? Response(id, channels, seconds));

        if (meta is not null)
        {
            Write($"{id}/meta.yaml", meta);
        }

        if (extraction is not null)
        {
            Write($"{id}/extraction.json", extraction);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives the run is noise, not a failed test, and Windows
            // refuses a contended delete as access denied as readily as a sharing violation.
        }
    }

    private void Write(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content, new UTF8Encoding(false));
    }

    /// <summary>
    /// A response carrying its own request id, as a real one does. Two meetings never come back
    /// with the same bytes, and a fixture that pretended otherwise would be testing a case the
    /// importer is right to treat as one meeting.
    /// </summary>
    /// <remarks>
    /// Public so a test that needs a response this would not have written can say what is wrong
    /// with one in a line, rather than spelling out a second response that would drift from this.
    /// </remarks>
    public static string Response(string requestId, int channels = 2, double seconds = 1800.5)
    {
        var duration = seconds.ToString("0.0###", CultureInfo.InvariantCulture);
        var alternatives = string.Join(
            ",",
            Enumerable.Range(0, channels).Select(channel => $$"""
                {"alternatives":[{"transcript":"turno {{channel}}","confidence":0.9,"words":[]}]}
                """));

        // Two utterances a channel, so a rendered transcript has turns in it rather than being an
        // empty file that any renderer would produce.
        var utterances = string.Join(
            ",",
            Enumerable.Range(0, channels).SelectMany(channel => Enumerable.Range(0, 2).Select(index => $$"""
                {"channel":{{channel}},"start":{{(channel * 10) + (index * 4)}}.0,"end":{{(channel * 10) + (index * 4)}}.5,"speaker":0,"confidence":0.9,"transcript":"turno {{channel}}{{index}} de quati"}
                """)));

        // Three dollars, because the response ends on two closing braces and two is what an
        // interpolation would take.
        return $$$"""
            {"metadata":{"transaction_key":"deprecated","request_id":"{{{requestId}}}","sha256":"s","created":"2026-01-01T00:00:00.000Z","duration":{{{duration}}},"channels":{{{channels}}},"models":["m"]},"results":{"channels":[{{{alternatives}}}],"utterances":[{{{utterances}}}]}}
            """;
    }

    /// <summary>
    /// An extraction as the Python system wrote one: the model that answered, the git description
    /// of the skill that prompted it, the Claude Code session it came out of, and a naive local
    /// timestamp. There is no schema version in it, and there never was in a real one either.
    /// </summary>
    public const string DefaultExtraction = """
        {
          "skill_version": "31d0d27-dirty",
          "model": "claude-opus-5[1m]",
          "session_id": "02132d9f-69ba-47c3-ab5b-ca6b4c739408",
          "extracted_at": "2026-07-29T10:58:55",
          "response": {"abstract": "something happened", "decisions": [], "actions": []}
        }
        """;

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
