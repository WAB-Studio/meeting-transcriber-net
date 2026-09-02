namespace MeetingTranscriber.Audio;

/// <summary>
/// One program running on this machine, as the thing channel 0 can be asked to follow.
/// </summary>
/// <param name="Id">The process id Windows knows it by, which is what the audio stack is given.</param>
/// <param name="Name">Its executable's name, without the extension, as Task Manager shows it.</param>
/// <param name="StartedBy">
/// The id of the process that started it. Kept because a meeting is rarely one process: a browser
/// renders its audio in a child, and following the tree is only possible from its root.
/// </param>
public sealed record AudioProcess(int Id, string Name, int StartedBy)
{
    /// <summary>How a person reads it back: the name they typed and the id that disambiguates it.</summary>
    /// <remarks>
    /// The recorder's source picker shows this, which is the one place left where a screen joins a
    /// name a machine gave to punctuation this application chose — the thing
    /// <see cref="AudioDevice.ToString"/> stopped doing on 2026-09-02. It stayed because the
    /// question underneath it is not the same one: <c>(default)</c> is an English word and
    /// <c>pid</c> is what Windows itself calls the number in either language, so moving this line
    /// into the catalogue means declaring the two versions equal on purpose — which
    /// <c>UiTextsTests.Reading_in_one_language_leaves_nothing_in_the_other</c> makes somebody say
    /// out loud, and nobody has. Issue #84 records it as what it did not carry.
    /// </remarks>
    public override string ToString() => $"{Name} (pid {Id})";
}
