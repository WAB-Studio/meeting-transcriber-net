namespace MeetingTranscriber.Recording;

/// <summary>
/// The line of writes a screen makes to the corpus: each waits for the one asked before it, so two
/// of them are never two migrations of a folder with no schema racing for one write lock.
/// </summary>
/// <remarks>
/// <para>
/// Here and not in the settings screen it came from, for the reason <see cref="RecordingClock"/> is
/// beside the window and not in it: the rule is what a build agent can run, and a rule that lives
/// beside a WinUI tree is one nothing proves. What the screen keeps is deciding what to write.
/// </para>
/// <para>
/// Asking and chaining happen on the thread the screen draws on, so there is no second thread to
/// race the line itself; what runs inside <see cref="Run"/> is the caller's to put where it likes.
/// A write that failed leaves the line as it was for the next one, and only its own caller sees the
/// failure.
/// </para>
/// </remarks>
public sealed class WritesInTurn
{
    private Task _last = Task.CompletedTask;

    /// <summary>
    /// Runs <paramref name="write"/> once every write asked before it has finished, and finishes
    /// when it does.
    /// </summary>
    public async Task Run(Func<Task> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        var done = new TaskCompletionSource();
        var before = Interlocked.Exchange(ref _last, done.Task);

        try
        {
            // `before` is a signal that is only ever completed and never faulted, so a write that
            // failed is seen by its own caller and by nobody waiting behind it.
            await before.ConfigureAwait(true);
            await write().ConfigureAwait(true);
        }
        finally
        {
            done.SetResult();
        }
    }
}

/// <summary>
/// Which choice of one question is the newest, so a write that comes up in turn and finds a newer
/// choice waiting writes nothing: the last choice wins, and the one before it was never going to be
/// seen.
/// </summary>
/// <remarks>One per question — what happens after a recording, the model, the effort — never shared.</remarks>
public sealed class LastChoice
{
    private int _asked;

    /// <summary>Takes the number of a choice just made, which is now the newest.</summary>
    public int Ask() => Interlocked.Increment(ref _asked);

    /// <summary>Whether <paramref name="ask"/> is still the newest choice, with none made after it.</summary>
    public bool IsStill(int ask) => ask == Volatile.Read(ref _asked);
}
