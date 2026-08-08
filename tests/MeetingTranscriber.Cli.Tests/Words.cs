using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// A word to ask the corpus about, taken out of a turn of the meeting that was just filed.
/// </summary>
/// <remarks>
/// Out of <c>utterances.jsonl</c> rather than out of the vocabulary the fixtures are built from:
/// what is in a turn was said in this meeting, and what is in the vocabulary might only have been
/// said in another one — which is a query that comes back empty and a test that proves nothing.
/// </remarks>
public static class Words
{
    public static string SaidIn(FileInfo utterances)
    {
        ArgumentNullException.ThrowIfNull(utterances);

        foreach (var line in File.ReadAllLines(utterances.FullName))
        {
            using var turn = JsonDocument.Parse(line);
            var said = turn.RootElement.GetProperty("text").GetString() ?? string.Empty;

            // Latin letters only, and six of them: long enough to be a word somebody would search
            // for, and free of the accents whose folding is the index's business and not this
            // test's.
            if (Regex.Match(said, "[a-zA-Z]{6,}") is { Success: true } word)
            {
                return word.Value;
            }
        }

        throw new InvalidOperationException($"No turn in '{utterances.FullName}' has a word to search for.");
    }
}
