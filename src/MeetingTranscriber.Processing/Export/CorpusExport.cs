using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Export;

/// <summary>What an export left: where, how many meetings, and what the corpus records that the disk did not have.</summary>
/// <param name="Folder">The finished export, with no <c>.partial</c> on it.</param>
/// <param name="Meetings">How many meetings have a folder in it.</param>
/// <param name="LeftOut">The corpus-relative paths the corpus records and the disk did not hold.</param>
public sealed record CorpusExported(DirectoryInfo Folder, int Meetings, IReadOnlyList<string> LeftOut);

/// <summary>
/// Takes what a person chose out of the corpus into a folder that can be read with nothing
/// installed.
/// </summary>
/// <remarks>
/// <para>
/// <b>A tick is a kind of thing.</b> <see cref="KindOf"/> is the one place that says which
/// artifact belongs to which <see cref="ExportKind"/>. With <see cref="ExportKind.HandCorrections"/>
/// unticked nothing that carries a hand correction goes: the corpus's own <c>transcript.md</c> holds
/// names on voices, corrected words and the context note, so it is replaced by a transcript the
/// export renders itself over the stored turns with none of them. Stored turns never carry a
/// correction, so every word is the one the provider returned. A meeting's title goes either way,
/// because it is the meeting's name and not a correction.
/// </para>
/// <para>
/// <b>It lives here and not beside storage</b> because the bare transcript goes through
/// <see cref="TranscriptRenderer"/>, which <c>Infrastructure</c> cannot see.
/// </para>
/// <para>
/// <b>A new folder, built under a <c>.partial</c> name and renamed when whole.</b> It never writes
/// into a folder that already holds something of the person's, and a crash leaves a name that says
/// it is unfinished. An export that threw leaves its <c>.partial</c> folder where it is.
/// </para>
/// <para>
/// <b>A file the corpus records and the disk does not have is left out, named in <c>index.md</c>,
/// and does not stop the export.</b> A copy is checked by length against the row and never hashed:
/// reading every byte of hours of audio twice is what <c>check --verify-contents</c> is for.
/// Every other failure throws. It runs no integrity check: it reads rows through the model and
/// files through their rows, so a corpus SQLite cannot read fails at the first read.
/// </para>
/// <para>
/// A meeting on its way out (<see cref="LifecycleState.Deleting"/> or deleted) is not exported.
/// </para>
/// </remarks>
public static class CorpusExport
{
    /// <summary>The start of the folder an export is made in, followed by its instant.</summary>
    public const string FolderPrefix = "meeting-transcriber-export-";

    /// <summary>The suffix an export carries while it is being written.</summary>
    public const string PartialSuffix = ".partial";

    /// <summary>The file at the root that says what is in the export.</summary>
    public const string IndexName = "index.md";

    /// <summary>The record of what somebody settled by hand, one per meeting and one at the root.</summary>
    public const string HandCorrectionsName = "hand-corrections.json";

    private const int MostOfATitle = 80;

    private static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Exports every active meeting into a new folder inside <paramref name="chosen"/>.</summary>
    /// <remarks>
    /// The folder is a path and not a <see cref="DirectoryInfo"/>: a corpus and a folder are never
    /// taken as two arguments of one type pair here (<c>CorpusIsOneThingTests</c> holds every
    /// public member to it), and this folder is not the corpus's own — it is where the person
    /// wants a copy, so nothing about it can disagree with the corpus.
    /// </remarks>
    /// <param name="context">The corpus.</param>
    /// <param name="chosen">The full path of the folder the person picked. Nothing in it is touched.</param>
    /// <param name="kinds">What goes. Never empty.</param>
    /// <param name="zone">The zone a meeting's start is written in, in its folder's name.</param>
    /// <param name="now">The export's instant.</param>
    /// <exception cref="ArgumentException"><paramref name="kinds"/> is empty.</exception>
    /// <exception cref="IOException">The folder this would make is already standing.</exception>
    public static CorpusExported Into(
        CorpusDbContext context,
        string chosen,
        IReadOnlySet<ExportKind> kinds,
        TimeZoneInfo zone,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(chosen);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(zone);

        if (kinds.Count == 0)
        {
            throw new ArgumentException("An export with nothing ticked takes nothing out.", nameof(kinds));
        }

        var name = FolderPrefix + now.Value.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var finished = new DirectoryInfo(Path.Combine(chosen, name));
        var partial = new DirectoryInfo(finished.FullName + PartialSuffix);

        foreach (var standing in new[] { finished, partial })
        {
            if (standing.Exists || File.Exists(standing.FullName))
            {
                throw new IOException($"'{standing.FullName}' is already there, so this export would land on top of it.");
            }
        }

        partial.Create();

        var meetings = context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.LifecycleState == LifecycleState.Active)
            .AsEnumerable()
            .OrderBy(meeting => meeting.StartedAt)
            .ThenBy(meeting => meeting.Id)
            .ToArray();

