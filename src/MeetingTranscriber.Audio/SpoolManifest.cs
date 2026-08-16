using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>What fed one channel, as the recording found it when it opened.</summary>
/// <param name="Channel">Which of the two channels this was.</param>
/// <param name="Heard">
/// What a person recording this would call it: the device's name, or the program channel 0 was
/// following and its process id.
/// </param>
/// <param name="DeviceId">
/// The endpoint this reopens by, or nothing when the channel was following a program rather than
/// listening to a device. It is the one field that says which of the two it was, so nothing else
/// stores that and nothing else can disagree with it.
/// </param>
public sealed record SpooledSource(AudioChannel Channel, string Heard, string? DeviceId);

/// <summary>
/// Which recording a spool folder holds, as the folder itself says it.
/// </summary>
/// <remarks>
/// It is fixed when the recording starts and never touched again. Everything that changes while a
/// meeting is being recorded — how far each source got, what survived — is in the blocks, where a
/// cut-off write costs one packet; a card rewritten as the recording went would be exactly the
/// torn write the spool format exists to avoid.
/// </remarks>
/// <param name="MeetingId">The meeting this recording is of.</param>
/// <param name="CaptureRunId">This run of it, so the row describing the run can be written again.</param>
/// <param name="StartedAt">When the recording started.</param>
/// <param name="Profile">What the audio will be transcribed as, which its channel count has to match.</param>
/// <param name="Sources">What fed each channel, in channel order.</param>
/// <param name="FellBack">
/// Why channel 0 is not following the program it was asked to, or nothing when it is — or when no
/// program was named.
/// </param>
public sealed record SpoolCard(
    Guid MeetingId,
    Guid CaptureRunId,
    UtcTimestamp StartedAt,
    SourceProfile Profile,
    IReadOnlyList<SpooledSource> Sources,
    string? FellBack)
{
    /// <summary>How channel 0 was obtained, read off what the card says fed it.</summary>
    /// <remarks>
    /// Derived rather than stored. A card carrying both this and the source beside it would have
    /// two answers to the same question, and the day they disagreed nothing would say which one
    /// the recording was actually made under.
    /// </remarks>
    public CaptureMode Mode => On(AudioChannel.Loopback).DeviceId is null
        ? CaptureMode.ProcessLoopback
        : CaptureMode.FullLoopback;

    /// <summary>What fed <paramref name="channel"/>.</summary>
    public SpooledSource On(AudioChannel channel) =>
        Sources.FirstOrDefault(source => source.Channel == channel)
        ?? throw new AudioContractException($"This card says nothing about the {channel} channel.");
}

/// <summary>
/// The card beside a recording's blocks: written once when the recording starts, read by whoever
/// is holding the folder afterwards and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The blocks are readable without it — each file declares its own format, which is the property
/// that keeps a source from having two ways to be lost. What the card adds is everything the
/// blocks cannot know: which meeting this was, when it started, and which device was on each
/// channel. That is what somebody is shown before they decide what happens to a recording the
/// application never got to stop, and deciding without it would be deciding about a folder.
/// </para>
/// <para>
/// Written once and refused a second time, which is the opposite of the card in a meeting's own
/// folder — that one is produced from the corpus every time and may be replaced. This one is the
/// only record of what was true when the devices opened, and there is no corpus to produce it
/// again from: a second write could only be something later overwriting the recording's own
/// account of itself.
/// </para>
/// </remarks>
public static class SpoolManifest
{
    /// <summary>The name the card is stored under, beside the blocks it describes.</summary>
    public const string FileName = "manifest.json";

    /// <summary>
    /// Indented and with accents left alone: this file is read by a person looking at a folder
    /// with no application to hand, which is the situation it exists for.
    /// </summary>
    private static readonly JsonSerializerOptions Readable = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The card of the recording in <paramref name="folder"/>, whether or not it is there.</summary>
    public static FileInfo In(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return new FileInfo(Path.Combine(folder.FullName, FileName));
    }

    /// <summary>
    /// Writes the card of a recording that is starting, refusing a folder that already has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file system answers rather than a check made a moment earlier, for the same reason a
    /// spool is created and not tested for: between the two there is a window in which the card
    /// being replaced is another recording's.
    /// </para>
    /// <para>
    /// The whole card reaches the file in one write, the way a block does. It is written at the
    /// instant a meeting starts, which is a moment a process really does die in, and a card
    /// serialised straight down the stream would leave half a sentence of JSON where the recording
    /// says what it is. A power cut can still tear it, and what covers that is the reader being
    /// allowed to say so rather than throw — a recording is not lost for the card beside it.
    /// </para>
    /// </remarks>
    public static void Write(DirectoryInfo folder, SpoolCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var file = In(folder);
        var written = JsonSerializer.SerializeToUtf8Bytes(Stored(card), Readable);

        FileStream stream;
        try
        {
            stream = new FileStream(file.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        catch (IOException taken) when (file.Exists)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' is already there, and what a recording says about itself is "
                + "written once. Record into a folder of its own.", taken);
        }

        try
        {
            using (stream)
            {
                stream.Write(written);
            }
        }
        catch
        {
            // The file was made here, so a card that never landed is this method's mess and not a
            // recording: what it would otherwise leave is a folder no later capture may record
            // into, refused for a card that never said anything.
            BlockSpool.Erase(file);
            throw;
        }
    }

