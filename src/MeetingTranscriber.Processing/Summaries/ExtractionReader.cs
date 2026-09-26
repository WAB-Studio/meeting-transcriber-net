using System.Globalization;
using System.Text.Json;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// Reads an extraction's raw output against the one shape schema version <c>"1"</c> is, and
/// reports every way it fails to be that shape rather than stopping at the first.
/// </summary>
/// <remarks>
/// <para>
/// This is the shape's only definition. There is no JSON Schema document beside it: a document a
/// person maintains and a validator that reads bytes are two definitions of one contract, and they
/// drift the first time somebody edits one and not the other. A provider that needs to be told the
/// shape is told by a document #119 generates from this reader, not the other way round.
/// </para>
/// <para>
/// Every refusal this produces carries <see cref="ExtractionCondition.NotTheSchema"/> and a
/// <c>null</c> statement: a shape problem is not about what a decision or a question said, it is
/// about the document not being readable as one. It never throws for malformed input — output that
/// is not JSON, or whose root is not an object, is one refusal at <c>$</c> — because a provider's
/// mistake is exactly the case this exists to report rather than to crash on.
/// </para>
/// <para>
/// A required field missing or of the wrong JSON kind is refused where it stands, and nothing
/// beneath it is asked about: an object that is not there has no fields of its own to be wrong, and
/// an array that is a string has no elements to walk. Refusals are collected in the order this
/// reader asks about each field — schema version, meeting id, abstract, summary, participants,
/// decisions, actions, then open questions, each before its own children — and a key the shape does
/// not name is reported last, in the order the document itself carries it.
/// </para>
/// </remarks>
public static class ExtractionReader
{
    /// <summary>The only schema version this build reads. Anything else is refused, not upgraded.</summary>
    public const string SchemaVersion = "1";

    /// <summary>What came back from reading one extraction's output: the document, or why not.</summary>
    /// <remarks><see cref="Document"/> is set exactly when <see cref="Refusals"/> is empty.</remarks>
    public sealed record ExtractionRead(ExtractionDocument? Document, IReadOnlyList<ExtractionRefusal> Refusals);

    private static readonly HashSet<string> TopLevelKeys =
    [
        "schema_version", "meeting_id", "abstract", "summary",
        "participants", "decisions", "actions", "open_questions",
    ];

    private static readonly HashSet<string> DecisionKeys = ["statement", "evidence"];
    private static readonly HashSet<string> ActionKeys = ["statement", "due_date", "evidence"];
    private static readonly HashSet<string> QuestionKeys = ["question", "evidence"];
    private static readonly HashSet<string> EvidenceKeys =
        ["utterance_ordinal", "start_ms", "end_ms", "speaker_label", "quoted_text"];

