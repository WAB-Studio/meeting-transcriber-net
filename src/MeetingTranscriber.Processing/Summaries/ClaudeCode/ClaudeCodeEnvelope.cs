using System.Text;
using System.Text.Json;

namespace MeetingTranscriber.Processing.Summaries.ClaudeCode;

/// <summary>
/// What Claude Code's <c>--output-format json</c> wraps a run's answer in, and the one place that
/// knows the wrapping — nothing past this file ever reads <c>"is_error"</c>, <c>"subtype"</c> or
/// <c>"modelUsage"</c> again, and <c>ClaudeCodeEnvelopeTests.Only_the_adapter_knows_how_the_answer_is_wrapped</c>
/// holds that.
/// </summary>
/// <remarks>
/// <c>"session_id"</c> is not one of the guarded words: this batch's migration names a column
/// <c>session_id</c>, and a column that shares a name with an envelope key is not knowledge of the
/// envelope.
/// </remarks>
public static class ClaudeCodeEnvelope
{
    private static readonly UTF8Encoding NoBom = new(false);

    /// <summary>
    /// Reads one run's standard output as the envelope it has to be, or says what was missing and
    /// quotes as much of <c>"result"</c> as there was to quote.
    /// </summary>
    public static SummaryProviderAnswer Read(
        byte[] standardOutput, string providerVersion, string? modelAsked = null)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(providerVersion);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(standardOutput);
        }
        catch (JsonException)
        {
            return DidNotAnswer(null, "did not print one JSON object");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return DidNotAnswer(null, "did not print one JSON object");
            }

            if (!IsString(root, "type", "result"))
            {
                return DidNotAnswer(root, "\"type\" was not \"result\"");
            }

            if (!IsString(root, "subtype", "success"))
            {
                return DidNotAnswer(root, "\"subtype\" was not \"success\"");
            }

            if (!root.TryGetProperty("is_error", out var isError) || isError.ValueKind != JsonValueKind.False)
            {
                return DidNotAnswer(root, "\"is_error\" was not false");
            }

            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.String)
            {
                return DidNotAnswer(root, "carried no \"result\" string");
            }

            var output = Unwrap(result.GetString() ?? string.Empty);
            var sessionId = root.TryGetProperty("session_id", out var session)
                && session.ValueKind == JsonValueKind.String
                ? session.GetString()
                : null;

            return new SummaryProviderAnswer.Extracted(
                NoBom.GetBytes(output), providerVersion, ChooseModel(root, modelAsked ?? ClaudeCodeSummaries.Model), sessionId);
        }
    }

    /// <summary>
    /// Trims <paramref name="result"/> and, when it is one Markdown code fence and nothing else,
    /// drops the fence's first and last lines and keeps what is between them.
    /// </summary>
    private static string Unwrap(string result)
    {
        var trimmed = result.Trim();
        var lines = trimmed.Split('\n');
        if (lines.Length >= 2 && IsFenceLine(lines[0]) && IsFenceLine(lines[^1]))
        {
            return string.Join('\n', lines[1..^1]).Trim();
        }

        return trimmed;
    }

    private static bool IsFenceLine(string line) => line.Trim().StartsWith("```", StringComparison.Ordinal);

    /// <summary>
    /// The model that answered, chosen off <c>"modelUsage"</c>: the entry whose name contains
    /// <paramref name="asked"/> (<see cref="ClaudeCodeSummaries.Model"/> when the run asked for no
    /// other), otherwise the one with the most
    /// <c>"outputTokens"</c>, otherwise — with no <c>"modelUsage"</c> at all —
    /// <see cref="ClaudeCodeSummaries.Model"/> itself. The CLI bills housekeeping models in the same
    /// run, so the first key is not necessarily the one that answered.
    /// </summary>
    /// <remarks>
    /// An empty <c>"modelUsage"</c> object reads the same as none at all: the loop below has
    /// nothing to choose among either way, so both fall through to the same constant. Two entries
    /// tied on <c>"outputTokens"</c>, with neither name containing <see cref="ClaudeCodeSummaries.Model"/>,
    /// keep whichever the object listed first — a rule this envelope has never seen exercised, and
    /// names here so a tie reads as decided rather than as an accident of enumeration order.
    /// </remarks>
    private static string ChooseModel(JsonElement root, string asked)
    {
        if (!root.TryGetProperty("modelUsage", out var modelUsage) || modelUsage.ValueKind != JsonValueKind.Object)
        {
            return asked;
        }

        string? matching = null;
        string? mostTokens = null;
        var highestOutputTokens = -1L;

        foreach (var entry in modelUsage.EnumerateObject())
        {
            if (matching is null
                && entry.Name.Contains(asked, StringComparison.OrdinalIgnoreCase))
            {
                matching = entry.Name;
            }

            var tokens = entry.Value.ValueKind == JsonValueKind.Object
                && entry.Value.TryGetProperty("outputTokens", out var outputTokens)
                && outputTokens.ValueKind == JsonValueKind.Number
                && outputTokens.TryGetInt64(out var parsed)
                    ? parsed
                    : 0L;

            if (tokens > highestOutputTokens)
            {
                highestOutputTokens = tokens;
                mostTokens = entry.Name;
            }
        }

        return matching ?? mostTokens ?? asked;
    }

    private static bool IsString(JsonElement owner, string key, string expected) =>
        owner.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() == expected;

    /// <summary>
    /// <paramref name="missing"/> names what the envelope should have been and was not.
    /// <paramref name="root"/> is asked, when there is one, for a <c>"result"/&gt; whose first 200
    /// characters are quoted alongside it — the reason it did not answer, and what it said instead.
    /// </summary>
    private static SummaryProviderAnswer DidNotAnswer(JsonElement? root, string missing)
    {
        if (root is { } element
            && element.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.String)
        {
            var text = result.GetString() ?? string.Empty;
            var quoted = text.Length > 200 ? text[..200] : text;
            return new SummaryProviderAnswer.DidNotAnswer($"{missing}: \"{quoted}\"");
        }

        return new SummaryProviderAnswer.DidNotAnswer(missing);
    }
}
