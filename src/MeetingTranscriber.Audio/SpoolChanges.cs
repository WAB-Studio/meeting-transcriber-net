using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>
/// A channel that started on one source and was moved to another while the meeting was running.
/// </summary>
/// <param name="At">When it moved.</param>
/// <param name="Channel">Which channel moved.</param>
/// <param name="Heard">What it listens to from here on, as a person would name it.</param>
/// <param name="WasHearing">What it was listening to until then.</param>
/// <param name="DeviceId">
/// The endpoint it reopens by from here on, or nothing when what it moved to is no device.
/// </param>
/// <remarks>
/// The device is the same field the card carries for what a channel opened on, and it is under the
/// same rule: a microphone is an endpoint and has one, and neither way of obtaining channel 0 is a
/// device, so a change naming a device on channel 0 is refused rather than reconciled. What a
/// channel moved to is always said in words as well, because words are all a channel 0 has.
/// </remarks>
public sealed record SourceChanged(
    UtcTimestamp At, AudioChannel Channel, string Heard, string WasHearing, string? DeviceId = null);

/// <summary>
/// What somebody changed about a recording while it was being recorded, beside the card that says
/// what it started as.
/// </summary>
/// <remarks>
/// <para>
/// The card is written once and never touched again, so that what a folder says about itself is
/// what was true when the devices opened and cannot be half rewritten by a process that died. That
/// leaves nowhere for a change made an hour in — and there are two. Channel 0 moves from the program
/// it was following to the whole machine when somebody chooses it: a recording whose folder still
/// named the program would tell whoever found it that their notifications are not in the file. And
/// a channel whose device was unplugged follows Windows to whatever replaced it, so the channel
/// comes to name two devices over one meeting — what the card says is the one it opened on, and
/// what says which device fed the rest of the meeting is here.
/// </para>
/// <para>
/// So it is a file of its own, appended to and never rewritten: one line per change, each line
/// whole on its own, in the order they happened. A line that is written down is never touched
/// again, and the one thing an append may take away is the unfinished tail an append that failed
/// left behind it. It is the card's rule applied to something that happens more than once rather
/// than an exception to it — a line that never landed costs the account of one change, and every
/// line before it still reads.
/// </para>
/// <para>
/// Absent is the ordinary answer. Most recordings change nothing, and this file is not written
/// until something does.
/// </para>
/// </remarks>
public static class SpoolChanges
{
    /// <summary>The name the changes are stored under, beside the card.</summary>
    public const string FileName = RecordingFiles.Changes;

    /// <summary>Accents left alone, for the reason the card leaves them alone: a person reads this.</summary>
    private static readonly JsonSerializerOptions OneLine = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// What makes two channels handing over at once two appends one after the other rather than one
    /// append and one refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both channels reach <see cref="Append"/> from their own capture callbacks, against one file,
    /// with a read-modify-truncate inside it. A dock or a hub coming out takes both endpoints in the
    /// same instant, and without this the second one in met the first one's handle, got a sharing
    /// violation, and had its move refused — so that channel recorded the rest of the meeting on an
    /// endpoint nobody was on, on timing alone.
    /// </para>
    /// <para>
    /// One lock and not one per folder: an append is a few bytes and happens a handful of times in a
    /// meeting, and this machine records one meeting at a time. A second recording waiting a
    /// microsecond costs nothing worth a dictionary keyed by path.
    /// </para>
    /// </remarks>
    private static readonly Lock Appending = new();

    /// <summary>Where a folder's changes are, whether or not anything changed.</summary>
    public static FileInfo In(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }

