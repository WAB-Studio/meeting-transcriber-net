using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingTranscriber.CorpusFixtures;

/// <summary>
/// Builds the committed Deepgram fixtures out of the paid responses of the Python corpus.
/// </summary>
/// <remarks>
/// One way, and it never writes into the corpus. It only exists so the fixtures can be rebuilt
/// and extended: what was done to a response the user paid for has to be readable and repeatable,
/// not a manual edit nobody can check afterwards.
/// </remarks>
internal static class Program
{
    private static readonly Recipe[] Recipes =
    [
        new("two-channel-long", "2026-07-29 09-35-15", Shape.AsRecorded),
        new("two-channel-short", "2026-08-03 08-13-17", Shape.AsRecorded),

        // This meeting and no other: six diarized speakers share its meeting channel, and a one
        // track fixture whose every turn is speaker 0 would not be a diarized one.
        new("single-track-diarized", "2026-08-04 13-23-25", Shape.MeetingChannelOnly),
        new("two-channel-silent-me", "2026-08-03 13-23-59", Shape.SilentUserChannel),
    ];

    private static readonly JsonSerializerOptions Compact = new()
    {
        // The provider sends accented text as itself, and a fixture that escaped it would be
        // testing this tool's encoder rather than the corpus.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: dotnet run --project tools/MeetingTranscriber.CorpusFixtures -- <corpus-directory>");
            return 2;
        }

        var corpus = new DirectoryInfo(args[0]);
        if (!corpus.Exists)
        {
            Console.Error.WriteLine($"There is no corpus at '{corpus.FullName}'.");
            return 1;
        }

        var fixtures = FixtureDirectory();
        var anonymizer = new Anonymizer(Vocabulary.Load(Path.Combine(fixtures.FullName, "vocabulary.txt")));

        foreach (var recipe in Recipes)
        {
            var source = new FileInfo(Path.Combine(corpus.FullName, recipe.Meeting, "deepgram.json"));
            if (!source.Exists)
            {
                Console.Error.WriteLine($"'{recipe.Meeting}' is not in this corpus, so '{recipe.Name}' was left alone.");
                return 1;
            }

            var response = Read(source);
            Reshape(response, recipe.Shape);
            anonymizer.Scrub(response);

            var target = Path.Combine(fixtures.FullName, recipe.Name + ".json");
            File.WriteAllText(target, JsonSerializer.Serialize(response, Compact), new UTF8Encoding(false));
            Console.WriteLine($"{recipe.Name,-24} {Describe(response)}");
        }

        return 0;
    }

    private static JsonObject Read(FileInfo source)
    {
        using var stream = source.OpenRead();
        return JsonNode.Parse(stream)?.AsObject()
            ?? throw new InvalidOperationException($"'{source.FullName}' is not a Deepgram response.");
    }

    private static void Reshape(JsonObject response, Shape shape)
    {
        if (shape is Shape.AsRecorded)
        {
            return;
        }

        var results = response["results"]!.AsObject();
        var channels = results["channels"]!.AsArray();

        if (shape is Shape.MeetingChannelOnly)
        {
            // What a single track request answers: one channel, its own paragraphs, and no
            // aggregate across channels because there is nothing to aggregate.
            for (var index = channels.Count - 1; index > 0; index--)
            {
                channels.RemoveAt(index);
            }

            response["metadata"]!["channels"] = 1;
            results.Remove("paragraphs");
        }

        if (shape is Shape.SilentUserChannel)
        {
            var silent = channels[1]!["alternatives"]!.AsArray()[0]!.AsObject();
            silent["transcript"] = string.Empty;
            silent["confidence"] = 0;
            silent["words"] = new JsonArray();
            silent.Remove("paragraphs");

            var paragraphs = results["paragraphs"]!.AsObject();
            Drop(paragraphs["paragraphs"]!.AsArray(), paragraph => Channel(paragraph) != 0);
            paragraphs["transcript"] = string.Join(
                "\n\n",
                paragraphs["transcript"]!.GetValue<string>()
                    .Split("\n\n")
                    .Where(block => !block.TrimStart('\n').StartsWith("Channel 1:", StringComparison.Ordinal)));
        }

        Drop(results["utterances"]!.AsArray(), utterance => Channel(utterance) != 0);
    }

    private static void Drop(JsonArray array, Func<JsonNode?, bool> unwanted)
    {
        for (var index = array.Count - 1; index >= 0; index--)
        {
            if (unwanted(array[index]))
            {
                array.RemoveAt(index);
            }
        }
    }

    private static int? Channel(JsonNode? node) => node?["channel"]?.GetValue<int>();

    private static string Describe(JsonObject response)
    {
        var results = response["results"]!.AsObject();
        var words = results["channels"]!.AsArray()
            .Select(channel => channel!["alternatives"]!.AsArray()[0]!["words"]!.AsArray().Count);

        return $"{response["metadata"]!["duration"]} s, "
            + $"channels [{string.Join(", ", words)}] words, "
            + $"{results["utterances"]!.AsArray().Count} utterances";
    }

    /// <summary>
    /// The fixtures, found from this file rather than from the working directory: the tool is run
    /// by hand, and a relative path would write the corpus into wherever it was run from.
    /// </summary>
    private static DirectoryInfo FixtureDirectory([CallerFilePath] string thisFile = "") => new(
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "tests", "fixtures", "deepgram")));

    private enum Shape
    {
        /// <summary>Two channels, exactly as the meeting was recorded and transcribed.</summary>
        AsRecorded,

        /// <summary>The meeting channel on its own, as a one track diarized response.</summary>
        MeetingChannelOnly,

        /// <summary>Two channels, with the user's microphone having caught nothing.</summary>
        SilentUserChannel,
    }

    private sealed record Recipe(string Name, string Meeting, Shape Shape);
}
