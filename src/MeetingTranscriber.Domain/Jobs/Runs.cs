using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Domain.Jobs;

/// <summary>What a recording session actually used, and how it ended.</summary>
public class CaptureRun
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public UtcTimestamp StartedAt { get; set; }

    public UtcTimestamp? FinishedAt { get; set; }

    // 'Others' is channel 0 and 'Me' is channel 1. The names follow the contract so neither
    // property can be read as the other source.
    public string? OthersDeviceId { get; set; }

    public string? OthersDeviceName { get; set; }

    public CaptureMode OthersCaptureMode { get; set; }

    public string? OthersProcess { get; set; }

    public string? MeDeviceId { get; set; }

    public string? MeDeviceName { get; set; }

    public int SampleRate { get; set; }

    public int ChannelCount { get; set; }

    public int BitsPerSample { get; set; }

    /// <summary>Divergence accumulated between the two clocks, measured at the end of the run.</summary>
    public Duration? Drift { get; set; }

    /// <summary>True when this run came back from a spool after an abrupt exit.</summary>
    public bool Recovered { get; set; }

    public string? LastError { get; set; }
}

/// <summary>
/// A channel that stopped following what it opened on, and the instant it stopped. Channel 0
/// moved to the whole machine is the one this exists for: the run says what the recording opened
/// on and goes on saying it, so what the file holds from an instant onward is a fact of its own.
/// </summary>
/// <remarks>
/// <para>
/// It hangs off the meeting and not off the run, because the folder does: one spool folder is one
/// meeting, <c>changes.jsonl</c> is that folder's, and a meeting recovered and finished twice is
/// still the one folder. Hanging it off a run would make it unwritable exactly when
/// <c>MeetingRecordings.Ran</c> answers nothing — a card that was torn in half over a meeting with
/// more than one run — which is the recovery this most needs to survive.
/// </para>
/// <para>
/// The words are the recording's own, as a person would read them: what a channel moved to has no
/// id when it is the whole machine, so a name is all there is. A row on channel 0 is by
/// construction a move to the whole machine, because <c>CaptureSession.RecordTheWholeMachine</c> is
/// the only move that channel has and it refuses a second one. The day a channel 0 can move back to
/// a program, the mode belongs on the line in <c>changes.jsonl</c> first and on this row second —
/// deriving it here from the two names would be this file guessing at what the recording did.
/// </para>
/// <para>
/// It lives here beside <see cref="CaptureRun"/> because it is the same subject read at a different
/// instant: what a recording's channels were on. It is not a run and holds no state, so nothing
/// about where a job reaches applies to it.
/// </para>
/// </remarks>
public class CaptureSourceChange
{
    public Guid MeetingId { get; set; }

    /// <summary>When it moved, which is read where the move happened and never where it was asked for.</summary>
    public UtcTimestamp At { get; set; }

    public AudioChannel Channel { get; set; }

    /// <summary>What it listened to from here on.</summary>
    public required string Heard { get; set; }

    /// <summary>What it was listening to until then.</summary>
    public required string WasHearing { get; set; }

    /// <summary>
    /// The endpoint it reopens by from here on, or nothing when what it moved to is no device.
    /// Never set on channel 0: neither way of obtaining it is an endpoint, and the CHECK behind
    /// this column says so where the row lands.
    /// </summary>
    public string? DeviceId { get; set; }
}

/// <summary>One call to a transcription provider, and what it was allowed to cost.</summary>
/// <remarks>
/// It carries no state of its own. Where this call stands is the state of the job that runs it —
/// one row, moving only through <see cref="ProcessingJob"/>'s own methods, recovered by the same
/// restart. A copy here would be a second answer to "was this transcription charged for", writable
/// to anything, and the two would disagree the first time one of them was not updated.
/// </remarks>
public class TranscriptionRun
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    /// <summary>The job that runs it, and the only thing that says where it stands.</summary>
    public Guid JobId { get; set; }

    public required string Provider { get; set; }

    public string? Model { get; set; }

    public SourceProfile SourceProfile { get; set; }

    public required string Language { get; set; }

    /// <summary>
    /// With <see cref="BillableConfigHash"/>, the pair that decides whether a request has already
    /// been paid for. Not unique: a deliberate re-transcription repeats it under its own approval.
    /// </summary>
    public required string AudioSha256 { get; set; }

    public required string BillableConfigHash { get; set; }

    /// <summary>
    /// Millionths of a unit of currency. Money never lives in a floating point column, and the
    /// price table is versioned configuration rather than a constant that ages badly.
    /// </summary>
    public long? EstimatedCostMicros { get; set; }

    public string? Currency { get; set; }

    public string? PriceTableVersion { get; set; }

    public UtcTimestamp? ApprovedAt { get; set; }

    public Guid? ResponseArtifactId { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp? FinishedAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>One summary attempt, with everything needed to tell two attempts apart.</summary>
/// <remarks>Where it stands is its job's, for the reason <see cref="TranscriptionRun"/> gives.</remarks>
public class ExtractionRun
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    /// <summary>The job that runs it, and the only thing that says where it stands.</summary>
    public Guid JobId { get; set; }

    public required string Provider { get; set; }

    public string? ProviderVersion { get; set; }

    public string? Model { get; set; }

    public required string PromptVersion { get; set; }

    public required string SchemaVersion { get; set; }

    public required string InputHash { get; set; }

    public string? RawOutputHash { get; set; }

    public Guid? OutputArtifactId { get; set; }

    /// <summary>A new extraction never edits the one before it. Accepting one is what supersedes it.</summary>
    public UtcTimestamp? AcceptedAt { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public string? LastError { get; set; }
}
