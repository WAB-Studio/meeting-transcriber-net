using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingTranscriber.CorpusFixtures;

/// <summary>
/// Replaces everything a Deepgram response says while keeping everything it measures.
/// </summary>
/// <remarks>
/// Word for word: every token becomes a <see cref="Vocabulary"/> word, and the timings, the
/// confidences, the channel and speaker numbers, the word counts and the ordering are left
/// exactly as the provider returned them. That is the half of the response the tests are for.
/// Numbers stay untouched as raw JSON text, so a fixture round trips through this tool without
/// a single digit shifting.
/// </remarks>
internal sealed partial class Anonymizer(Vocabulary vocabulary)
{
    /// <summary>What the request that was paid for is replaced by. Both identify the call.</summary>
    public const string RequestId = "00000000-0000-0000-0000-000000000000";

    public const string AudioSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The date of the call, normalised. When the user met somebody is not test data.</summary>
    public const string Created = "2020-01-01T00:00:00.000Z";

    public void Scrub(JsonObject response)
    {
        if (response["metadata"] is JsonObject metadata)
        {
            metadata["request_id"] = RequestId;
            metadata["sha256"] = AudioSha256;
            metadata["created"] = Created;
        }

        if (response["results"] is not JsonObject results)
        {
            return;
        }

        foreach (var channel in results["channels"]?.AsArray() ?? [])
        {
            foreach (var alternative in channel?["alternatives"]?.AsArray() ?? [])
            {
                ScrubAlternative(alternative?.AsObject());
            }
        }

        foreach (var utterance in results["utterances"]?.AsArray() ?? [])
        {
            ScrubSpokenText(utterance?.AsObject(), "transcript");
            ScrubWords(utterance?["words"]?.AsArray());
        }

        ScrubParagraphs(results["paragraphs"]?.AsObject());
    }

    /// <summary>
    /// The text of a token with its shape kept: the punctuation around it, and whether it was
    /// capitalised. A transcript that lost its sentence casing would not be one to render.
    /// </summary>
    public string ScrubToken(string token)
    {
        var start = 0;
        while (start < token.Length && !char.IsLetterOrDigit(token[start]))
        {
            start++;
        }

        var end = token.Length;
        while (end > start && !char.IsLetterOrDigit(token[end - 1]))
        {
            end--;
        }

        if (start == end)
        {
            return token;
        }

        var core = token[start..end];
        return string.Concat(token[..start], MatchCase(core, vocabulary.WordFor(core)), token[end..]);
    }

    [GeneratedRegex(@"^(Channel \d+: )?(Speaker \d+: )?")]
    private static partial Regex SpeakerMarker { get; }

    private static string MatchCase(string source, string replacement)
    {
        if (source.Length > 1 && source.All(character => !char.IsLetter(character) || char.IsUpper(character)))
        {
            return replacement.ToUpperInvariant();
        }

        return char.IsUpper(source[0])
            ? string.Concat(char.ToUpperInvariant(replacement[0]).ToString(), replacement[1..])
            : replacement;
    }

    private void ScrubAlternative(JsonObject? alternative)
    {
        if (alternative is null)
        {
            return;
        }

        ScrubSpokenText(alternative, "transcript");
        ScrubWords(alternative["words"]?.AsArray());
        ScrubParagraphs(alternative["paragraphs"]?.AsObject());
    }

    private void ScrubParagraphs(JsonObject? paragraphs)
    {
        if (paragraphs is null)
        {
            return;
        }

        ScrubSpokenText(paragraphs, "transcript");
        foreach (var paragraph in paragraphs["paragraphs"]?.AsArray() ?? [])
        {
            foreach (var sentence in paragraph?["sentences"]?.AsArray() ?? [])
            {
                ScrubSpokenText(sentence?.AsObject(), "text");
            }
        }
    }

    private void ScrubWords(JsonArray? words)
    {
        foreach (var word in words ?? [])
        {
            ScrubSpokenText(word?.AsObject(), "word");
            ScrubSpokenText(word?.AsObject(), "punctuated_word");
        }
    }

    private void ScrubSpokenText(JsonObject? holder, string property)
    {
        if (holder?[property] is not JsonValue value || value.GetValueKind() is not System.Text.Json.JsonValueKind.String)
        {
            return;
        }

        holder[property] = ScrubLines(value.GetValue<string>());
    }

    /// <summary>
    /// Substitutes a whole passage. The <c>Channel 0: Speaker 1:</c> a paragraph transcript opens
    /// a line with is structure, not speech, and survives: replacing it would leave the fixture
    /// saying nothing about which channel a paragraph came from.
    /// </summary>
    private string ScrubLines(string text) => string.Join('\n', text.Split('\n').Select(ScrubLine));

    private string ScrubLine(string line)
    {
        var marker = SpeakerMarker.Match(line).Length;
        return string.Concat(
            line[..marker],
            string.Join(' ', line[marker..].Split(' ').Select(ScrubToken)));
    }
}
