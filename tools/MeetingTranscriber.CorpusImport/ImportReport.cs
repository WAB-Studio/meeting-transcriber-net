using System.Text;

namespace MeetingTranscriber.CorpusImport;

/// <summary>
/// What one import did, and everything it could not do. The last of the three lists is the point:
/// an import that silently drops what it has no column for is how a corpus loses the part nobody
/// can regenerate, and it is also the shortest list, so it is the one that has to stay unburied.
/// </summary>
/// <remarks>
/// Three lists and not one. A file this application renders itself was never going to be imported
/// and says nothing about this corpus; a meeting that came in with its language taken from a
/// default did come in; what had nowhere to go did not. Run over a real corpus those differ by two
/// orders of magnitude in length, and mixing them puts the only one worth reading underneath the
/// other two.
/// </remarks>
public sealed class ImportReport
{
    private readonly Dictionary<ImportCounter, int> counts = [];
    private readonly Dictionary<string, int> skipped = new(StringComparer.Ordinal);
    private readonly List<string> assumed = [];
    private readonly List<string> notImported = [];

    public int MeetingsImported => Count(ImportCounter.Meeting);

    /// <summary>Meetings already in this corpus, matched on the response they came from.</summary>
    public int MeetingsAlreadyThere => Count(ImportCounter.MeetingAlreadyThere);

    public int ArtifactsRegistered => Count(ImportCounter.Artifact);

    public int ArtifactsCopied => Count(ImportCounter.ArtifactCopy);

    public int OrganizationsImported => Count(ImportCounter.Organization);

    public int InitiativesImported => Count(ImportCounter.Initiative);

    public int PeopleImported => Count(ImportCounter.Person);

    public int MeetingsClassified => Count(ImportCounter.Classified);

    public int SpeakersResolved => Count(ImportCounter.Speaker);

    public int CorrectionsImported => Count(ImportCounter.Correction);

    /// <summary>
    /// Files left where they are on purpose, by name and how many meetings had one. They are this
    /// application's to render again, so there are three per meeting and none of them is news.
    /// </summary>
    public IReadOnlyDictionary<string, int> SkippedByDesign => skipped;

    /// <summary>
    /// Meetings that did arrive, carrying something the corpus did not say and the import chose.
    /// Worth reading once; not a loss.
    /// </summary>
    public IReadOnlyList<string> Assumed => assumed;

    /// <summary>What was read and had nowhere to go, each line saying which meeting and why.</summary>
    public IReadOnlyList<string> NotImported => notImported;

    public void Imported(ImportCounter counter, int amount = 1)
    {
        if (!Enum.IsDefined(counter))
        {
            throw new ArgumentOutOfRangeException(nameof(counter), counter, "Unknown counter.");
        }

        counts[counter] = Count(counter) + amount;
    }

    /// <summary>A file that was never going to be imported, because this application makes it.</summary>
    public void SkippedByChoice(string file) => skipped[file] = skipped.GetValueOrDefault(file) + 1;

    /// <summary>A meeting that arrived with something the import had to decide for it.</summary>
    public void Assume(string what) => assumed.Add(what);

    public void CouldNotImport(string what) => notImported.Add(what);

    /// <summary>
    /// Folds one meeting's own tally into this one, which happens only once the corpus has
    /// accepted it. A meeting whose rows were refused leaves nothing behind but the line saying so.
    /// </summary>
    public void Absorb(ImportReport other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (counter, amount) in other.counts)
        {
            Imported(counter, amount);
        }

        foreach (var (file, amount) in other.skipped)
        {
            skipped[file] = skipped.GetValueOrDefault(file) + amount;
        }

        assumed.AddRange(other.assumed);
        notImported.AddRange(other.notImported);
    }

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

        if (skipped.Count > 0)
        {
            var files = skipped.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} {pair.Value}");
            report.AppendLine($"rendered here {string.Join(", ", files)}");
        }

        Section(report, "assumed", assumed);
        Section(report, "not imported", notImported);
        return report.ToString();
    }

    private static void Section(StringBuilder report, string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        report.AppendLine();
        report.AppendLine($"{title} ({lines.Count}):");
        foreach (var line in lines)
        {
            report.AppendLine($"  {line}");
        }
    }

    private int Count(ImportCounter counter) => counts.GetValueOrDefault(counter);
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
