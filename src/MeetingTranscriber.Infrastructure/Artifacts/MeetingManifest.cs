using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>A recovery card that cannot be read as one, naming the file and what is wrong.</summary>
public sealed class ManifestException(string message) : Exception(message);

/// <summary>
/// Which meeting a folder holds, as the folder itself says it.
/// </summary>
/// <remarks>
/// Five fields and no more. They are what answers the one question a meeting's folder cannot
/// answer for itself — the files in it are named after what they hold, so a card listing them
/// would only repeat the directory listing. Everything else somebody typed is what a backup is
/// for: the card is a way to recognise a meeting without the database, not a second copy of it.
/// </remarks>
public sealed record MeetingCard(
    Guid MeetingId,
    UtcTimestamp StartedAt,
    SourceProfile Profile,
    string Language,
    string? Title);

/// <summary>
/// The card that makes a meeting recognisable when the database is gone: written wherever a
/// meeting is born, read back by whoever is holding a folder and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// It is a source by the rule in docs/corpus.md — a backup carries it and a deletion of the
/// derivatives leaves it alone — and it is the one source that may be written twice. Nothing is
/// destroyed by that, because the corpus produces its contents from the meetings row every time.
/// Making it write-once would be the unsafe choice, not the careful one: a card that cannot be
/// corrected keeps whatever it said the first time, and a meeting whose card was never written
/// would have no way left to get one.
/// </para>
/// <para>
/// It sits here, beside the write it is made of, rather than with the intake that first called it.
/// Nothing in it belongs to processing a response — it reads a meetings row and writes a file — and
/// what it has to stay reachable from is every place that changes one of the five fields. A card
/// that only ingestion could write would go stale the first time somebody renamed a meeting, with
/// the layer that renamed it unable to say so.
/// </para>
/// <para>
/// Written and read here rather than only written. A recovery card whose only parser lives in a
/// test is a card this product cannot actually recover from, and the round trip is what makes the
/// write mean something: it proves the file says what the corpus said, in a form something other
/// than the writer can get back out.
/// </para>
/// </remarks>
public static class MeetingManifest
{
    /// <summary>The name the card is stored under, which docs/corpus.md fixes.</summary>
    public const string FileName = "manifest.json";

    /// <summary>
    /// Indented and with accents left alone: this file is read by a person looking at a folder
    /// with no application to hand, which is the only situation it exists for.
    /// </summary>
    /// <remarks>
    /// A null title is written as <c>null</c> rather than left out. The card is the same five keys
    /// every time, so whatever reads it — a person, a script somebody wrote in an afternoon — does
    /// not have to treat an untitled meeting as a different shape of file, and an absent key stays
    /// available to mean what it should mean: this card was written by something older or by
    /// something else.
    /// </remarks>
    private static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes the card this meeting's row says it should have, replacing any card already there.
    /// </summary>
    public static Artifact Write(CorpusDbContext context, Guid meetingId, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);

        var meeting = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new ManifestException(
                $"There is no meeting {meetingId}, so there is nothing to write a recovery card about.");

        return DurableArtifact.WriteText(
            context,
            meetingId,
            ArtifactKind.Manifest,
            CorpusFiles.PathFor(meetingId, FileName),
            now,
            Serialise(Of(meeting)));
    }

    /// <summary>What the card of this meeting says.</summary>
    public static MeetingCard Of(Meeting meeting)
    {
        ArgumentNullException.ThrowIfNull(meeting);

        return new MeetingCard(
            meeting.Id,
            meeting.StartedAt,
            meeting.SourceProfile,
            meeting.Language,
            meeting.Title);
    }

    /// <summary>
    /// Reads a card back with nothing else open — no corpus, no database, one file.
    /// </summary>
    /// <remarks>
    /// The four fields that identify the meeting are required, and a card missing one is refused
    /// rather than returned with a gap: the whole worth of this file is being trusted when there is
    /// nothing left to check it against, and a card that answers three of the four questions is one
    /// somebody would act on. The title is the exception because a meeting is allowed not to have
    /// one, so an absent or null title reads as untitled and never as a card to refuse.
    /// </remarks>
    public static MeetingCard Read(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        Card? card;
        try
        {
            using var stream = file.OpenRead();
            card = JsonSerializer.Deserialize<Card>(stream, Readable);
        }
        catch (JsonException malformed)
        {
            throw new ManifestException(
                $"'{file.FullName}' does not read as JSON, so it is not a recovery card: {malformed.Message}");
        }

        if (card is null)
        {
            throw new ManifestException($"'{file.FullName}' holds nothing, so it names no meeting.");
        }

        return new MeetingCard(
            Guid.TryParse(Required(file, "meeting", card.Meeting), out var meetingId)
                ? meetingId
                : throw new ManifestException($"'{file.FullName}' names '{card.Meeting}' as its meeting, which is not an id."),
            Parsed(file, "started_at", () => UtcTimestamp.Parse(Required(file, "started_at", card.StartedAt))),
            Parsed(file, "source_profile", () => WireNames<SourceProfile>.Parse(Required(file, "source_profile", card.SourceProfile))),
            Required(file, "language", card.Language),
            card.Title);
    }

    private static string Serialise(MeetingCard card) => JsonSerializer.Serialize(
        new Card(
            card.MeetingId.ToString(),
            card.StartedAt.ToStorage(),
            WireNames<SourceProfile>.Of(card.Profile),
            card.Language,
            card.Title),
        Readable);

    private static string Required(FileInfo file, string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ManifestException(
                $"'{file.FullName}' says nothing under '{field}', so it does not identify the meeting this folder holds.")
            : value;

    /// <summary>
    /// Turns whatever a parser throws into the one exception that names the file and the field.
    /// A caller holding a folder and no corpus has nothing else to go on.
    /// </summary>
    private static T Parsed<T>(FileInfo file, string field, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception rejected) when (rejected is ArgumentException or InvalidOperationException)
        {
            throw new ManifestException($"'{file.FullName}' has a '{field}' this corpus cannot read: {rejected.Message}");
        }
    }

    /// <summary>
    /// The card as it is on disk. Separate from <see cref="MeetingCard"/> because that one holds
    /// the domain's own types, and this one holds exactly the text the file carries — including
    /// the wrong text, which is what the reader has to be able to complain about.
    /// </summary>
    private sealed record Card(
        [property: JsonPropertyName("meeting")] string? Meeting,
        [property: JsonPropertyName("started_at")] string? StartedAt,
        [property: JsonPropertyName("source_profile")] string? SourceProfile,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("title")] string? Title);
}
