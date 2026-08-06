using System.Globalization;
using System.Text.Json;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MeetingTranscriber.CorpusImport;

/// <summary>What a meeting folder of the Python corpus holds that is worth importing.</summary>
public sealed record LegacyMeeting
{
    /// <summary>The folder name, which is also the identifier the Python corpus uses.</summary>
    public required string Id { get; init; }

    public required DirectoryInfo Directory { get; init; }

    public UtcTimestamp? RecordedAt { get; init; }

    public Duration? Duration { get; init; }

    public SourceProfile? Profile { get; init; }

    public string? Language { get; init; }

    public string? Title { get; init; }

    public string? Context { get; init; }

    /// <summary>What the old corpus called the meeting type: a review, a refinement, a one to one.</summary>
    public string? MeetingType { get; init; }

    public string? CompanyId { get; init; }

    public string? ProjectId { get; init; }

    /// <summary>Diarization label to the catalog id of the person somebody resolved it to.</summary>
    public IReadOnlyDictionary<string, string> Speakers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Why this meeting cannot be imported at all, or null when it can.</summary>
    public string? Unreadable { get; init; }
}

/// <summary>An entry of the Python corpus catalog: a company, a project or a person.</summary>
public sealed record LegacyCatalogEntry(string Id, string Name, string? CompanyId);

/// <summary>A term the transcription gets wrong, and the spellings it gets wrong as.</summary>
public sealed record LegacyTerm(string Canonical, IReadOnlyList<string> Aliases);

/// <summary>
/// The Python corpus, read and never written. Every path it opens it opens for reading, and
/// nothing in this file creates, moves or deletes anything inside it.
/// </summary>
/// <remarks>
/// Tolerant on purpose. A corpus that has been edited by hand for months has meetings missing a
/// meta.yaml, speakers nobody ever resolved and folders with no response in them. Refusing the
/// whole import over one of those would make the tool useless exactly when it is needed; each is
/// read as far as it goes and the rest is named in the report.
/// </remarks>
public sealed class LegacyCorpus(DirectoryInfo root)
{
    /// <summary>OBS names a recording <c>YYYY-MM-DD HH-MM-SS</c>, in the machine's own time.</summary>
    private const string RecordedAtFormat = "yyyy-MM-dd HH-mm-ss";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public DirectoryInfo Root { get; } = root;

    public FileInfo Catalog => new(Path.Combine(Root.FullName, "catalog.yaml"));

    public FileInfo Corrections => new(Path.Combine(Root.FullName, "corrections.yaml"));

    /// <summary>Every folder with a paid response in it, oldest first.</summary>
    public IEnumerable<LegacyMeeting> Meetings() => Root
        .EnumerateDirectories()
        .Where(folder => File.Exists(Path.Combine(folder.FullName, "deepgram.json")))
        .OrderBy(folder => folder.Name, StringComparer.Ordinal)
        .Select(ReadMeeting);

    /// <summary>Companies, projects and people, by the ids the corpus refers to them with.</summary>
    public (
        IReadOnlyList<LegacyCatalogEntry> Companies,
        IReadOnlyList<LegacyCatalogEntry> Projects,
        IReadOnlyList<LegacyCatalogEntry> People) ReadCatalog()
    {
        if (!Catalog.Exists)
        {
            return ([], [], []);
        }

        var catalog = Read<CatalogFile>(Catalog);
        return (Entries(catalog?.companies), Entries(catalog?.projects), Entries(catalog?.people));

        static IReadOnlyList<LegacyCatalogEntry> Entries(Dictionary<string, CatalogEntry>? section) =>
        [
            .. (section ?? []).Where(pair => !string.IsNullOrWhiteSpace(pair.Value?.name))
                .Select(pair => new LegacyCatalogEntry(pair.Key, pair.Value.name!, pair.Value.company)),
        ];
    }

    /// <summary>The approved terminology corrections that are still switched on.</summary>
    public IReadOnlyList<LegacyTerm> ReadCorrections()
    {
        if (!Corrections.Exists)
        {
            return [];
        }

        var file = Read<CorrectionsFile>(Corrections);
        return
        [
            .. (file?.terms ?? []).Values
                .Where(term => term is { active: true } && !string.IsNullOrWhiteSpace(term.canonical))
                .Select(term => new LegacyTerm(
                    term.canonical!,
                    [.. (term.aliases ?? []).Where(alias => !string.IsNullOrWhiteSpace(alias))])),
        ];
    }

