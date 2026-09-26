using System.Text.Encodings.Web;
using System.Text.Json;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// The bytes a provider is given to summarise a meeting, and the hash that pins them: the meeting
/// id, the SHA-256 of the response its turns came from, and every turn in ordinal order.
/// </summary>
/// <remarks>
/// <para>
/// These bytes are what a provider is handed and what #119's workspace holds. Preparing the same
/// meeting twice, with nothing changed in between, writes the same bytes, because
/// <see cref="ExtractionCheck"/> trusts <see cref="Hash"/> to say whether an extraction was made
/// from what the meeting says now rather than from what it said when the call was sent.
/// </para>
/// <para>
/// The authorised human context arquitectura.md §7.2 describes is left out of version 1 on purpose:
/// #119 adds it, and adding it changes the bytes, and so the hash, of runs prepared afterwards only.
/// </para>
/// </remarks>
public sealed record MeetingInput(Guid MeetingId, string TranscribedFrom, IReadOnlyList<Turn> Turns)
{
    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The one JSON object these turns become, written the same way every time: no indentation, the
    /// meeting id in its default lower-case form, accents written as themselves rather than escaped
    /// the way <see cref="MeetingManifest"/> writes them, and keys in exactly this order.
    /// </summary>
    public byte[] Bytes()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("meeting_id", MeetingId.ToString());
            writer.WriteString("transcribed_from", TranscribedFrom);
            writer.WriteStartArray("turns");
            foreach (var turn in Turns)
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", turn.Ordinal);
                writer.WriteNumber("start_ms", turn.Start.Milliseconds);
                writer.WriteNumber("end_ms", turn.End.Milliseconds);
                writer.WriteString("speaker_label", turn.SpeakerLabel);
                writer.WriteString("text", turn.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The SHA-256 of <see cref="Bytes"/>, through <see cref="CorpusFiles.Sha256Of(Stream)"/> so
    /// there is one spelling of the hash.
    /// </summary>
    public string Hash
    {
        get
        {
            using var stream = new MemoryStream(Bytes());
            return CorpusFiles.Sha256Of(stream);
        }
    }

    /// <summary>
    /// Reads the meeting's turns and what they were transcribed from, ready to be hashed and sent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The meeting has no turns yet, or does not say which response they came from.
    /// </exception>
    public static MeetingInput Prepare(CorpusDbContext context, Guid meetingId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reading = new MeetingReading(context, TimeProvider.System);
        var turns = reading.EveryTurn(meetingId);
        if (turns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Meeting {meetingId} has no turns, so there is nothing to summarise yet. It is "
                + "transcribed and rendered first.");
        }

        var transcribedFrom = reading.TranscribedFrom(meetingId)
            ?? throw new InvalidOperationException(
                $"Meeting {meetingId} does not say which response its turns came from, so a "
                + "citation would have nothing to name. Rendering it again records that.");

        return new MeetingInput(meetingId, transcribedFrom, turns);
    }
}
