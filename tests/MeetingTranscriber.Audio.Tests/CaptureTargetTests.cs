namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// What a channel says it is listening to. Only the naming is here: opening either kind really
/// does open a stream, and that is a probe somebody runs with <c>capture</c>.
/// </summary>
public class CaptureTargetTests
{
    /// <summary>
    /// It is what the recording reports and what a person reads back, and a process id is part of
    /// it because a machine runs a dozen processes of the same name.
    /// </summary>
    [Fact]
    public void A_target_names_what_a_person_would_call_it()
    {
        new CaptureTarget.Program(new AudioProcess(1000, "msedge", 900)).Name.ShouldBe("msedge (pid 1000)");
        new CaptureTarget.Endpoint(new AudioDevice("{id}", "Altavoces", true)).Name.ShouldBe("Altavoces");
    }
}
