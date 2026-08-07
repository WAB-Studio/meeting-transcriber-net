using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Rendering;

/// <summary>What the rendered files say about the meeting they belong to, above the turns.</summary>
/// <param name="Names">
/// The speaker label of a turn to the name of the person somebody resolved it to. A label with no
/// entry is rendered as the label, because nobody has said who it is yet.
/// </param>
public sealed record TranscriptHeader(
    Guid MeetingId,
    UtcTimestamp StartedAt,
    string Language,
    string? Title,
    string? Context,
    IReadOnlyDictionary<string, string> Names,
    IReadOnlyList<TerminologyCorrection> Corrections);

/// <summary>The two derived files of a meeting, as text and ready to be written.</summary>
public sealed record RenderedTranscript(string Markdown, string Jsonl);

/// <summary>
/// Turns into the two files a person and a machine each read. Pure: given the same turns and the
/// same human layer it returns the same bytes, which is what makes a rerender safe to run.
/// </summary>
/// <remarks>
/// <para>
/// The two files are deliberately not the same content in two shapes. <c>transcript.md</c> is for
/// reading: names where somebody resolved one, corrections applied, one heading per turn so that a
/// chunker splits on speaker boundaries rather than mid-sentence. <c>utterances.jsonl</c> is for a
/// machine to line up against the response: the provider's own labels, one object per line, and the
/// ordinal that a citation anchors on.
/// </para>
/// <para>
/// Corrections reach both, because they correct what the transcription heard rather than how it is
/// displayed, and the file that is compared against a claim has to hold the words the claim quotes.
/// Names do not reach the jsonl: a label is what <c>speaker_assignments</c> is keyed on, and a file
/// carrying a name instead would stop being comparable to the raw response the moment somebody was
/// renamed.
/// </para>
/// </remarks>
public static class TranscriptRenderer
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        // The corpus is mostly Spanish and these files are read by people. Escaping every accent
        // to é would triple the size of a transcript and make a diff of one unreadable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static RenderedTranscript Render(TranscriptHeader header, IReadOnlyList<Turn> turns)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(turns);

        return new RenderedTranscript(Markdown(header, turns), Jsonl(header, turns));
    }

    /// <summary>
    /// The readable file: frontmatter somebody can scan, then one <c>##</c> per turn carrying who
    /// said it and when.
    /// </summary>
    private static string Markdown(TranscriptHeader header, IReadOnlyList<Turn> turns)
    {
        var markdown = new StringBuilder();
        markdown.Append("---\n");
        markdown.Append(CultureInfo.InvariantCulture, $"meeting: {header.MeetingId}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"started_at: {header.StartedAt}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"language: {header.Language}\n");
        markdown.Append(CultureInfo.InvariantCulture, $"turns: {turns.Count}\n");
        Frontmatter(markdown, "title", header.Title, header);
        Frontmatter(markdown, "context", header.Context, header);
        markdown.Append("---\n");

        foreach (var turn in turns)
        {
            markdown.Append(CultureInfo.InvariantCulture, $"\n## {Speaker(header, turn)} — {Clock(turn.Start)}\n\n");
            markdown.Append(Terminology.Apply(turn.Text, header.Corrections));
            markdown.Append('\n');
        }

        return markdown.ToString();
    }

    /// <summary>
    /// One object per line, in the order the turns are in. JSON rather than the file's own quoting
    /// rules, so a title holding a colon or a quote is a value and not a broken document.
    /// </summary>
    private static string Jsonl(TranscriptHeader header, IReadOnlyList<Turn> turns)
    {
        var jsonl = new StringBuilder();
        foreach (var turn in turns)
        {
            jsonl.Append(JsonSerializer.Serialize(
                new
                {
                    meeting = header.MeetingId,
                    ordinal = turn.Ordinal,
                    start_ms = turn.Start.Milliseconds,
                    end_ms = turn.End.Milliseconds,
                    channel = turn.Channel is { } channel ? (int)channel : (int?)null,
                    speaker_label = turn.SpeakerLabel,
                    confidence = turn.Confidence,
                    text = Terminology.Apply(turn.Text, header.Corrections),
                },
                Compact));
            jsonl.Append('\n');
        }

        return jsonl.ToString();
    }

    /// <summary>
    /// Who to put above a turn: the person somebody resolved that label to, or the label itself.
    /// Never a guess — an unresolved label reads as one, which is what makes it worth resolving.
    /// </summary>
    private static string Speaker(TranscriptHeader header, Turn turn) =>
        header.Names.TryGetValue(turn.SpeakerLabel, out var name) ? name : turn.SpeakerLabel;

    /// <summary>An offset as somebody would read it off a player.</summary>
    private static string Clock(Duration offset)
    {
        var span = TimeSpan.FromMilliseconds(offset.Milliseconds);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A frontmatter line a person wrote, as JSON so that a colon, a quote or a newline in it stays
    /// inside the value. Omitted when there is nothing, rather than written as an empty string that
    /// reads as somebody having cleared it.
    /// </summary>
    private static void Frontmatter(StringBuilder markdown, string key, string? value, TranscriptHeader header)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var corrected = Terminology.Apply(value, header.Corrections);
        markdown.Append(CultureInfo.InvariantCulture, $"{key}: {JsonSerializer.Serialize(corrected, Compact)}\n");
    }
}
