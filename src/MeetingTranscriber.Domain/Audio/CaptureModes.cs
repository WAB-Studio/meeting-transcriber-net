namespace MeetingTranscriber.Domain.Audio;

/// <summary>The name a <see cref="CaptureMode"/> is written down under.</summary>
/// <remarks>
/// Here as well as in the corpus's own naming convention, and that is the shape
/// <see cref="SourceProfiles"/> already has: a recording's folder is read with no database open, so
/// what it says has to be spelled somewhere the storage layer is not. The two agree, and a test in
/// the corpus's naming suite is what says they still do.
/// </remarks>
public static class CaptureModes
{
    /// <summary>The name this mode is persisted under, in a corpus and beside a recording's blocks.</summary>
    public static string ToWireName(this CaptureMode mode) => mode switch
    {
        CaptureMode.OneProgram => "one_program",
        CaptureMode.WholeMachine => "whole_machine",
        _ => throw new AudioContractException($"Unknown capture mode '{mode}'."),
    };

    /// <summary>Reads back a mode persisted by <see cref="ToWireName"/>.</summary>
    public static CaptureMode FromWireName(string name) => name switch
    {
        "one_program" => CaptureMode.OneProgram,
        "whole_machine" => CaptureMode.WholeMachine,
        _ => throw new AudioContractException($"Unknown capture mode '{name}'."),
    };
}
