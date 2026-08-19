using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// A channel that started on one source and was moved to another while the meeting was running.
/// </summary>
/// <param name="At">When it moved.</param>
/// <param name="Channel">Which channel moved.</param>
/// <param name="Heard">What it listens to from here on, as a person would name it.</param>
/// <param name="DeviceId">
/// The endpoint it reopens by. Never absent, and that is the shape of what can happen rather than
/// a field nobody filled in: what a channel is ever moved to is a device. Somebody choosing a
/// program to follow is somebody starting a recording, so a change naming no device describes a
/// state this application cannot produce and is refused rather than read.
/// </param>
/// <param name="WasHearing">What it was listening to until then.</param>
public sealed record SourceChanged(
    UtcTimestamp At, AudioChannel Channel, string Heard, string DeviceId, string WasHearing);

/// <summary>
/// What somebody changed about a recording while it was being recorded, beside the card that says
/// what it started as.
/// </summary>
/// <remarks>
/// <para>
/// The card is written once and never touched again, so that what a folder says about itself is
/// what was true when the devices opened and cannot be half rewritten by a process that died. That
/// leaves nowhere for a change made an hour in — and there is one: channel 0 moves from the program
/// it was following to the whole machine when somebody chooses it. A recording whose folder still
/// named the program would tell whoever found it that their notifications are not in the file.
/// </para>
/// <para>
/// So it is a file of its own, appended to and never rewritten: one line per change, each line
/// whole on its own, in the order they happened. It is the card's rule applied to something that
/// happens more than once rather than an exception to it — a line that never landed costs the
/// account of one change, and every line before it still reads.
/// </para>
/// <para>
/// Absent is the ordinary answer. Most recordings change nothing, and this file is not written
/// until something does.
/// </para>
/// </remarks>
public static class SpoolChanges
{
    /// <summary>The name the changes are stored under, beside the card.</summary>
    public const string FileName = "changes.jsonl";

    /// <summary>Accents left alone, for the reason the card leaves them alone: a person reads this.</summary>
    private static readonly JsonSerializerOptions OneLine = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Where a folder's changes are, whether or not anything changed.</summary>
    public static FileInfo In(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }

    /// <summary>
    /// Writes down that a channel moved, behind everything already written down.
    /// </summary>
    /// <remarks>
    /// One line, in one write, for the reason a block is written in one: what a power cut may cost
    /// is the line being written and never a line already there. It is called after the channel has
    /// really moved, so a failure here is a recording that moved and could not say so — which is
    /// what it says, rather than being swallowed into looking like a move that never happened.
    /// </remarks>
    public static void Append(DirectoryInfo folder, SourceChanged change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var file = In(folder);
        var line = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(Stored(change), OneLine) + Environment.NewLine);

        try
        {
            using var stream = new FileStream(
                file.FullName, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(line);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw new AudioCaptureException(
                $"The {change.Channel} channel is recording '{change.Heard}' from here on, and "
                + $"'{file.FullName}' could not be written, so the folder still says it is "
                + $"recording '{change.WasHearing}': {refused.Message}",
                refused);
        }
    }

    /// <summary>
    /// What changed in <paramref name="folder"/> while it was being recorded, in the order it
    /// happened, which is nothing at all when nothing did.
    /// </summary>
    /// <remarks>
    /// A last line that never finished landing is dropped, the way a spool's last block is: it is
    /// what a machine dying mid-write leaves, and everything before it is still an account of the
    /// recording. What says a line never finished is that the file ends without ending the line —
    /// a complete line that will not read is not a torn write at all, it is a file that has stopped
    /// being what it says it is, and reading it as "nothing changed" would be this file failing in
    /// exactly the direction it exists to prevent. That one throws.
    /// </remarks>
    public static IReadOnlyList<SourceChanged> Find(DirectoryInfo folder)
    {
        var file = In(folder);
        if (!file.Exists)
        {
            return [];
        }

        var written = File.ReadAllText(file.FullName);
        var lines = written
            .Split('\n')
            .Select(line => line.Trim('\r'))
            .Where(line => line.Length > 0)
            .ToArray();

        // The last line of a file that does not end its last line, and nothing else. Every line
        // above one of those was whole before the next one was begun.
        var mayBeTorn = !written.EndsWith('\n');

        var changes = new List<SourceChanged>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var change = Read(file, lines[index], isLast: mayBeTorn && index == lines.Length - 1);
            if (change is not null)
            {
                changes.Add(change);
            }
        }

        return changes;
    }

    private static SourceChanged? Read(FileInfo file, string line, bool isLast)
    {
        Change? change;
        try
        {
            change = JsonSerializer.Deserialize<Change>(line, OneLine);
        }
        catch (JsonException torn) when (isLast)
        {
            // The write that was underway when the machine died. Dropped rather than complained
            // about: everything above it is what the recording changed, and this line is the change
            // whose account was lost — see the summary.
            _ = torn;
            return null;
        }
        catch (JsonException malformed)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' has a line that does not read as JSON, so what it says about "
                + $"the recording beside it cannot be trusted: {malformed.Message}");
        }

        if (change is null)
        {
            return null;
        }

        try
        {
            return new SourceChanged(
                UtcTimestamp.Parse(Required(file, "at", change.At)),
                CapturedAudio.ChannelAt(
                    change.Channel ?? throw new AudioContractException("A change names no channel.")),
                Required(file, "heard", change.Heard),
                Required(file, "device", change.DeviceId),
                Required(file, "was_hearing", change.WasHearing));
        }
        catch (Exception rejected)
            when (rejected is AudioContractException or ArgumentException or FormatException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' has a change this build cannot read: {rejected.Message}");
        }
    }

    private static string Required(FileInfo file, string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AudioCaptureException(
                $"'{file.FullName}' says nothing under '{field}', so it does not say what changed.")
            : value;

    private static Change Stored(SourceChanged change) => new(
        change.At.ToStorage(),
        CapturedAudio.IndexOf(change.Channel),
        change.Heard,
        change.DeviceId,
        change.WasHearing);

    /// <summary>
    /// A change as it is on disk, separate from <see cref="SourceChanged"/> for the reason the
    /// card's stored shape is separate: this one holds exactly the text the file carries, wrong
    /// text included.
    /// </summary>
    private sealed record Change(
        [property: JsonPropertyName("at")] string? At,
        [property: JsonPropertyName("channel")] int? Channel,
        [property: JsonPropertyName("heard")] string? Heard,
        [property: JsonPropertyName("device")] string? DeviceId,
        [property: JsonPropertyName("was_hearing")] string? WasHearing);
}
