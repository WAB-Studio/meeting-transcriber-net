using System.Globalization;
using System.Security.Cryptography;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.CorpusImport;

/// <summary>How an import is allowed to treat the corpus it reads.</summary>
/// <param name="CopyTo">
/// Where to copy the sources, or null to register them where they already are. Copying is the
/// explicit option because it doubles the disk the paid responses take, and referencing is the
/// one that breaks if the legacy corpus is ever moved.
/// </param>
/// <param name="Language">
/// The language to record for a meeting whose rendered transcript does not say. The paid response
/// does not carry it: it was a request parameter.
/// </param>
public sealed record ImportOptions(DirectoryInfo? CopyTo = null, string Language = "es");

/// <summary>
/// Reads the Python corpus into this one. One way: it opens the legacy corpus for reading and
/// never creates, moves, rewrites or deletes anything inside it.
/// </summary>
/// <remarks>
/// Repeatable. A meeting is matched on the SHA-256 of the response it was transcribed from, so
/// running it twice over the same corpus imports nothing the second time rather than making a
/// second copy of every meeting — and renaming a folder does not turn one meeting into two.
/// Everything the human layer holds — the companies, the projects, the people, the resolved
/// speakers, the titles and the corrections — is matched by what it is, the same way.
/// </remarks>
public sealed class CorpusImporter(CorpusDbContext context, TimeProvider clock)
{
    private static readonly (string File, ArtifactKind Kind)[] Sources =
    [
        ("deepgram.json", ArtifactKind.DeepgramResponse),
        ("extraction.json", ArtifactKind.Extraction),
    ];

    /// <summary>
    /// The rendered files. Not imported: they are this application's to produce, and registering
    /// the Python system's output would claim a rebuild reproduces bytes that nothing here wrote.
    /// </summary>
    private static readonly string[] Derived = ["transcript.md", "utterances.jsonl", "summary.md"];

    public ImportReport Import(LegacyCorpus corpus, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var now = UtcTimestamp.From(clock.GetUtcNow());
        var report = new ImportReport();

        var (companies, projects, people) = corpus.ReadCatalog();
        var companyIds = ImportCompanies(companies, now, report);
        var projectIds = ImportProjects(projects, companyIds, now, report);
        var personIds = ImportPeople(people, companyIds, now, report);

        ImportCorrections(corpus.ReadCorrections(), now, report);

        foreach (var legacy in corpus.Meetings())
        {
            ImportMeeting(legacy, projectIds, personIds, options, now, report);
        }

        context.SaveChanges();
        return report;
    }

    private Dictionary<string, Guid> ImportCompanies(
        IReadOnlyList<LegacyCatalogEntry> entries,
        UtcTimestamp now,
        ImportReport report)
    {
        var byId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var existing = context.Companies.Local.FirstOrDefault(company => company.Name == entry.Name)
                ?? context.Companies.FirstOrDefault(company => company.Name == entry.Name);
            if (existing is null)
            {
                existing = new Company { Id = Guid.NewGuid(), Name = entry.Name, CreatedAt = now, UpdatedAt = now };
                context.Companies.Add(existing);
                report.Imported(ImportCounter.Company);
            }

            byId[entry.Id] = existing.Id;
        }

