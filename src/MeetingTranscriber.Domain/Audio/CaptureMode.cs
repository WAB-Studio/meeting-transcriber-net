namespace MeetingTranscriber.Domain.Audio;

/// <summary>How channel 0 was obtained.</summary>
public enum CaptureMode
{
    /// <summary>The selected process and, where Windows supports it, its child processes.</summary>
    ProcessLoopback = 1,

    /// <summary>
    /// Everything the machine plays. The fallback when the chosen process produces no audio or
    /// the API is unavailable, and it can pick up notifications and other applications.
    /// </summary>
    FullLoopback = 2,
}
