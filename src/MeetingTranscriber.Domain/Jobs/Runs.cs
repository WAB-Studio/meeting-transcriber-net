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

/// <summary>One call to a transcription provider, and what it was allowed to cost.</summary>
public class TranscriptionRun
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid? JobId { get; set; }

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

    public JobState State { get; set; } = JobState.Pending;

    public UtcTimestamp CreatedAt { get; set; }

    public UtcTimestamp? FinishedAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>One summary attempt, with everything needed to tell two attempts apart.</summary>
public class ExtractionRun
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid? JobId { get; set; }

    public required string Provider { get; set; }

    public string? ProviderVersion { get; set; }

    public string? Model { get; set; }

    public required string PromptVersion { get; set; }

    public required string SchemaVersion { get; set; }

    public required string InputHash { get; set; }

    public string? RawOutputHash { get; set; }

    public Guid? OutputArtifactId { get; set; }

    public JobState State { get; set; } = JobState.Pending;

    /// <summary>A new extraction never edits the one before it. Accepting one is what supersedes it.</summary>
    public UtcTimestamp? AcceptedAt { get; set; }

    public UtcTimestamp CreatedAt { get; set; }

    public string? LastError { get; set; }
}
