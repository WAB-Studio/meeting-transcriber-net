using System.Text;

using MeetingTranscriber.Processing.Summaries;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// The words a provider is shown answer to the one reader that decides what an extraction is.
/// </summary>
public class ExtractionInstructionsTests
{
    [Fact]
    public void The_example_the_provider_is_shown_is_one_the_reader_accepts()
    {
        var read = ExtractionReader.Read(Encoding.UTF8.GetBytes(ExtractionInstructions.Example));

        read.Refusals.ShouldBeEmpty();
        read.Document.ShouldNotBeNull();
    }

    [Fact]
    public void The_shape_is_the_version_the_reader_reads()
    {
        ExtractionInstructions.Schema.Version.ShouldBe(ExtractionReader.SchemaVersion);
        ExtractionInstructions.Schema.Document.ShouldContain(ExtractionInstructions.Example);
    }
}
