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
    public override string ToString() => $"{Name} (pid {Id})";
}