    /// <summary>
    /// Opens this file the way everything that reads it opens it: sharing it with the writer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The share mode is the rule and not a detail, and denying write sharing cost both directions
    /// at once. The one command somebody runs to find out what happened to a recording met a bare
    /// <see cref="IOException"/> exactly while a recording was in progress; and, the other way
    /// round, a listing holding this file for the length of one read made <see cref="Append"/>
    /// throw — which <see cref="Append"/> says means the move does not happen and the rest of the
    /// meeting records on an endpoint nobody is on.
    /// </para>
    /// <para>
    /// Sharing is safe because of what the file is. Nothing before the last line break ever moves:
    /// <see cref="Settle"/> truncates only bytes with no line break behind them, which were never a
    /// whole line, and a line is written in one call. So the worst a reader can see is a line half
    /// written, which is the tail <see cref="Find"/> already drops on purpose.
    /// </para>
    /// <para>
    /// Public because the only way to hold this rule is to take a handle the way a reader takes one
    /// and watch an append land through it. A test spelling the modes itself would go green against
    /// whatever this asked for.
    /// </para>
    /// </remarks>
    public static FileStream Reading(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    /// <summary>
    /// Writes down that a channel moved, behind everything already written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One line per write, for the reason a block is written in one: what a power cut may cost is
    /// the line being written and never a line already there. It is called before the channel hands
    /// over — the new device is running by then and its blocks are going nowhere — so a failure
    /// here is a move that does not happen, and that is what it says. The other order would leave a
    /// folder claiming audio a file does not hold, which is the lie worth preventing.
    /// </para>
    /// <para>
    /// One write is not enough on its own, because the failure is what makes somebody ask again. A
    /// write that landed in part and then threw leaves bytes no line break follows, and the retry
    /// used to land its JSON on the back of them as one complete line that will not read — the one
    /// shape <see cref="Find"/> refuses a whole folder over, so a disk that filled for two bytes
    /// cost every change the folder had. An append now drops what the one before it left unfinished
    /// before adding to it, which is what the file is opened for reading as well as writing for.
    /// What is lost is still at most the line being written, and it is a line nothing happened for.
    /// </para>
    /// </remarks>
    public static void Append(DirectoryInfo folder, SourceChanged change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var file = In(folder);
        Sound(file, change);

        var line = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(Stored(change), OneLine) + Environment.NewLine);

        try
        {
            lock (Appending)
            {
                using var stream = new FileStream(
                    file.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

                Settle(stream);
                stream.Write(line);
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw new AudioCaptureException(
                $"The {change.Channel} channel is still recording '{change.WasHearing}': "
                + $"'{file.FullName}' could not be written, and nothing moves before its folder "
                + $"has said so, because a folder naming a move that did not happen would tell "
                + $"whoever found it that '{change.Heard}' is in a file that never held it: "
                + $"{refused.Message}",
                refused);
        }
    }

    /// <summary>
    /// Leaves <paramref name="stream"/> at the end of the last line that finished landing, so that
    /// what is written next begins a line of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bytes with no line break behind them are dropped rather than finished, because of what put
    /// them there. An append is called before the channel hands over, so an append that threw is a
    /// move that did not happen: the channel is still on what it was on, and the tail is the
    /// account of a move this application refused. Terminating it would make the folder claim
    /// audio the file does not hold, and would leave the retry's line naming a source the line
    /// above it says the channel had already left.
    /// </para>
    /// <para>
    /// It is the only tail that reaches here. A folder torn by a machine dying is never appended
    /// to again — <see cref="BlockSpool.EnsureNothingRecordedIn"/> refuses a folder that already
    /// holds this file — so the reader goes on being the one that judges that one, and goes on
    /// keeping a last line that landed whole. The two rules differ because the two situations do:
    /// there, nobody refused the move.
    /// </para>
    /// <para>
    /// Only the tail. Everything at or before the last line break was whole before the next line
    /// was begun, and is never rewritten, moved or read back into the file.
    /// </para>
    /// </remarks>
    private static void Settle(FileStream stream)
    {
        var written = new byte[stream.Length];
        stream.ReadExactly(written);

        // -1 when there is no line break anywhere, which makes the whole file the tail. That is the
        // likeliest shape of all, since most folders only ever hold one line, and a guard returning
        // early on -1 would leave exactly that one glued to the retry that follows it.
        var lastBreak = Array.LastIndexOf(written, (byte)'\n');
        if (lastBreak + 1 == written.Length)
        {
            // The ordinary case, and an empty file: it ends where a line ends, so nothing is
            // settled and an append adds one line rather than a blank one and a line.
            return;
        }

        // The position is set as well as the length, rather than left to SetLength's own clamp,
        // because what is written next lands wherever this leaves it and a position of 0 would put
        // a change on top of a change already written down.
        stream.SetLength(lastBreak + 1);
        stream.Position = stream.Length;
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
    /// <para>
    /// <b>The read shares the file with the writer rather than locking it out.</b> A reader that
    /// drops a torn tail on purpose is a reader built to read a file being appended to, and denying
    /// write sharing cost both directions at once: the one command somebody runs to find out what
    /// happened to a recording met a bare <see cref="IOException"/> exactly while a recording was in
    /// progress, and a listing taken at the instant a channel handed over made <see cref="Append"/>
    /// throw — which <see cref="Append"/> says means the move does not happen and the rest of the
    /// meeting records on an endpoint nobody is on. Sharing removes both.
    /// </para>
    /// <para>
    /// What is left is a file that genuinely will not open, and that answers an
    /// <see cref="AudioCaptureException"/> naming the file and the folder, because every caller here
    /// turns one of those into a sentence rather than a stack trace.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SourceChanged> Find(DirectoryInfo folder)
    {
        var file = In(folder);
        if (!file.Exists)
        {
            return [];
        }

        string written;
        try
        {
            // `Reading` and not `File.ReadAllText`, which shares Read only — see `Reading` for what
            // that was costing in both directions.
            using var stream = Reading(file);
            using var reading = new StreamReader(stream);
            written = reading.ReadToEnd();
        }
        catch (Exception held)
            when (held is IOException or UnauthorizedAccessException
                && held is not FileNotFoundException and not DirectoryNotFoundException)
        {
            // What is left after the share mode is a file that genuinely will not open — a denied
            // permission, a disk that answered no. A folder that went between `file.Exists` and
            // here is a recording somebody discarded in another window, and saying "could not be
            // read" about a recording that is simply gone would be the wrong sentence, so those two
            // go on out as they are.
            throw new AudioCaptureException(
                $"'{file.FullName}' could not be read, so what the recording in "
                + $"'{folder.FullName}' changed while it was running is not known: {held.Message}",
                held);
        }

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
            return Sound(
                file,
                new SourceChanged(
                    UtcTimestamp.Parse(Required(file, "at", change.At)),
                    CapturedAudio.ChannelAt(
                        change.Channel ?? throw new AudioContractException("A change names no channel.")),
                    Required(file, "heard", change.Heard),
                    Required(file, "was_hearing", change.WasHearing),
                    change.DeviceId));
        }
        catch (Exception rejected)
            when (rejected is AudioContractException or ArgumentException or FormatException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' has a change this build cannot read: {rejected.Message}");
        }
    }

    /// <summary>
    /// The change, unless it says a channel 0 was moved onto a device. The card refuses the same
    /// thing about what a channel opened on, and for the same reason: neither way of obtaining
    /// channel 0 is an endpoint, so a line saying otherwise describes a recording this application
    /// cannot have made — and read anyway it would put an endpoint's id on a channel whose audio came
    /// from somewhere else entirely.
    /// </summary>
    private static SourceChanged Sound(FileInfo file, SourceChanged change)
    {
        if (change.Channel == AudioChannel.Loopback && change.DeviceId is not null)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' says the {AudioChannel.Loopback} channel moved onto device "
                + $"'{change.DeviceId}', and no device ever feeds it: what it holds is one "
                + "program's audio or everything the machine plays, and neither is an endpoint.");
        }

        return change;
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
        change.WasHearing,
        change.DeviceId);

    /// <summary>
    /// A change as it is on disk, separate from <see cref="SourceChanged"/> for the reason the
    /// card's stored shape is separate: this one holds exactly the text the file carries, wrong
    /// text included.
    /// </summary>
    private sealed record Change(
        [property: JsonPropertyName("at")] string? At,
        [property: JsonPropertyName("channel")] int? Channel,
        [property: JsonPropertyName("heard")] string? Heard,
        [property: JsonPropertyName("was_hearing")] string? WasHearing,
        [property: JsonPropertyName("device")] string? DeviceId);
}