    public static ExtractionRead Read(ReadOnlySpan<byte> output)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output.ToArray());
        }
        catch (JsonException)
        {
            return new ExtractionRead(null, [NotTheSchema("$")]);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ExtractionRead(null, [NotTheSchema("$")]);
            }

            var refusals = new List<ExtractionRefusal>();
            var read = ReadDocument(document.RootElement, refusals);
            return refusals.Count == 0
                ? new ExtractionRead(read, [])
                : new ExtractionRead(null, refusals);
        }
    }

    private static ExtractionDocument ReadDocument(JsonElement root, List<ExtractionRefusal> refusals)
    {
        var schemaVersion = RequireSchemaVersion(root, refusals);
        var meetingId = RequireGuid(root, "meeting_id", "meeting_id", refusals);
        var abstractText = RequireNonBlankString(root, "abstract", "abstract", refusals);
        var summary = RequireString(root, "summary", "summary", refusals);
        var participants = RequireStringArray(root, "participants", "participants", refusals);
        var decisions = RequireObjectArray(root, "decisions", "decisions", refusals, ReadDecision);
        var actions = RequireObjectArray(root, "actions", "actions", refusals, ReadAction);
        var openQuestions = RequireObjectArray(root, "open_questions", "open_questions", refusals, ReadQuestion);

        RefuseUnknownKeys(root, "$", TopLevelKeys, refusals);

        return new ExtractionDocument(
            schemaVersion ?? string.Empty,
            meetingId ?? Guid.Empty,
            abstractText ?? string.Empty,
            summary ?? string.Empty,
            participants,
            decisions,
            actions,
            openQuestions);
    }

    private static ExtractedDecision ReadDecision(JsonElement item, string path, List<ExtractionRefusal> refusals)
    {
        var statement = RequireNonBlankString(item, "statement", $"{path}.statement", refusals);
        var evidence = ReadEvidence(item, "evidence", $"{path}.evidence", refusals);
        RefuseUnknownKeys(item, path, DecisionKeys, refusals);
        return new ExtractedDecision(statement ?? string.Empty, evidence);
    }

    private static ExtractedAction ReadAction(JsonElement item, string path, List<ExtractionRefusal> refusals)
    {
        var statement = RequireNonBlankString(item, "statement", $"{path}.statement", refusals);
        var dueDate = ReadOptionalDate(item, "due_date", $"{path}.due_date", refusals);
        var evidence = ReadEvidence(item, "evidence", $"{path}.evidence", refusals);
        RefuseUnknownKeys(item, path, ActionKeys, refusals);
        return new ExtractedAction(statement ?? string.Empty, dueDate, evidence);
    }

    private static ExtractedQuestion ReadQuestion(JsonElement item, string path, List<ExtractionRefusal> refusals)
    {
        var question = RequireNonBlankString(item, "question", $"{path}.question", refusals);
        var evidence = ReadEvidence(item, "evidence", $"{path}.evidence", refusals);
        RefuseUnknownKeys(item, path, QuestionKeys, refusals);
        return new ExtractedQuestion(question ?? string.Empty, evidence);
    }

    /// <summary>
    /// Absent or JSON <c>null</c> reads as no evidence at all, and never as a shape problem: an
    /// action with no due date and a statement with no citation are both things a provider may
    /// honestly have nothing to say about.
    /// </summary>
    private static ExtractedEvidence? ReadEvidence(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        if (!owner.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            refusals.Add(NotTheSchema(path));
            return null;
        }

        var ordinal = RequireInt(value, "utterance_ordinal", $"{path}.utterance_ordinal", refusals, min: 0);
        var start = RequireInt(value, "start_ms", $"{path}.start_ms", refusals, min: 0);
        var end = RequireInt(value, "end_ms", $"{path}.end_ms", refusals, min: 0);
        var speakerLabel = RequireString(value, "speaker_label", $"{path}.speaker_label", refusals);
        var quotedText = RequireString(value, "quoted_text", $"{path}.quoted_text", refusals);

        if (start.HasValue && end.HasValue && end.Value < start.Value)
        {
            refusals.Add(NotTheSchema($"{path}.end_ms"));
        }

        RefuseUnknownKeys(value, path, EvidenceKeys, refusals);

        return new ExtractedEvidence(
            ordinal ?? 0,
            Duration.FromMilliseconds(start ?? 0),
            Duration.FromMilliseconds(end ?? 0),
            speakerLabel ?? string.Empty,
            quotedText ?? string.Empty);
    }

    private static DateOnly? ReadOptionalDate(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        if (!owner.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(
                value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        refusals.Add(NotTheSchema(path));
        return null;
    }

    private static string? RequireSchemaVersion(JsonElement owner, List<ExtractionRefusal> refusals)
    {
        var text = RequireString(owner, "schema_version", "schema_version", refusals);
        if (text is not null && text != SchemaVersion)
        {
            refusals.Add(NotTheSchema("schema_version"));
            return null;
        }

        return text;
    }

    private static Guid? RequireGuid(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        var text = RequireString(owner, key, path, refusals);
        if (text is null)
        {
            return null;
        }

        if (!Guid.TryParse(text, out var id))
        {
            refusals.Add(NotTheSchema(path));
            return null;
        }

        return id;
    }

    private static string? RequireNonBlankString(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        var text = RequireString(owner, key, path, refusals);
        if (text is not null && string.IsNullOrWhiteSpace(text))
        {
            refusals.Add(NotTheSchema(path));
            return null;
        }

        return text;
    }

    private static string? RequireString(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        if (!owner.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
        {
            refusals.Add(NotTheSchema(path));
            return null;
        }

        return value.GetString();
    }

    private static int? RequireInt(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals, int min = int.MinValue)
    {
        if (!owner.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed)
            || parsed < min)
        {
            refusals.Add(NotTheSchema(path));
            return null;
        }

        return parsed;
    }

    private static IReadOnlyList<string> RequireStringArray(
        JsonElement owner, string key, string path, List<ExtractionRefusal> refusals)
    {
        if (!owner.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            refusals.Add(NotTheSchema(path));
            return [];
        }

        var items = new List<string>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                items.Add(item.GetString()!);
            }
            else
            {
                refusals.Add(NotTheSchema($"{path}[{index}]"));
            }

            index++;
        }

        return items;
    }

    private static IReadOnlyList<T> RequireObjectArray<T>(
        JsonElement owner,
        string key,
        string path,
        List<ExtractionRefusal> refusals,
        Func<JsonElement, string, List<ExtractionRefusal>, T> readItem)
    {
        if (!owner.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            refusals.Add(NotTheSchema(path));
            return [];
        }

        var items = new List<T>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            if (item.ValueKind == JsonValueKind.Object)
            {
                items.Add(readItem(item, itemPath, refusals));
            }
            else
            {
                refusals.Add(NotTheSchema(itemPath));
            }

            index++;
        }

        return items;
    }

    /// <summary>Every key an object carries that this shape does not name, refused where it stands.</summary>
    private static void RefuseUnknownKeys(
        JsonElement owner, string path, IReadOnlySet<string> known, List<ExtractionRefusal> refusals)
    {
        foreach (var property in owner.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                refusals.Add(NotTheSchema(path == "$" ? property.Name : $"{path}.{property.Name}"));
            }
        }
    }

    private static ExtractionRefusal NotTheSchema(string path) =>
        new(ExtractionCondition.NotTheSchema, path, null);
}