        var ids = meetings.Select(meeting => meeting.Id).ToArray();
        var artifacts = context.Artifacts
            .AsNoTracking()
            .Where(artifact => ids.Contains(artifact.MeetingId))
            .AsEnumerable()
            .ToLookup(artifact => artifact.MeetingId);

        var reading = new MeetingReading(context, TimeProvider.System);
        var classifying = new MeetingClassifying(context, TimeProvider.System);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leftOut = new List<string>();
        var rows = new List<(string Folder, Meeting Meeting)>();

        foreach (var meeting in meetings)
        {
            var folder = new DirectoryInfo(Path.Combine(partial.FullName, Unused(used, FolderName(meeting, zone))));
            folder.Create();
            rows.Add((folder.Name, meeting));

            File.WriteAllText(
                Path.Combine(folder.FullName, MeetingManifest.FileName),
                MeetingManifest.Serialise(MeetingManifest.Of(meeting)),
                Utf8);

            foreach (var artifact in artifacts[meeting.Id])
            {
                if (Carries(kinds, artifact.Kind) && !Copy(context.Root, folder, artifact))
                {
                    leftOut.Add(artifact.RelativePath);
                }
            }

            if (kinds.Contains(ExportKind.Transcripts) && !kinds.Contains(ExportKind.HandCorrections))
            {
                var turns = reading.EveryTurn(meeting.Id);
                if (turns.Count > 0)
                {
                    var bare = new TranscriptHeader(
                        meeting.Id,
                        meeting.StartedAt,
                        meeting.Language,
                        meeting.Title,
                        Context: null,
                        Names: new Dictionary<string, string>(),
                        Corrections: []);

                    File.WriteAllText(
                        Path.Combine(folder.FullName, TranscriptFile),
                        TranscriptRenderer.Render(bare, turns).Markdown,
                        Utf8);
                }
            }

            if (kinds.Contains(ExportKind.HandCorrections))
            {
                Write(Path.Combine(folder.FullName, HandCorrectionsName), ByMeeting(context, classifying, meeting));
            }
        }

        if (kinds.Contains(ExportKind.HandCorrections))
        {
            Write(Path.Combine(partial.FullName, HandCorrectionsName), AtTheRoot(context, classifying));
        }

        var ticked = kinds.Order().ToArray();
        File.WriteAllText(Path.Combine(partial.FullName, IndexName), Index(now, ticked, rows, leftOut), Utf8);

        Directory.Move(partial.FullName, finished.FullName);

        // Last, and after the rename: an export that threw never becomes the last one.
        try
        {
            new CorpusSettings(context).Exported(new LastExport(now, ticked, meetings.Length, finished.FullName));
        }
        catch (DbUpdateException notRemembered)
        {
            // The export is whole and in its final place; only the line about it could not be kept.
            // Saying the screen's generic failure would send somebody looking for an export that is
            // there, so the message names the folder.
            throw new IOException(
                $"The export is whole in '{finished.FullName}', but the corpus could not remember it: {notRemembered.Message}",
                notRemembered);
        }