    private LegacyMeeting ReadMeeting(DirectoryInfo folder)
    {
        var meeting = new LegacyMeeting
        {
            Id = folder.Name,
            Directory = folder,
            RecordedAt = RecordedAt(folder.Name),
            Language = Language(folder),
        };

        try
        {
            using var response = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(folder.FullName, "deepgram.json")));
            var metadata = response.RootElement.GetProperty("metadata");
            meeting = meeting with
            {
                Duration = Duration.FromSeconds(metadata.GetProperty("duration").GetDouble()),
                Profile = metadata.GetProperty("channels").GetInt32() switch
                {
                    1 => SourceProfile.Diarize,
                    2 => SourceProfile.Multichannel,
                    var channels => throw new AudioContractException(
                        $"A response of {channels} channels matches no profile."),
                },
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException
            or InvalidOperationException or IOException)
        {
            return meeting with { Unreadable = $"deepgram.json cannot be read: {exception.Message}" };
        }

        var metaPath = new FileInfo(Path.Combine(folder.FullName, "meta.yaml"));
        if (!metaPath.Exists)
        {
            return meeting;
        }

        var meta = Read<MetaFile>(metaPath);
        return meeting with
        {
            Title = Trimmed(meta?.title),
            Context = Trimmed(meta?.context),
            MeetingType = Trimmed(meta?.meeting_type),
            CompanyId = Trimmed(meta?.company),
            ProjectId = Trimmed(meta?.project),
            Speakers = (meta?.speakers ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value!.Trim(), StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// The language of a meeting, which the paid response does not carry: it was a request
    /// parameter. The rendered transcript's frontmatter is the only place the corpus wrote it
    /// down, so a derived file answers for a fact that exists nowhere else.
    /// </summary>
    private static string? Language(DirectoryInfo folder)
    {
        var transcript = new FileInfo(Path.Combine(folder.FullName, "transcript.md"));
        if (!transcript.Exists)
        {
            return null;
        }

        foreach (var line in File.ReadLines(transcript.FullName).Take(30))
        {
            if (line.StartsWith("language:", StringComparison.Ordinal))
            {
                return Trimmed(line["language:".Length..].Trim().Trim('"'));
            }
        }

        return null;
    }

    private static UtcTimestamp? RecordedAt(string folderName)
    {
        if (folderName.Length < RecordedAtFormat.Length
            || !DateTime.TryParseExact(
                folderName[..RecordedAtFormat.Length],
                RecordedAtFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        // The recorder wrote the name in the machine's own time, and it is the same machine.
        return UtcTimestamp.From(DateTime.SpecifyKind(parsed, DateTimeKind.Local));
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T? Read<T>(FileInfo file)
        where T : class
    {
        using var reader = file.OpenText();
        return Yaml.Deserialize<T>(reader);
    }

#pragma warning disable SA1300, IDE1006 // The names are the corpus's, not this codebase's.
    private sealed class CatalogFile
    {
        public Dictionary<string, CatalogEntry>? companies { get; set; }

        public Dictionary<string, CatalogEntry>? projects { get; set; }

        public Dictionary<string, CatalogEntry>? people { get; set; }
    }

    private sealed class CatalogEntry
    {
        public string? name { get; set; }

        public string? company { get; set; }
    }

    private sealed class CorrectionsFile
    {
        public Dictionary<string, TermEntry>? terms { get; set; }
    }

    private sealed class TermEntry
    {
        public string? canonical { get; set; }

        public List<string>? aliases { get; set; }

        public bool active { get; set; }
    }

    private sealed class MetaFile
    {
        public string? company { get; set; }

        public string? project { get; set; }

        public string? title { get; set; }

        public string? context { get; set; }

        public string? meeting_type { get; set; }

        public Dictionary<string, string?>? speakers { get; set; }
    }
#pragma warning restore SA1300, IDE1006
}
