namespace MeetingTranscriber.Cli;

/// <summary>
/// One live run's claim over the folder its responses land in, held for as long as the run lasts.
/// </summary>
/// <remarks>
/// <para>
/// What it stops is two runs pointed at one <c>--out</c> paying for the same audio. Each reads the
/// folder to decide what is already answered, so two started together both see nothing answered,
/// both pass the ceiling and both buy every file — and the run stamps keep the files from
/// colliding, which is exactly what would hide it. A listing is not a claim; a handle is.
/// </para>
/// <para>
/// <c>ReadingMark</c> and <c>SavingMark</c> are the precedent, and this is the refusing kind like
/// the second of them: joining would be two runs spending together, which is the thing itself. It
/// does not share their mechanism — <c>HeldMark</c> — because that one is
/// <see langword="internal"/> to <c>MeetingTranscriber.Audio</c> and there is no
/// <c>InternalsVisibleTo</c> in this repository to reach it through. What is copied instead is
/// every decision it had already made and written down: <b>held-ness and not existence</b>, so a
/// mark a process that died left behind says nothing and is written over rather than read as a
/// holder; and the short wait, for the reason below.
/// </para>
/// <para>
/// <b>It waits before it refuses, and the wait is not about a second run.</b> A second run holds
/// this for the length of a whole run, which is minutes, so no wait this side of absurd could
/// outlast one. What the wait is for is the handle that is not a holder at all — a sync client, a
/// backup or a scanner passing over a new file in a folder somebody chose, which is a real thing
/// in a user's own folder and is the case <c>HeldMark</c>'s own two seconds were written for. Refusing
/// a run over one of those would cost somebody a send and send them looking for a run that is not
/// there.
/// </para>
/// <para>
/// Public, and not by preference: there is no <c>InternalsVisibleTo</c> anywhere in this
/// repository, so an internal one could not be held by the probe that proves a second run is
/// refused. <see cref="LiveCheck"/> is public for that same reason.
/// </para>
/// <para>
/// <b>The mark is not a response and cannot be read as one.</b> <see cref="LiveCheck.Of"/> lists
/// the folder as <c>*.json</c>, so a <c>.mark</c> is invisible to the rule that decides what need
/// not be bought — which is the one thing a file this writes into that folder must not disturb.
/// </para>
/// <para>
/// The file is left on disk when a run ends, well or badly, and nothing clears it, which is
/// <c>SavingMark</c>'s answer and holds for the same reason: what says a run is under way is that
/// the file is <em>held</em> and never that it is there, so a stale empty file is exactly what a
/// folder nobody is sending into looks like. Removing it would be this command taking a file out of
/// somebody's folder on its own account, over a folder whose whole contents are things they paid
/// for.
/// </para>
/// <para>
/// <b>What all of it rests on is a file system that enforces share modes</b>, which is a local
/// volume. <c>--out</c> is any path somebody types, so it can be a network share or a container
/// mount — and one that does not enforce them lets both runs take the claim and both pay, silently,
/// exactly as before this existed. That is the condition under which this guard is not there, and
/// it is on the type because nothing at the prompt can detect it.
/// </para>
/// </remarks>
public sealed class SendingMark : IDisposable
{
    /// <summary>What the mark is called, inside the folder the responses land in.</summary>
    /// <remarks>
    /// Named for the sending rather than for the folder being in use, the way the audio engine's
    /// three marks are each named for what is happening. The extension is deliberately not
    /// <c>.json</c>: everything else this folder holds is a paid response, and
    /// <see cref="LiveCheck.Of"/> reads that name back.
    /// </remarks>
    public const string FileName = "sending.mark";

    /// <summary>
    /// How long a claim waits out a handle that is not a holder — see the type's remarks for why
    /// there is one at all, given that a real holder holds this for minutes.
    /// </summary>
    private static readonly TimeSpan WaitsOutSomethingPassingOver = TimeSpan.FromSeconds(2);

    private readonly FileStream held;

    private SendingMark(FileStream held) => this.held = held;

    /// <summary>
    /// Claims <paramref name="folder"/> for one live run and holds it until this is let go of.
    /// </summary>
    /// <remarks>
    /// The claim is the file system's and not a question asked a moment ago: two runs started
    /// together would both read the listing as unanswered. Whichever gets the handle sends; the
    /// other is refused here, before the folder has been listed and before anything has been sent.
    /// </remarks>
    /// <param name="folder">Where the responses land.</param>
    /// <exception cref="CommandException">
    /// The folder would not take the claim — a live run over it already under way, or something
    /// else the message carries in the file system's own words rather than asserting.
    /// </exception>
    public static SendingMark Take(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var mark = Path.Combine(folder.FullName, FileName);
        var waitingSince = Environment.TickCount64;

        while (true)
        {
            try
            {
                // Written over rather than opened, for `HeldMark`'s reason: what may already be
                // there is a mark a run that died left, and that one says nothing about anybody.
                // `FileShare.Read` is what excludes the second run: it permits a second handle to
                // ask for read access and no more, and a claim asks to write. It is not a promise
                // that anything can read the file while a run holds it — measured, a reader asking
                // `FileShare.Read` as `File.OpenRead` does is itself refused, and only one asking
                // `FileShare.ReadWrite` gets in. Nothing in this application reads it either way:
                // what this is for is the handle.
                return new SendingMark(
                    new FileStream(
                        mark, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 1));
            }

            // Before the wait and not inside it, because this one is an `IOException` too and
            // waiting it out would spend two seconds over a folder that is not coming back. It also
            // is not a second run: the command refuses a missing `--out` in its own words a few
            // lines above this call, so what this is, is a folder that went away in between —
            // `Cli.IsRefusal` carries `IOException`, so it comes out as the file system's own true
            // sentence rather than as a claim about somebody else sending.
            catch (DirectoryNotFoundException)
            {
                throw;
            }
            catch (IOException) when (
                Environment.TickCount64 - waitingSince < WaitsOutSomethingPassingOver.TotalMilliseconds)
            {
                Thread.Sleep(25);
            }
            catch (Exception wouldNotTakeIt)
                when (wouldNotTakeIt is IOException or UnauthorizedAccessException)
            {
                // What refused is carried and not summarised, which is the rule the rename in
                // `DeepgramCommands` was corrected to on this same branch: a second run is what this
                // almost always is and is not what has been checked — a read-only folder, a full
                // disk and a path too long all arrive here too — so the one sentence somebody gets
                // about a run that sent nothing has to hold the cause it really had.
                // `CommandException` carries no inner exception, so the text goes in the message or
                // it is gone.
                throw new CommandException(
                    $"'{folder.FullName}' could not be claimed for this run, so nothing was sent: "
                    + $"{wouldNotTakeIt.Message} A live run holds a '{FileName}' in the folder its "
                    + "responses land in for as long as it lasts, because what is in that folder is "
                    + "what decides which files need not be bought — two runs over one folder would "
                    + "both read it as unanswered and both pay for the same audio. If another run "
                    + "is under way, let it finish or point this one at a folder of its own.");
            }
        }
    }

    /// <summary>Lets the mark go, which is what says the run is over.</summary>
    public void Dispose() => held.Dispose();
}
