using System.Text;

using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Processing.Summaries.ClaudeCode;

namespace MeetingTranscriber.Processing.Tests.Summaries.ClaudeCode;

/// <summary>What Claude Code's own envelope is read as, and what only this file may know about it.</summary>
public class ClaudeCodeEnvelopeTests
{
    private const string ProviderVersion = "1.2.3 (Claude Code)";

    [Theory]
    [MemberData(nameof(Envelopes))]
    public void The_extraction_is_taken_out_of_the_envelope(byte[] standardOutput, string expectedOutput, string? expectedModel)
    {
        var answer = ClaudeCodeEnvelope.Read(standardOutput, ProviderVersion);

        var extracted = answer.ShouldBeOfType<SummaryProviderAnswer.Extracted>();
        Encoding.UTF8.GetString(extracted.Output).ShouldBe(expectedOutput);
        extracted.ProviderVersion.ShouldBe(ProviderVersion);
        extracted.Model.ShouldBe(expectedModel);
    }

    public static TheoryData<byte[], string, string?> Envelopes()
    {
        var data = new TheoryData<byte[], string, string?>
        {
            // A plain result, no fence.
            { Bytes(FakeClaudeCode.Envelope("""{"schema_version":"1"}""")), """{"schema_version":"1"}""", ClaudeCodeSummaries.Model },

            // A result fenced in Markdown.
            {
                Bytes(FakeClaudeCode.Envelope("```json\n{\"schema_version\":\"1\"}\n```")),
                """{"schema_version":"1"}""",
                ClaudeCodeSummaries.Model
            },

            // No modelUsage at all: the constant.
            {
                Bytes(FakeClaudeCode.Envelope("""{"a":1}""", sessionId: "s-1")),
                """{"a":1}""",
                ClaudeCodeSummaries.Model
            },

            // modelUsage present but empty: nothing to choose among, same as none at all.
            {
                Bytes(Wrap("""{"a":1}""", modelUsage: "\"modelUsage\":{}")),
                """{"a":1}""",
                ClaudeCodeSummaries.Model
            },

            // Two modelUsage entries, the housekeeping model billed first: the sonnet one wins.
            {
                Bytes(Wrap("""{"a":1}""", modelUsage: """
                    "modelUsage":{"claude-haiku-housekeeping":{"outputTokens":900},"claude-sonnet-4-5":{"outputTokens":5}}
                    """.Trim())),
                """{"a":1}""",
                "claude-sonnet-4-5"
            },

            // Two entries, neither sonnet: the one with more outputTokens wins.
            {
                Bytes(Wrap("""{"a":1}""", modelUsage: """
                    "modelUsage":{"claude-opus":{"outputTokens":5},"claude-haiku":{"outputTokens":40}}
                    """.Trim())),
                """{"a":1}""",
                "claude-haiku"
            },
        };

        return data;
    }

    [Fact]
    public void The_entry_naming_the_model_asked_for_wins_over_the_default_s()
    {
        var output = Bytes(Wrap("""{"a":1}""", modelUsage: """
            "modelUsage":{"claude-sonnet-4-5":{"outputTokens":900},"claude-opus-4-1":{"outputTokens":5}}
            """.Trim()));

        var asked = ClaudeCodeEnvelope.Read(output, ProviderVersion, "opus")
            .ShouldBeOfType<SummaryProviderAnswer.Extracted>();
        var none = ClaudeCodeEnvelope.Read(output, ProviderVersion)
            .ShouldBeOfType<SummaryProviderAnswer.Extracted>();

        asked.Model.ShouldBe("claude-opus-4-1");
        none.Model.ShouldBe("claude-sonnet-4-5");
    }

    [Fact]
    public void A_run_that_reported_no_usage_is_recorded_under_the_model_it_asked_for()
    {
        var output = Bytes(FakeClaudeCode.Envelope("""{"a":1}"""));

        ClaudeCodeEnvelope.Read(output, ProviderVersion, "opus")
            .ShouldBeOfType<SummaryProviderAnswer.Extracted>().Model.ShouldBe("opus");
    }

    [Theory]
    [MemberData(nameof(NotAnExtraction))]
    public void An_envelope_with_no_extraction_says_so(byte[] standardOutput)
    {
        var answer = ClaudeCodeEnvelope.Read(standardOutput, ProviderVersion);

        answer.ShouldBeOfType<SummaryProviderAnswer.DidNotAnswer>();
    }

    public static TheoryData<byte[]> NotAnExtraction() =>
    [
        Bytes("not json at all"),
        Bytes("""{"type":"result","subtype":"success","is_error":true,"result":"{}"}"""),
        Bytes("""{"type":"result","subtype":"error_max_turns","is_error":false,"result":"{}"}"""),
        Bytes("""{"type":"result","subtype":"success","is_error":false}"""),
        Bytes("""{"type":"result","subtype":"success","is_error":false,"result":42}"""),
    ];

    [Fact]
    public void An_envelope_that_quotes_a_long_result_is_cut_to_two_hundred_characters()
    {
        var said = new string('x', 500);
        var answer = ClaudeCodeEnvelope.Read(
            Bytes($$"""{"type":"result","subtype":"success","is_error":true,"result":"{{said}}"}"""),
            ProviderVersion);

        var didNotAnswer = answer.ShouldBeOfType<SummaryProviderAnswer.DidNotAnswer>();
        didNotAnswer.Said.ShouldContain(new string('x', 200));
        didNotAnswer.Said.ShouldNotContain(new string('x', 201));
    }

    /// <summary>
    /// Every word that only <see cref="ClaudeCodeEnvelope"/> may read is kept to
    /// <c>Summaries/ClaudeCode/</c> — red were <c>SummarisingAMeeting</c>, or any other file outside
    /// this folder, ever to spell one of the three itself.
    /// </summary>
    [Fact]
    public void Only_the_adapter_knows_how_the_answer_is_wrapped()
    {
        var guarded = new[] { "is_error", "subtype", "modelUsage" };
        var claudeCodeFolder = Path.Combine("Summaries", "ClaudeCode") + Path.DirectorySeparatorChar;

        var offenders = RepositoryTree.SourceUnder(RepositoryTree.Src)
            .Where(file => !file.FullName.Contains(claudeCodeFolder, StringComparison.OrdinalIgnoreCase))
            .Select(file => (File: file, Text: SourceText.WithoutProse(file)))
            .Where(source => guarded.Any(word => source.Text.Contains(word, StringComparison.Ordinal)))
            .Select(source => Path.GetRelativePath(RepositoryTree.Root.FullName, source.File.FullName).Replace('\\', '/'))
            .ToArray();

        offenders.ShouldBeEmpty(
            "only Summaries/ClaudeCode/ may know how Claude Code wraps its answer — \"is_error\", "
            + "\"subtype\" and \"modelUsage\" are the envelope's own words, and a file outside that "
            + "folder that names one of them has learned something only the adapter should know.");
    }

    private static string Wrap(string result, string modelUsage) =>
        $$"""{"type":"result","subtype":"success","is_error":false,"result":"{{Escape(result)}}",""" + modelUsage + "}";

    private static string Escape(string json) => json.Replace("\"", "\\\"");

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
}
