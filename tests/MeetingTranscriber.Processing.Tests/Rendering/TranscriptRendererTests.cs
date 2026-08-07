using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Processing.Tests.Rendering;

/// <summary>
/// The shape of the two files. They are not the same content twice: one is for reading and one is
/// for lining up against the response, and the difference between them is the whole reason there
/// are two.
/// </summary>
public class TranscriptRendererTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 3, 4, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public void The_transcript_opens_with_frontmatter_a_person_can_read()
    {
        var markdown = Render(Header()).Markdown;

        markdown.ShouldStartWith("---\n");
        markdown.ShouldContain("language: es\n");
        markdown.ShouldContain("turns: 2\n");
        markdown.ShouldContain("started_at: 2026-03-04T14:00:00.000Z\n");
    }

    /// <summary>
    /// One heading per turn, so a chunker splits where the speaker changes rather than mid
    /// sentence — which is what makes a retrieved chunk something a claim can be traced back to.
    /// </summary>
    [Fact]
    public void Every_turn_is_its_own_heading()
    {
        var markdown = Render(Header()).Markdown;

        markdown.Split("\n## ").Length.ShouldBe(3);
        markdown.ShouldContain("## Renata — 0:00\n");
    }

    /// <summary>
    /// A name where somebody resolved one, and the provider's label where nobody has. An unresolved
    /// label reads as one on purpose: that is what makes it worth resolving.
    /// </summary>
    [Fact]
    public void A_label_nobody_resolved_is_rendered_as_the_label()
    {
        Render(Header()).Markdown.ShouldContain("## ch0:speaker_0 — 0:03\n");
    }

    /// <summary>
    /// The jsonl keeps the provider's labels. It is what a claim is lined up against, and a file
    /// carrying a name instead would stop matching the response the moment somebody was renamed.
    /// </summary>
    [Fact]
    public void The_jsonl_carries_the_labels_and_never_the_names()
    {
        var jsonl = Render(Header()).Jsonl;

        jsonl.ShouldContain("\"speaker_label\":\"ch1:speaker_0\"");
        jsonl.ShouldNotContain("Renata");
    }

    [Fact]
    public void The_jsonl_is_one_object_per_turn_in_order()
    {
        var lines = Render(Header()).Jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(2);
        var first = JsonDocument.Parse(lines[0]).RootElement;
        first.GetProperty("ordinal").GetInt32().ShouldBe(0);
        first.GetProperty("start_ms").GetInt64().ShouldBe(0);
        first.GetProperty("end_ms").GetInt64().ShouldBe(2000);
        first.GetProperty("channel").GetInt32().ShouldBe(1);
        first.GetProperty("confidence").GetDouble().ShouldBe(0.9);
        JsonDocument.Parse(lines[1]).RootElement.GetProperty("ordinal").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// Corrections reach both files, because they correct what the transcription heard rather than
    /// how it is displayed — and the file a claim is checked against has to hold the words the
    /// claim quotes.
    /// </summary>
    [Fact]
    public void A_correction_reaches_both_files()
    {
        var rendered = Render(Header() with
        {
            Corrections = [Correct("quati", "Coati")],
        });

        rendered.Markdown.ShouldContain("Coati");
        rendered.Markdown.ShouldNotContain("quati");
        rendered.Jsonl.ShouldContain("Coati");
        rendered.Jsonl.ShouldNotContain("quati");
    }

    /// <summary>
    /// A title with a colon or a quote in it is a value and not a broken document, which is what
    /// writing frontmatter as JSON buys over writing it by hand.
    /// </summary>
    [Fact]
    public void A_title_that_would_break_the_frontmatter_does_not()
    {
        var markdown = Render(Header() with { Title = "review: \"orchard\"" }).Markdown;

        markdown.ShouldContain("title: \"review: \\\"orchard\\\"\"\n");
    }

    /// <summary>Nothing written is not an empty string, which reads as somebody having cleared it.</summary>
    [Fact]
    public void A_meeting_with_no_title_has_no_title_line()
    {
        Render(Header()).Markdown.ShouldNotContain("title:");
    }

    /// <summary>
    /// The property the whole task rests on: same turns, same human layer, same bytes. Without it
    /// a rerender is a write nobody can tell from a change.
    /// </summary>
    [Fact]
    public void Rendering_the_same_meeting_twice_produces_the_same_bytes()
    {
        var header = Header();

        Render(header).ShouldBe(Render(header));
    }

    [Fact]
    public void Accents_are_written_as_themselves_and_not_escaped()
    {
        var rendered = Render(Header() with { Corrections = [Correct("sesion", "sesión")] });

        rendered.Jsonl.ShouldNotContain("\\u");
    }

    private static RenderedTranscript Render(TranscriptHeader header) => TranscriptRenderer.Render(
        header,
        [
            new Turn(0, Duration.Zero, Duration.FromMilliseconds(2000), AudioChannel.Microphone,
                "ch1:speaker_0", "arrancamos con quati y la sesion", 0.9),
            new Turn(1, Duration.FromMilliseconds(3000), Duration.FromMilliseconds(4000), AudioChannel.Loopback,
                "ch0:speaker_0", "de acuerdo", 0.8),
        ]);

    private static TranscriptHeader Header() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        When,
        "es",
        Title: null,
        Context: null,
        Names: new Dictionary<string, string>(StringComparer.Ordinal) { ["ch1:speaker_0"] = "Renata" },
        Corrections: []);

    private static TerminologyCorrection Correct(string wrong, string right) => new()
    {
        Id = Guid.NewGuid(),
        WrongText = wrong,
        CorrectText = right,
        MatchMode = TerminologyMatchMode.Exact,
        CreatedAt = When,
    };
}
