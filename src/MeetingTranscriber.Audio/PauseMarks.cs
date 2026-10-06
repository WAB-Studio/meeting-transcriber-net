using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>What a line of <c>pauses.jsonl</c> says happened to the meeting.</summary>
public enum PauseMark
{
    /// <summary>The meeting stopped being recorded from here.</summary>
    Paused = 1,

    /// <summary>The meeting carried on from here.</summary>
    Resumed = 2,
}

/// <summary>One pause or resume, where it fell on the packets' clock and on the wall clock.</summary>
/// <param name="Kind">Which of the two it was.</param>
/// <param name="At">
/// The instant on the clock WASAPI stamps every packet with — <see cref="MonotonicInstant.Ticks"/>.
/// The recording is cut by this one, because it is the only clock the audio itself is measured on.
/// </param>
/// <param name="When">The same moment as a person reads it. Never used to cut anything.</param>
public sealed record PauseLine(PauseMark Kind, long At, UtcTimestamp When);

/// <summary>
/// The pauses of one recording, written beside its blocks and read back when they are poured into
/// the meeting's audio.
/// </summary>
/// <remarks>
/// <para>
/// A pause keeps spooling silent blocks (<see cref="RecordingPause"/>), so the blocks cannot say
/// which stretches were a pause. This file does: one line per change of state, appended and never
/// rewritten, in the order they happened. It follows <see cref="SpoolChanges"/>' rules — a line is
/// written whole, a reader shares the file with the writer, a torn last line is dropped, and a
/// whole line that will not read throws — because it is the same kind of file with the same kind of
/// writer.
/// </para>
/// <para>
/// Absent is the ordinary answer and means nothing was paused.
/// </para>
/// </remarks>
public static class RecordingPauses
{
    /// <summary>The name the pauses are stored under, beside the card.</summary>
    public const string FileName = RecordingFiles.Pauses;

    private static readonly Lock Appending = new();

    /// <summary>Where a folder's pauses are, whether or not anything was paused.</summary>
    public static FileInfo In(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }

    /// <summary>
    /// Writes one line behind everything already written. A failure throws, because the pause it
    /// would have recorded has then not happened: a meeting that went quiet without a line saying so
    /// would keep the silence.
    /// </summary>
    public static void Append(DirectoryInfo folder, PauseLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var file = In(folder);
        var kind = line.Kind.ToString().ToLowerInvariant();
        var bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Stored(kind, line.At, line.When.ToStorage())) + Environment.NewLine);

        try
        {
            lock (Appending)
            {
                using var stream = new FileStream(
                    file.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

                // What an append that threw left behind is not a line, and the next one must not
                // land on the back of it.
                var written = new byte[stream.Length];
                stream.ReadExactly(written);
                var kept = Array.LastIndexOf(written, (byte)'\n') + 1;
                stream.SetLength(kept);
                stream.Position = kept;

                stream.Write(bytes);
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' could not be written, so the meeting was not {kind}: {refused.Message}",
                refused);
        }
    }

    /// <summary>
    /// The stretches of <paramref name="folder"/>'s recording that were paused, as the packets'
    /// clock reads them: from the pause to the resume, or to nothing when it never resumed.
    /// </summary>
    /// <remarks>
    /// A resume with nothing paused is ignored, and so is a pause while one already stands: neither
    /// can be what this application wrote, and neither says anything the stretch does not.
    /// </remarks>
    public static IReadOnlyList<(long From, long? To)> Stretches(DirectoryInfo folder)
    {
        var file = In(folder);
        if (!file.Exists)
        {
            return [];
        }

        string written;
        try
        {
            using var stream = SpoolChanges.Reading(file);
            using var reading = new StreamReader(stream);
            written = reading.ReadToEnd();
        }
        catch (Exception held)
            when (held is IOException or UnauthorizedAccessException
                && held is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' could not be read, so which stretches of the recording in "
                + $"'{folder.FullName}' were paused is not known: {held.Message}",
                held);
        }

        var lines = written.Split('\n').Select(line => line.Trim('\r')).Where(line => line.Length > 0).ToArray();
        var mayBeTorn = !written.EndsWith('\n');

        var stretches = new List<(long From, long? To)>();
        long? since = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = Read(file, lines[index], isLast: mayBeTorn && index == lines.Length - 1);
            switch (line?.Kind)
            {
                case PauseMark.Paused when since is null:
                    since = line.At;
                    break;
                case PauseMark.Resumed when since is { } from:
                    stretches.Add((from, line.At));
                    since = null;
                    break;
            }
        }

        if (since is { } open)
        {
            stretches.Add((open, null));
        }

        return stretches;
    }

    private static PauseLine? Read(FileInfo file, string line, bool isLast)
    {
        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(line);
        }
        catch (JsonException) when (isLast)
        {
            return null;
        }
        catch (JsonException malformed)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' has a line that does not read as JSON, so which stretches of the "
                + $"recording were paused cannot be trusted: {malformed.Message}");
        }

        if (stored is null)
        {
            return null;
        }

        try
        {
            var kind = stored.Kind switch
            {
                "paused" => PauseMark.Paused,
                "resumed" => PauseMark.Resumed,
                _ => throw new FormatException($"'{stored.Kind}' is not a pause or a resume."),
            };

            return new PauseLine(
                kind,
                stored.At ?? throw new FormatException("A line names no instant."),
                UtcTimestamp.Parse(stored.When ?? throw new FormatException("A line names no time.")));
        }
        catch (Exception rejected) when (rejected is ArgumentException or FormatException)
        {
            throw new AudioCaptureException($"'{file.FullName}' has a line this build cannot read: {rejected.Message}");
        }
    }

    private sealed record Stored(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("at")] long? At,
        [property: JsonPropertyName("when")] string? When);
}
