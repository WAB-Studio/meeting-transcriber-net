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
        ArgumentNullException.ThrowIfNull(corpus);

        options ??= new ImportOptions();
        var now = UtcTimestamp.From(clock.GetUtcNow());
        var report = new ImportReport();

        var (companies, projects, people) = corpus.ReadCatalog();
        var catalog = new ImportReport();
        var organizations = ImportOrganizations(companies, now, catalog);
        var initiatives = ImportInitiatives(projects, organizations, now, catalog);
        var personIds = ImportPeople(people, organizations, now, catalog);
        ImportCorrections(corpus.ReadCorrections(), now, catalog);
        Commit("the catalog", catalog, report);

        foreach (var legacy in corpus.Meetings())
        {
            // Each meeting is its own transaction. One that the corpus refuses used to take the
            // two hundred behind it, and the report with them — which is printed at the end and
            // is the only record of what a run left behind.
            var meeting = new ImportReport();
            Commit(
                legacy.Id,
                meeting,
                report,
                () => ImportMeeting(legacy, organizations, initiatives, personIds, options, now, meeting));
        }

        return report;
    }

    /// <summary>
    /// Reads one meeting and writes it, and folds its tally into the run's report. A meeting the
    /// corpus refuses — or one whose files cannot be read or copied — is named there instead, and
    /// everything read for it is thrown away so the next one starts from a clean change tracker.
    /// </summary>
    /// <remarks>
    /// A source already copied to disk is left where it is. It carries no row, so the next run
    /// copies and registers it again, which is the same outcome as never having tried — and
    /// deleting files is not something this tool does.
    /// </remarks>
    private void Commit(string what, ImportReport pending, ImportReport report, Action? read = null)
    {
        try
        {
            read?.Invoke();
            context.SaveChanges();
        }
        catch (Exception exception) when (exception is DbUpdateException or IOException
            or UnauthorizedAccessException)
        {
            report.CouldNotImport($"{what}: {(exception.InnerException ?? exception).Message}");
            foreach (var entry in context.ChangeTracker.Entries()
                .Where(entry => entry.State is not EntityState.Unchanged).ToArray())
            {
                entry.State = EntityState.Detached;
            }

            return;
        }

        report.Absorb(pending);
    }

    /// <summary>The catalog's companies, as the roots of the tree.</summary>
    private Dictionary<string, Node> ImportOrganizations(
        IReadOnlyList<LegacyCatalogEntry> entries,
        UtcTimestamp now,
        ImportReport report)
    {
        var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            byId[entry.Id] = NodeAt(entry.Name, parent: null, NodeKind.Organization, now, report, ImportCounter.Organization);
        }

        return byId;
    }

    /// <summary>
    /// The catalog's projects, under the organization each belongs to. A project whose company
    /// the catalog does not name becomes a root of its own: work belonging to nobody in
    /// particular is ordinary, and inventing an owner for it would be worse than having none.
    /// </summary>
    private Dictionary<string, Node> ImportInitiatives(
        IReadOnlyList<LegacyCatalogEntry> entries,
        IReadOnlyDictionary<string, Node> organizations,
        UtcTimestamp now,
        ImportReport report)
    {
        var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var parent = Lookup(organizations, entry.CompanyId);
            byId[entry.Id] = NodeAt(entry.Name, parent, NodeKind.Initiative, now, report, ImportCounter.Initiative);
        }

        return byId;
    }

    /// <summary>
    /// Finds a node by its place in the tree, or puts it there. Where it sits — its depth, and
    /// the copy of the parent the key is checked against — is the tree's to work out, never this
    /// tool's: a hardcoded depth here is what would put the first third-level node in wrong.
    /// </summary>
    private Node NodeAt(
        string name,
        Node? parent,
        NodeKind kind,
        UtcTimestamp now,
        ImportReport report,
        ImportCounter counter)
    {
        var parentId = parent?.Id;
        var existing = context.Nodes.Local.FirstOrDefault(node => node.ParentId == parentId && node.Name == name)
            ?? context.Nodes.FirstOrDefault(node => node.ParentId == parentId && node.Name == name);
        if (existing is not null)
        {
            return existing;
        }

        var created = parent is null
            ? Node.Root(Guid.NewGuid(), kind, name, now)
            : Node.Under(Guid.NewGuid(), parent, kind, name, now);
        context.Nodes.Add(created);
        report.Imported(counter);
        return created;
    }

    private Dictionary<string, Guid> ImportPeople(
        IReadOnlyList<LegacyCatalogEntry> entries,
        IReadOnlyDictionary<string, Node> organizations,
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
                    DisplayName = entry.Name,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                existing.WorksAt(Lookup(organizations, entry.CompanyId));
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
        // way to say a term only applies to one node.
        var known = context.TerminologyCorrections
            .Where(correction => correction.NodeId == null && correction.MeetingId == null)
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
        IReadOnlyDictionary<string, Node> organizations,
        IReadOnlyDictionary<string, Node> initiatives,
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
                report.Assume(
                    $"{legacy.Id}: no rendered transcript to read the language from, recorded as '{options.Language}'");
            }

            meeting = new Meeting
            {
                Id = Guid.NewGuid(),
                Title = legacy.Title,
                Context = legacy.Context,
                TemplateId = Template(legacy.MeetingType, now),
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

        Classify(legacy, meeting, organizations, initiatives, now, report);
        ImportArtifacts(legacy, meeting, options, now, report);
        ImportSpeakers(legacy, meeting, people, now, report);
    }

    /// <summary>
    /// What the meeting is work of. The project when it names one, and the organization itself
    /// when it does not — which is the meeting that happened before any project existed, and had
    /// nowhere to go when a meeting could only belong to a project.
    /// </summary>
    private void Classify(
        LegacyMeeting legacy,
        Meeting meeting,
        IReadOnlyDictionary<string, Node> organizations,
        IReadOnlyDictionary<string, Node> initiatives,
        UtcTimestamp now,
        ImportReport report)
    {
        // The old corpus names one project per meeting. Several is what the new shape allows and
        // what the old one could not say, so nothing here reads more than it wrote down.
        var node = Lookup(initiatives, legacy.ProjectId) ?? Lookup(organizations, legacy.CompanyId);
        if (node is null)
        {
            if (legacy.ProjectId is not null || legacy.CompanyId is not null)
            {
                report.CouldNotImport(
                    $"{legacy.Id}: names '{legacy.ProjectId ?? legacy.CompanyId}', which is not in the catalog");
            }

            return;
        }

        var linked = context.MeetingNodes.Local
            .Concat(context.MeetingNodes.Where(link => link.MeetingId == meeting.Id))
            .Any(link => link.MeetingId == meeting.Id
                && link.NodeId == node.Id
                && link.Role == MeetingNodeRole.WorkOf);
        if (linked)
        {
            return;
        }

        context.MeetingNodes.Add(new MeetingNode
        {
            MeetingId = meeting.Id,
            NodeId = node.Id,
            Role = MeetingNodeRole.WorkOf,
            CreatedAt = now,
        });
        report.Imported(ImportCounter.Classified);
    }

    /// <summary>
    /// The template a meeting was classified as. The old corpus called it the meeting type, and
    /// its values — a review, a refinement, a one to one — are exactly the shapes a template
    /// names.
    /// </summary>
    private Guid? Template(string? name, UtcTimestamp now)
    {
        if (name is null)
        {
            return null;
        }

        var existing = context.Templates.Local.FirstOrDefault(template => template.Name == name)
            ?? context.Templates.FirstOrDefault(template => template.Name == name);
        if (existing is null)
        {
            existing = new MeetingTemplate { Id = Guid.NewGuid(), Name = name, CreatedAt = now };
            context.Templates.Add(existing);
        }

        return existing.Id;
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
                report.SkippedByChoice(name);
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

    private static Node? Lookup(IReadOnlyDictionary<string, Node> known, string? key) =>
        key is not null && known.TryGetValue(key, out var node) ? node : null;

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
