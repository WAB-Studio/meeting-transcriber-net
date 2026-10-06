namespace MeetingTranscriber.Presentation;

/// <summary>
/// What the application leaves behind when something it did not expect goes wrong on its way down:
/// the exception's type, message and stack, one entry after another in a file of its own.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports a managed exception that nobody caught as a fault in a native module — a
/// stowed exception inside <c>CoreMessagingXP.dll</c> — and that report names no line of ours. So
/// the application writes down what it was told before it goes, and the file is the first thing to
/// read after a crash. It is <c>%LOCALAPPDATA%\MeetingTranscriber\crash-record.log</c>, which a
/// packaged build redirects to <c>%LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache\Local\
/// MeetingTranscriber\crash-record.log</c>. Never the corpus and never the folder the corpus is
/// in: it is a diagnostic, and a corpus folder holds what cannot be obtained again.
/// </para>
/// <para>
/// It writes the type, the message (cut to <see cref="LongestMessage"/> characters) and the stack
/// of each exception in the chain, and nothing else: not <c>Exception.Data</c>, not a transcript,
/// not a setting, and so never the Deepgram key, which no exception message here carries (a
/// message may still name a path). The file
/// moves to <c>.1</c> when it passes <see cref="LongestFile"/> bytes, and a second rollover finds
/// that copy there and writes nothing, so a fault that repeats every
/// frame cannot fill a disk. Writing never throws: a record that could not be written must not be
/// the second thing that went wrong.
/// </para>
/// </remarks>
public sealed class CrashRecord
{
    /// <summary>The file's name, and what a person looks for.</summary>
    public const string FileName = "crash-record.log";

    /// <summary>How much of an exception's message is kept.</summary>
    public const int LongestMessage = 500;

    /// <summary>How large the file may get before it starts again.</summary>
    public const long LongestFile = 256 * 1024;

    private const int LongestChain = 5;

    private readonly object _gate = new();
    private Exception? _last;

    public CrashRecord(FileInfo location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    /// <summary>The file the entries go in.</summary>
    public FileInfo Location { get; }

    /// <summary>This user's record, beside the language and theme choices and not in the corpus.</summary>
    public static CrashRecord OfThisUser() => new(new FileInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber",
        FileName)));

    /// <summary>
    /// Writes one entry for <paramref name="exception"/>, said to have happened in
    /// <paramref name="where"/>. The same exception object twice is written once: it is what a
    /// handler that recorded it and the application's own last-chance handler both see.
    /// </summary>
    public void Write(string where, Exception exception, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(where);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            lock (_gate)
            {
                if (ReferenceEquals(_last, exception))
                {
                    return;
                }

                _last = exception;

                Location.Directory?.Create();

                // Moved aside rather than deleted, so the first entry of a fault that repeats —
                // the one that names the cause — survives the hundredth.
                if (File.Exists(Location.FullName) && new FileInfo(Location.FullName).Length > LongestFile)
                {
                    File.Move(Location.FullName, Location.FullName + ".1", overwrite: false);
                }

                File.AppendAllText(Location.FullName, Entry(where, exception, at));
            }
        }
        catch (Exception)
        {
            // Every kind, and this is the one place that is right: nothing is left to say it to,
            // and a throw here would hide the exception being written.
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/>, and records anything it throws without catching it: the
    /// exception goes on up exactly as it would have, stack and all, so wrapping a handler changes
    /// what is written down and nothing about what happens.
    /// </summary>
    public void Guard(string where, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            body();
        }
        catch (Exception thrown) when (NoteAndLetItGoOn(where, thrown))
        {
            throw;
        }
    }

    private bool NoteAndLetItGoOn(string where, Exception thrown)
    {
        Write(where, thrown, TimeProvider.System.GetUtcNow());
        return false;
    }

    /// <summary>The text of one entry.</summary>
    public static string Entry(string where, Exception exception, DateTimeOffset at)
    {
        var text = new System.Text.StringBuilder()
            .Append(at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture))
            .Append("  ")
            .AppendLine(where);

        var current = exception;
        for (var depth = 0; current is not null && depth < LongestChain; depth++)
        {
            var message = current.Message.Length > LongestMessage
                ? current.Message[..LongestMessage] + "…"
                : current.Message;

            text.Append(depth == 0 ? string.Empty : "--- caused by ")
                .Append(current.GetType().FullName)
                .Append(": ")
                .AppendLine(message)
                .AppendLine(current.StackTrace ?? "(no stack)");

            current = current.InnerException;
        }

        return text.AppendLine().ToString();
    }
}