    /// <summary>
    /// The card of the recording in <paramref name="folder"/>, or nothing when there is none.
    /// </summary>
    /// <remarks>
    /// Absent is an answer and not a failure: the blocks say what they hold on their own, so a
    /// folder whose card never landed still holds a recording somebody may want. A card that is
    /// there and cannot be read is a different thing entirely, and that one throws.
    /// </remarks>
    public static SpoolCard? Find(DirectoryInfo folder)
    {
        var file = In(folder);
        return file.Exists ? Read(file) : null;
    }

    /// <summary>Reads a card back with nothing else open — no corpus, no database, one file.</summary>
    /// <remarks>
    /// Every field is required. The whole worth of this file is being trusted when there is
    /// nothing left to check it against, so a card answering four of the five questions is one
    /// somebody would act on; what is genuinely allowed to be absent is why channel 0 fell back,
    /// because most recordings never asked it to follow anything.
    /// </remarks>
    public static SpoolCard Read(FileInfo file)
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
            throw new AudioCaptureException(
                $"'{file.FullName}' does not read as JSON, so it says nothing about the recording "
                + $"beside it: {malformed.Message}");
        }

        if (card is null)
        {
            throw new AudioCaptureException($"'{file.FullName}' holds nothing, so it names no recording.");
        }

        var profile = Parsed(
            file,
            "source_profile",
            () => SourceProfiles.FromWireName(Required(file, "source_profile", card.SourceProfile)));

        return new SpoolCard(
            Identifier(file, "meeting", card.Meeting),
            Identifier(file, "capture_run", card.CaptureRun),
            Parsed(file, "started_at", () => UtcTimestamp.Parse(Required(file, "started_at", card.StartedAt))),
            profile,
            Sources(file, card, profile),
            card.FellBack);
    }

    /// <summary>
    /// The sources in channel order, however the file happened to list them — one per channel the
    /// profile promises, and no channel twice.
    /// </summary>
    /// <remarks>
    /// The card's job is to say what fed <em>each</em> channel, so a list that names one of them,
    /// or names one of them twice, has stopped being able to do it. Both are what a card edited by
    /// hand or half rewritten looks like, and either would come back as a recording with a channel
    /// whose device nobody knows — or with the wrong one, which is worse. The count is the
    /// profile's own rule rather than a number written here.
    /// </remarks>
    private static SpooledSource[] Sources(FileInfo file, Card card, SourceProfile profile)
    {
        var listed = card.Sources ?? [];
        var sources = listed
            .Select(source => new SpooledSource(
                Parsed(
                    file,
                    "channel",
                    () => CapturedAudio.ChannelAt(
                        source.Channel ?? throw new AudioContractException("A source names no channel."))),
                Required(file, "heard", source.Heard),
                source.DeviceId))
            .OrderBy(source => CapturedAudio.IndexOf(source.Channel))
            .ToArray();

        Parsed(file, "sources", () =>
        {
            profile.EnsureChannelCount(sources.Length);
            return sources;
        });

        if (sources.Select(source => source.Channel).Distinct().Count() != sources.Length)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' names one of its channels twice, so it does not say what fed "
                + "the other one.");
        }

        return sources;
    }

    private static Card Stored(SpoolCard card) => new(
        card.MeetingId.ToString(),
        card.CaptureRunId.ToString(),
        card.StartedAt.ToStorage(),
        card.Profile.ToWireName(),
        [.. card.Sources.Select(source => new Source(
            CapturedAudio.IndexOf(source.Channel), source.Heard, source.DeviceId))],
        card.FellBack);

    private static Guid Identifier(FileInfo file, string field, string? value) =>
        Guid.TryParse(Required(file, field, value), out var identifier)
            ? identifier
            : throw new AudioCaptureException(
                $"'{file.FullName}' names '{value}' under '{field}', which is not an id.");

    private static string Required(FileInfo file, string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new AudioCaptureException(
                $"'{file.FullName}' says nothing under '{field}', so it does not identify the "
                + "recording this folder holds.")
            : value;

    /// <summary>
    /// Turns whatever a parser throws into the one exception that names the file and the field.
    /// Somebody holding a folder and no corpus has nothing else to go on.
    /// </summary>
    private static T Parsed<T>(FileInfo file, string field, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception rejected) when (rejected is AudioContractException or ArgumentException or FormatException)
        {
            throw new AudioCaptureException(
                $"'{file.FullName}' has a '{field}' this build cannot read: {rejected.Message}");
        }
    }

    /// <summary>
    /// The card as it is on disk. Separate from <see cref="SpoolCard"/> because that one holds the
    /// domain's own types and this one holds exactly the text the file carries — including the
    /// wrong text, which is what the reader has to be able to complain about.
    /// </summary>
    private sealed record Card(
        [property: JsonPropertyName("meeting")] string? Meeting,
        [property: JsonPropertyName("capture_run")] string? CaptureRun,
        [property: JsonPropertyName("started_at")] string? StartedAt,
        [property: JsonPropertyName("source_profile")] string? SourceProfile,
        [property: JsonPropertyName("sources")] IReadOnlyList<Source>? Sources,
        [property: JsonPropertyName("fell_back")] string? FellBack);

    private sealed record Source(
        [property: JsonPropertyName("channel")] int? Channel,
        [property: JsonPropertyName("heard")] string? Heard,
        [property: JsonPropertyName("device")] string? DeviceId);
}
