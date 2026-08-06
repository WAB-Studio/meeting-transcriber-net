using System.Text;

namespace MeetingTranscriber.CorpusImport;

/// <summary>
/// What one import did, and everything it could not do. The second half is the point: an import
/// that silently drops what it has no column for is how a corpus loses the part nobody can
/// regenerate.
/// </summary>
public sealed class ImportReport
{
    private readonly List<string> notImported = [];

    public int MeetingsImported { get; private set; }

    /// <summary>Meetings already in this corpus, matched on the response they came from.</summary>
    public int MeetingsAlreadyThere { get; private set; }

    public int ArtifactsRegistered { get; private set; }

    public int ArtifactsCopied { get; private set; }

    public int OrganizationsImported { get; private set; }

    public int InitiativesImported { get; private set; }

    public int PeopleImported { get; private set; }

    public int MeetingsClassified { get; private set; }

    public int SpeakersResolved { get; private set; }

    public int CorrectionsImported { get; private set; }

    /// <summary>What was read and left behind, each line saying which meeting and why.</summary>
    public IReadOnlyList<string> NotImported => notImported;

    public void Imported(ImportCounter counter, int amount = 1)
    {
        switch (counter)
        {
            case ImportCounter.Meeting: MeetingsImported += amount; break;
            case ImportCounter.MeetingAlreadyThere: MeetingsAlreadyThere += amount; break;
            case ImportCounter.Artifact: ArtifactsRegistered += amount; break;
            case ImportCounter.ArtifactCopy: ArtifactsCopied += amount; break;
            case ImportCounter.Organization: OrganizationsImported += amount; break;
            case ImportCounter.Initiative: InitiativesImported += amount; break;
            case ImportCounter.Person: PeopleImported += amount; break;
            case ImportCounter.Classified: MeetingsClassified += amount; break;
            case ImportCounter.Speaker: SpeakersResolved += amount; break;
            case ImportCounter.Correction: CorrectionsImported += amount; break;
            default: throw new ArgumentOutOfRangeException(nameof(counter), counter, "Unknown counter.");
        }
    }

    public void CouldNotImport(string what) => notImported.Add(what);

    public override string ToString()
    {
        var report = new StringBuilder();
        report.AppendLine($"meetings      {MeetingsImported} imported, {MeetingsAlreadyThere} already there");
        report.AppendLine($"artifacts     {ArtifactsRegistered} registered, {ArtifactsCopied} copied");
        report.AppendLine($"organizations {OrganizationsImported}");
        report.AppendLine($"initiatives   {InitiativesImported}");
        report.AppendLine($"people        {PeopleImported}");
        report.AppendLine($"links         {MeetingsClassified} meeting to node");
        report.AppendLine($"speakers      {SpeakersResolved} resolved onto a person");
        report.AppendLine($"corrections   {CorrectionsImported}");

        if (notImported.Count == 0)
        {
            return report.ToString();
        }

        report.AppendLine();
        report.AppendLine($"not imported ({notImported.Count}):");
        foreach (var line in notImported)
        {
            report.AppendLine($"  {line}");
        }

        return report.ToString();
    }
}

public enum ImportCounter
{
    Meeting,
    MeetingAlreadyThere,
    Artifact,
    ArtifactCopy,
    Organization,
    Initiative,
    Person,
    Classified,
    Speaker,
    Correction,
}
