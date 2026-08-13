namespace MeetingTranscriber.CorpusImport.Tests;

/// <summary>
/// The importer held the live instance of the mismatch — a database and a folder to copy into,
/// named by two flags nothing compared — so the rule that a corpus is one thing is asserted over
/// this assembly too. Nothing under <c>src/</c> may reference <c>tools/</c>, so the suite that
/// walks the product cannot see this one, and a rule it cannot reach is a rule this tool does not
/// have.
/// </summary>
public class CorpusIsOneThingTests
{
    [Fact]
    public void Nothing_in_the_importer_takes_a_corpus_and_a_folder_as_two_things()
    {
        CorpusPairing.WhereACorpusMeetsAFolder(typeof(CorpusImporter).Assembly).ShouldBeEmpty();
    }
}