        return byId;
    }

    private Dictionary<string, Guid> ImportProjects(
        IReadOnlyList<LegacyCatalogEntry> entries,
        IReadOnlyDictionary<string, Guid> companies,
        UtcTimestamp now,
        ImportReport report)
    {
        var byId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var company = Lookup(companies, entry.CompanyId);
            var existing = context.Projects.Local.FirstOrDefault(project => project.Name == entry.Name)
                ?? context.Projects.FirstOrDefault(project => project.Name == entry.Name);
            if (existing is null)
            {
                existing = new Project
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company,
                    Name = entry.Name,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.Projects.Add(existing);
                report.Imported(ImportCounter.Project);
            }

            byId[entry.Id] = existing.Id;
        }

        return byId;
    }

    private Dictionary<string, Guid> ImportPeople(
        IReadOnlyList<LegacyCatalogEntry> entries,
        IReadOnlyDictionary<string, Guid> companies,
        UtcTimestamp now,
        ImportReport report)
    {
        var byId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            // People have no unique name in the schema — two colleagues can share one — so the
            // match is by name here rather than by a constraint, and it is the legacy catalog's
            // own identity that decides they are the same person.
            var existing = context.People.Local.FirstOrDefault(person => person.DisplayName == entry.Name)
                ?? context.People.FirstOrDefault(person => person.DisplayName == entry.Name);
            if (existing is null)
            {
                existing = new Person
                {
                    Id = Guid.NewGuid(),
                    CompanyId = Lookup(companies, entry.CompanyId),
                    DisplayName = entry.Name,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.People.Add(existing);
                report.Imported(ImportCounter.Person);
            }

            byId[entry.Id] = existing.Id;
        }

        return byId;
    }

    private void ImportCorrections(IReadOnlyList<LegacyTerm> terms, UtcTimestamp now, ImportReport report)
    {
        // Global, because that is what the legacy file is: one list for the whole corpus, with no
        // way to say a term only applies to one project.
        var known = context.TerminologyCorrections
            .Where(correction => correction.ProjectId == null && correction.MeetingId == null)
            .Select(correction => correction.WrongText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in terms.SelectMany(term => term.Aliases.Select(alias => (term.Canonical, alias))))
        {
            if (!known.Add(alias.alias))
            {
                continue;
            }

            context.TerminologyCorrections.Add(new TerminologyCorrection
            {
                Id = Guid.NewGuid(),
                WrongText = alias.alias,
                CorrectText = alias.Canonical,
                // The Python renderer matches without regard to case, and a corpus of speech has
                // the same word at the start of a sentence and in the middle of one.
                MatchMode = TerminologyMatchMode.IgnoreCase,
                CreatedAt = now,
            });
            report.Imported(ImportCounter.Correction);
        }
    }

    private void ImportMeeting(
        LegacyMeeting legacy,
        IReadOnlyDictionary<string, Guid> projects,
        IReadOnlyDictionary<string, Guid> people,
        ImportOptions options,
        UtcTimestamp now,
        ImportReport report)
    {
        if (legacy.Unreadable is { } problem)
        {
            report.CouldNotImport($"{legacy.Id}: {problem}");
            return;
        }

        if (legacy.RecordedAt is not { } startedAt)
        {
            report.CouldNotImport($"{legacy.Id}: the folder name gives no recording date");
            return;
        }

        // What decides this meeting is already here: the response it was transcribed from. It is
        // stored, it is indexed, and it does not care what the folder it arrived in was called.
        var response = new FileInfo(Path.Combine(legacy.Directory.FullName, "deepgram.json"));
        var responseSha256 = Sha256(response);
        var meeting = Imported(responseSha256) is { } imported
            ? context.Meetings.Local.FirstOrDefault(existing => existing.Id == imported)
                ?? context.Meetings.First(existing => existing.Id == imported)
            : null;

        if (meeting is not null)
        {
            report.Imported(ImportCounter.MeetingAlreadyThere);
        }
        else
        {
            if (legacy.Language is null)
            {
                report.CouldNotImport(
                    $"{legacy.Id}: no rendered transcript to read the language from, recorded as '{options.Language}'");
            }

            meeting = new Meeting
            {
                Id = Guid.NewGuid(),
                ProjectId = Lookup(projects, legacy.ProjectId),
                Title = legacy.Title,
                StartedAt = startedAt,
                Duration = legacy.Duration,
                SourceProfile = legacy.Profile!.Value,
                Language = legacy.Language ?? options.Language,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.Meetings.Add(meeting);
            report.Imported(ImportCounter.Meeting);

            // Where it came from, in the table provenance belongs in rather than as a column of
            // the meeting. It outlives the tool that wrote it and costs the application nothing.
            context.AuditEvents.Add(new AuditEvent
            {
                OccurredAt = now,
                Actor = AuditActor.App,
                Action = "imported",
                MeetingId = meeting.Id,
                Detail = $"from the Python corpus, folder '{legacy.Id}'",
            });
        }

        if (legacy.Context is not null)
        {
            report.CouldNotImport($"{legacy.Id}: the meeting's context note has no column to go in");
        }

        // A meeting reaches its company through its project. With no project there is no route,
        // and the company the old corpus wrote on the meeting itself has nowhere to land.
        if (legacy.CompanyId is not null && legacy.ProjectId is null)
        {
            report.CouldNotImport(
                $"{legacy.Id}: names company '{legacy.CompanyId}' and no project, so the company is not reachable");
        }

        ImportArtifacts(legacy, meeting, options, now, report);
        ImportSpeakers(legacy, meeting, people, now, report);
    }

    private void ImportArtifacts(
        LegacyMeeting legacy,
        Meeting meeting,
        ImportOptions options,
        UtcTimestamp now,
        ImportReport report)
    {
        foreach (var name in Derived)
        {
            if (File.Exists(Path.Combine(legacy.Directory.FullName, name)))
            {
                report.CouldNotImport($"{legacy.Id}: {name} is this application's to render again");
            }
        }

        foreach (var (name, kind) in Sources)
        {
            var source = new FileInfo(Path.Combine(legacy.Directory.FullName, name));
            if (!source.Exists)
            {
                continue;
            }

            var relativePath = options.CopyTo is null
                ? Path.Combine(legacy.Directory.Name, name).Replace('\\', '/')
                : $"meetings/{meeting.Id}/{name}";

            var known = context.Artifacts.Local
                .Concat(context.Artifacts.Where(artifact => artifact.MeetingId == meeting.Id))
                .Any(artifact => artifact.MeetingId == meeting.Id && artifact.RelativePath == relativePath);
            if (known)
            {
                continue;
            }

            var sha256 = Sha256(source);
            if (options.CopyTo is { } target)
            {
                Copy(source, new FileInfo(Path.Combine(target.FullName, relativePath)), sha256);
                report.Imported(ImportCounter.ArtifactCopy);
            }

            context.Artifacts.Add(new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting.Id,
                Kind = kind,
                Origin = kind.OriginOf(),
                RelativePath = relativePath,
                ByteSize = source.Length,
                Sha256 = sha256,
                CreatedAt = now,
            });
            report.Imported(ImportCounter.Artifact);
        }
    }

    /// <summary>
    /// The resolved diarization labels, which are the most valuable thing in the legacy corpus:
    /// nothing regenerates them, and every one is somebody having listened.
    /// </summary>
    /// <remarks>
    /// Channel 1 is left alone on purpose. The Python system labelled it with the user's name
    /// from configuration rather than from a decision about that meeting, and a microphone can
    /// carry more than one person in a room. Importing it would put a guess where the corpus only
    /// holds things a person said.
    /// </remarks>
    private void ImportSpeakers(
        LegacyMeeting legacy,
        Meeting meeting,
        IReadOnlyDictionary<string, Guid> people,
        UtcTimestamp now,
        ImportReport report)
    {
        foreach (var (label, personKey) in legacy.Speakers)
        {
            if (Lookup(people, personKey) is not { } personId)
            {
                report.CouldNotImport($"{legacy.Id}: {label} names '{personKey}', who is not in the catalog");
                continue;
            }

            if (SpeakerLabel(label) is not { } speakerLabel)
            {
                report.CouldNotImport($"{legacy.Id}: '{label}' is not a label the provider would have written");
                continue;
            }

            var assigned = context.SpeakerAssignments.Local
                .Concat(context.SpeakerAssignments.Where(assignment => assignment.MeetingId == meeting.Id))
                .Any(assignment => assignment.MeetingId == meeting.Id && assignment.SpeakerLabel == speakerLabel);
            if (assigned)
            {
                continue;
            }

            context.SpeakerAssignments.Add(new SpeakerAssignment
            {
                MeetingId = meeting.Id,
                SpeakerLabel = speakerLabel,
                PersonId = personId,
                AssignedBy = SpeakerAssignmentSource.Person,
                CreatedAt = now,
            });
            report.Imported(ImportCounter.Speaker);

            var present = context.MeetingParticipants.Local
                .Concat(context.MeetingParticipants.Where(participant => participant.MeetingId == meeting.Id))
                .Any(participant => participant.MeetingId == meeting.Id && participant.PersonId == personId);
            if (!present)
            {
                context.MeetingParticipants.Add(new MeetingParticipant
                {
                    MeetingId = meeting.Id,
                    PersonId = personId,
                    CreatedAt = now,
                });
            }
        }
    }

    /// <summary>
    /// The stored form of a label the Python system wrote for display. It counted speakers from
    /// one because a person reads it; the provider counts from zero, and what is stored is what
    /// the provider said.
    /// </summary>
    private static string? SpeakerLabel(string legacyLabel)
    {
        const string prefix = "Speaker ";
        if (!legacyLabel.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(legacyLabel[prefix.Length..], CultureInfo.InvariantCulture, out var number)
            || number < 1)
        {
            return null;
        }

        return $"speaker_{number - 1}";
    }

    /// <summary>The meeting a response with this hash was already imported as, if there is one.</summary>
    private Guid? Imported(string responseSha256) => context.Artifacts.Local
        .Concat(context.Artifacts)
        .FirstOrDefault(artifact =>
            artifact.Kind == ArtifactKind.DeepgramResponse && artifact.Sha256 == responseSha256)
        ?.MeetingId;

    private static Guid? Lookup(IReadOnlyDictionary<string, Guid> known, string? key) =>
        key is not null && known.TryGetValue(key, out var id) ? id : null;

    private static string Sha256(FileInfo file)
    {
        using var stream = file.OpenRead();
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void Copy(FileInfo source, FileInfo target, string sha256)
    {
        target.Directory!.Create();
        source.CopyTo(target.FullName, overwrite: true);

        // A copy that arrived wrong is worse than one that failed: the corpus would hold a paid
        // response it cannot tell apart from the real one.
        var copied = Sha256(target);
        if (copied != sha256)
        {
            target.Delete();
            throw new IOException($"'{source.FullName}' copied as {copied}, not {sha256}.");
        }
    }
}