        return new CorpusExported(finished, meetings.Length, leftOut);
    }

    private const string TranscriptFile = "transcript.md";

    /// <summary>
    /// Which tick an artifact goes under, or nothing for what the export never carries: the card
    /// (it writes its own), the machine's copy of the turns and the spool.
    /// </summary>
    private static ExportKind? KindOf(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Audio => ExportKind.Audio,
        ArtifactKind.DeepgramResponse => ExportKind.Transcripts,
        ArtifactKind.Transcript => ExportKind.Transcripts,
        ArtifactKind.Extraction => ExportKind.Summaries,

        // Nothing writes a row of this kind yet. It goes with the summaries once something does.
        ArtifactKind.Summary => ExportKind.Summaries,
        ArtifactKind.Manifest => null,
        ArtifactKind.Utterances => null,
        ArtifactKind.SpoolBlock => null,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown artifact kind."),
    };

    /// <summary>
    /// Whether an artifact is copied. The corpus's own <c>transcript.md</c> carries names, corrected
    /// words and the context note, so it goes only with the hand corrections.
    /// </summary>
    private static bool Carries(IReadOnlySet<ExportKind> kinds, ArtifactKind artifact) =>
        KindOf(artifact) is { } kind
        && kinds.Contains(kind)
        && (artifact is not ArtifactKind.Transcript || kinds.Contains(ExportKind.HandCorrections));

    /// <summary>Copies one artifact. False when the disk did not have it, or had something of another length.</summary>
    private static bool Copy(DirectoryInfo root, DirectoryInfo folder, Artifact artifact)
    {
        var prefix = $"{CorpusFiles.Meetings}/{artifact.MeetingId}/";
        if (!artifact.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{artifact.RelativePath}' is recorded against meeting {artifact.MeetingId} and is not under '{prefix}'.");
        }

        var source = CorpusFiles.Locate(root, artifact.RelativePath);
        if (!source.Exists)
        {
            return false;
        }

        var destination = CorpusFiles.Locate(folder, artifact.RelativePath[prefix.Length..]);
        destination.Directory!.Create();
        source.CopyTo(destination.FullName, overwrite: false);

        destination.Refresh();
        if (destination.Length == artifact.ByteSize)
        {
            return true;
        }

        File.Delete(destination.FullName);
        return false;
    }

    private static string FolderName(Meeting meeting, TimeZoneInfo zone)
    {
        var start = TimeZoneInfo.ConvertTime(meeting.StartedAt.Value, zone)
            .ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);

        if (meeting.Title is not { } title)
        {
            return start;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. title.Select(letter => Array.IndexOf(invalid, letter) >= 0 ? '-' : letter)]);
        if (safe.Length > MostOfATitle)
        {
            safe = safe[..MostOfATitle];
        }

        safe = safe.TrimEnd('.', ' ').TrimStart(' ');
        return safe.Length == 0 ? start : $"{start} {safe}";
    }

    private static string Unused(HashSet<string> used, string wanted)
    {
        var name = wanted;
        for (var again = 2; !used.Add(name); again++)
        {
            name = $"{wanted} ({again})";
        }

        return name;
    }

    private static string Index(
        UtcTimestamp now,
        IReadOnlyList<ExportKind> kinds,
        IReadOnlyList<(string Folder, Meeting Meeting)> rows,
        IReadOnlyList<string> leftOut)
    {
        var index = new StringBuilder();
        index.Append("---\n");
        index.Append(CultureInfo.InvariantCulture, $"exported_at: {now}\n");
        index.Append(CultureInfo.InvariantCulture, $"kinds: [{string.Join(", ", kinds.Select(WireNames<ExportKind>.Of))}]\n");
        index.Append(CultureInfo.InvariantCulture, $"meetings: {rows.Count}\n");
        index.Append("---\n\n");
        index.Append("| folder | meeting_id | started_at | title |\n");
        index.Append("| --- | --- | --- | --- |\n");

        foreach (var (folder, meeting) in rows)
        {
            index.Append(CultureInfo.InvariantCulture, $"| {Cell(folder)} | {meeting.Id} | {meeting.StartedAt} | {Cell(meeting.Title ?? string.Empty)} |\n");
        }

        if (leftOut.Count > 0)
        {
            index.Append("\n## left out\n\n");
            foreach (var path in leftOut)
            {
                index.Append(CultureInfo.InvariantCulture, $"- {path}\n");
            }
        }

        return index.ToString();
    }

    private static string Cell(string text) => text
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static void Write(string path, object content) =>
        File.WriteAllText(path, JsonSerializer.Serialize(content, Readable), Utf8);

    private static MeetingCorrections ByMeeting(CorpusDbContext context, MeetingClassifying classifying, Meeting meeting)
    {
        var names = context.People.AsNoTracking().ToDictionary(person => person.Id, person => person.DisplayName);

        var voices = context.SpeakerAssignments
            .AsNoTracking()
            .Where(assignment => assignment.MeetingId == meeting.Id)
            .AsEnumerable()
            .OrderBy(assignment => assignment.SpeakerLabel, StringComparer.Ordinal)
            .Select(assignment => new Voice(
                assignment.SpeakerLabel,
                names[assignment.PersonId],
                WireNames<SpeakerAssignmentSource>.Of(assignment.AssignedBy)))
            .ToArray();

        var people = context.MeetingPeople
            .AsNoTracking()
            .Where(link => link.MeetingId == meeting.Id)
            .AsEnumerable()
            .Select(link => new Named(names[link.PersonId], WireNames<MeetingPersonRole>.Of(link.Role)))
            .OrderBy(named => named.Person, StringComparer.Ordinal)
            .ThenBy(named => named.Role, StringComparer.Ordinal)
            .ToArray();

        var filed = classifying.Filing(meeting.Id)
            .Select(filing => new Filed(WireNames<MeetingNodeRole>.Of(filing.Role), PathText(filing.Path)))
            .ToArray();

        var runs = context.ExtractionRuns
            .AsNoTracking()
            .Where(run => run.MeetingId == meeting.Id)
            .Select(run => run.Id)
            .ToArray();

        var progress = context.ActionItemProgress
            .AsNoTracking()
            .Where(row => runs.Contains(row.ExtractionRunId))
            .AsEnumerable()
            .OrderBy(row => row.ExtractionRunId)
            .ThenBy(row => row.Ordinal)
            .Select(row => new Progress(
                row.ExtractionRunId,
                row.Ordinal,
                WireNames<ActionItemState>.Of(row.State),
                row.OwnerPersonId is { } owner ? names[owner] : null))
            .ToArray();

        var corrections = context.TerminologyCorrections
            .AsNoTracking()
            .Where(correction => correction.MeetingId == meeting.Id)
            .AsEnumerable()
            .OrderBy(correction => correction.WrongText, StringComparer.Ordinal)
            .ThenBy(correction => correction.Id)
            .Select(correction => new Correction(
                correction.WrongText,
                correction.CorrectText,
                WireNames<TerminologyMatchMode>.Of(correction.MatchMode)))
            .ToArray();

        var summaryShown = new MeetingReading(context, TimeProvider.System).SummaryShown(meeting.Id);

        return new MeetingCorrections(meeting.Id, meeting.Context, voices, people, filed, progress, corrections, summaryShown);
    }

    private static RootCorrections AtTheRoot(CorpusDbContext context, MeetingClassifying classifying)
    {
        var organizations = context.Nodes.AsNoTracking().ToDictionary(node => node.Id, node => node.Name);
        var affiliations = context.Affiliations.AsNoTracking().AsEnumerable().ToLookup(row => row.PersonId);

        var people = context.People
            .AsNoTracking()
            .AsEnumerable()
            .OrderBy(person => person.DisplayName, StringComparer.Ordinal)
            .ThenBy(person => person.Id)
            .Select(person => new PersonRow(
                person.Id,
                person.DisplayName,
                person.IsMe,
                [
                    .. affiliations[person.Id]
                        .OrderBy(row => row.StartedAt)
                        .ThenBy(row => row.Id)
                        .Select(row => new Belonging(
                            organizations[row.OrganizationId],
                            row.StartedAt?.ToStorage(),
                            row.EndedAt?.ToStorage())),
                ]))
            .ToArray();

        var corrections = context.TerminologyCorrections
            .AsNoTracking()
            .Where(correction => correction.MeetingId == null)
            .AsEnumerable()
            .OrderBy(correction => correction.WrongText, StringComparer.Ordinal)
            .ThenBy(correction => correction.Id)
            .Select(correction => new ScopedCorrection(
                correction.WrongText,
                correction.CorrectText,
                WireNames<TerminologyMatchMode>.Of(correction.MatchMode),
                correction.NodeId is { } node ? PathText(classifying.PathTo(node)) : Everywhere))
            .ToArray();

        return new RootCorrections(people, corrections);
    }

    private const string Everywhere = "everywhere";

    private static string PathText(NodePath path) => string.Join(" / ", path.Nodes.Select(node => node.Name));

    private sealed record Voice(string SpeakerLabel, string Person, string AssignedBy);

    private sealed record Named(string Person, string Role);

    private sealed record Filed(string Role, string Path);

    private sealed record Progress(Guid ExtractionRunId, int Ordinal, string State, string? Owner);

    private sealed record Correction(string Wrong, string Right, string MatchMode);

    /// <param name="SummaryShown">
    /// The run whose output is the export's <c>extractions/&lt;id&gt;.json</c> when the export also
    /// carries the summaries, and which the meeting showed — a choice somebody may have made by
    /// putting an earlier summary back. Null where no summary was accepted.
    /// </param>
    private sealed record MeetingCorrections(
        Guid MeetingId,
        string? Context,
        IReadOnlyList<Voice> Voices,
        IReadOnlyList<Named> People,
        IReadOnlyList<Filed> FiledUnder,
        IReadOnlyList<Progress> ActionProgress,
        IReadOnlyList<Correction> Corrections,
        Guid? SummaryShown);

    private sealed record Belonging(string Organization, string? From, string? Until);

    private sealed record PersonRow(Guid PersonId, string Name, bool IsMe, IReadOnlyList<Belonging> Affiliations);

    private sealed record ScopedCorrection(string Wrong, string Right, string MatchMode, string Scope);

    private sealed record RootCorrections(IReadOnlyList<PersonRow> People, IReadOnlyList<ScopedCorrection> Corrections);
}
