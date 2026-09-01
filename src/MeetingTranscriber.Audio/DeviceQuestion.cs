namespace MeetingTranscriber.Audio;

/// <summary>
/// One thing this application asks this machine about its devices, and what
/// <see cref="DeviceEnquiry"/> remembers a wedge against.
/// </summary>
/// <remarks>
/// <para>
/// A closed set and not a string, because these words have two readers and only one of them may
/// change. They end the sentence somebody is shown when Windows will not answer and they name the
/// thread in a debugger — both of which anybody may reword, and the second half of this branch is
/// somebody deciding that English in front of a person needs handling. What must survive that
/// rewording is the memory of a question given up on going on meaning the same question. One
/// string doing both jobs forks identity when a sentence is reworded, which puts the deadline back
/// into every look — the freeze this exists to end — and merges it when two questions are spelled
/// alike, which refuses one caller on another's wedge. Neither shows up as a failure anywhere.
/// </para>
/// <para>
/// Three, because three is what this application asks on nobody else's behalf. A fourth is one
/// more static here and needs nothing else: what makes a question the same question is being this
/// object, so there is no rule at a call site to get wrong and none to write down.
/// </para>
/// </remarks>
public sealed class DeviceQuestion
{
    private DeviceQuestion(string asked) => Asked = asked;

    /// <summary>Which microphones this machine has active.</summary>
    public static DeviceQuestion Microphones { get; } = new("the microphones on this machine");

    /// <summary>Which endpoint Windows is playing through.</summary>
    public static DeviceQuestion PlaybackDevice { get; } =
        new("the device this machine plays through");

    /// <summary>
    /// Being told when this machine's devices change. Asked once in a session, and bounded like
    /// the other two for the same reason: it is <c>CoCreateInstance</c> on the audio service,
    /// which a screen makes while somebody is looking at it. Being told to stop is not this
    /// question and is not bounded at all — <see cref="DeviceChanges.Dispose"/> says why.
    /// </summary>
    public static DeviceQuestion DeviceChanges { get; } =
        new("being told when this machine's devices change");

    /// <summary>
    /// What is being asked about, said the way a person would hear it and read as the end of a
    /// sentence about Windows not answering. Also what the thread is called in a debugger. Free to
    /// be reworded, since nothing is decided by it.
    /// </summary>
    public string Asked { get; }
}
