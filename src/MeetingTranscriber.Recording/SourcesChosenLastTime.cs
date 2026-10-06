using System.Text.Json;

using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What somebody chose to record last time, as far as it can be recognised again: the microphone,
/// whether channel 0 followed the whole machine, and otherwise the program.
/// </summary>
/// <param name="MicrophoneId">The microphone's endpoint id, or nothing.</param>
/// <param name="TheWholeMachine">Whether channel 0 followed everything the machine plays.</param>
/// <param name="ProgramName">The executable name of the program channel 0 followed, or nothing.</param>
public sealed record LastSources(string? MicrophoneId, bool TheWholeMachine, string? ProgramName);

/// <summary>
/// The microphone and the source somebody chose, kept so that choosing them once is enough
/// (ISC-220.3).
/// </summary>
/// <remarks>
/// A file beside the language and the theme, for the reason <c>LanguageChoice</c> gives: a
/// preference re-picked in one click is not a source, and the corpus may not be reachable when the
/// window opens. Never the corpus, which could be carried to another machine with this one's
/// device id in it. A program is kept by its executable name and never by a process id, which does
/// not outlive the program, nor by a window title, which changes with a tab.
/// </remarks>
public sealed class SourcesChosenLastTime
{
    private static readonly LastSources Empty = new(null, TheWholeMachine: false, ProgramName: null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SourcesChosenLastTime(FileInfo location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    /// <summary>The file the choice is kept in.</summary>
    public FileInfo Location { get; }

    /// <summary>Where this user's choice is kept.</summary>
    public static SourcesChosenLastTime OfThisUser() => new(new FileInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber",
        "recorder-sources.json")));

    /// <summary>
    /// What was chosen, or <c>null</c> when nothing was. A file that is not there, cannot be read or
    /// does not parse reads as nothing: this is read before the window opens, and refusing to open
    /// over a preference is worse than offering the defaults.
    /// </summary>
    public LastSources? Read()
    {
        try
        {
            // The path and not `Location.Exists`, which a FileInfo caches: a read made before the first
            // write would go on saying nothing is there, and the write would forget the other field.
            if (!File.Exists(Location.FullName))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(Location.FullName));
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new LastSources(
                Text(root, "microphone"),
                root.TryGetProperty("wholeMachine", out var whole) && whole.ValueKind == JsonValueKind.True,
                Text(root, "program"));
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Records the microphone somebody chose, leaving what channel 0 follows as it was kept.</summary>
    /// <remarks>
    /// One field and never the whole choice: what is on screen may hold a default or a fallback the
    /// person never made, and writing it down would forget what they did choose (ISC-220.3).
    /// Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot,
    /// and the caller decides whether that is worth telling somebody.
    /// </remarks>
    public void KeepTheMicrophone(AudioDevice microphone)
    {
        ArgumentNullException.ThrowIfNull(microphone);
        Write((Read() ?? Empty) with { MicrophoneId = microphone.Id });
    }

    /// <summary>Records what channel 0 follows, leaving the microphone as it was kept. See <see cref="KeepTheMicrophone"/>.</summary>
    public void KeepTheSource(RecorderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write((Read() ?? Empty) with
        {
            TheWholeMachine = source.IsTheWholeMachine,
            ProgramName = source.Follow?.Name,
        });
    }

    private void Write(LastSources kept)
    {
        Location.Directory?.Create();
        File.WriteAllText(
            Location.FullName,
            JsonSerializer.Serialize(new Kept(kept.MicrophoneId, kept.TheWholeMachine, kept.ProgramName), Json));
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record Kept(string? Microphone, bool WholeMachine, string? Program);
}
