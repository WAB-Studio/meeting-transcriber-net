using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Letting go of one device's handles, on a thread that can be given up on.
/// </summary>
/// <remarks>
/// <para>
/// Releasing a WASAPI client, an endpoint or a playback is a synchronous call into the driver, and
/// a driver can wedge on being let go of exactly as it can wedge on being drained. Done on the
/// caller's thread, that is an application which will not close after a meeting somebody already
/// recorded — the same failure <see cref="CaptureLoop"/> exists for, one line further down.
/// </para>
/// <para>
/// So the deadline is <see cref="CaptureLoop.StopsWithin"/> and not a second number: what "did not
/// answer" means is one sentence everywhere, and a release given up on means what abandoning
/// anything here means — the thread is still inside those handles, so they are nobody's to touch.
/// </para>
/// <para>
/// A type of its own rather than another way into <see cref="CaptureLoop"/>, because the two agree
/// on the deadline and on nothing else. A draining loop is what a recording is being written by, so
/// it reads whether it was asked to stop, says when its device got going, and lets an exception
/// take the process rather than decide on its own that the meeting goes on without the audio. A
/// release is none of that. It is cleanup after a recording that is already on disk, it has nothing
/// to be asked and nothing to announce, and an exception from it is the case the source that owns
/// these handles has always swallowed on the way out — which on a thread with no boundary would
/// instead end the process, and take the other source's release down with it.
/// </para>
/// </remarks>
public sealed class DeviceRelease : IDisposable
{
    private readonly Thread thread;

    private DeviceRelease(string name, Action release) =>
        thread = new Thread(() => Run(release)) { IsBackground = true, Name = name };

    /// <summary>
    /// Whether this was given up on: the handles did not come back inside
    /// <see cref="CaptureLoop.StopsWithin"/>, so a thread is still inside them.
    /// </summary>
    public bool Abandoned { get; private set; }

    /// <summary>
    /// Starts letting go on a thread of its own and comes back without waiting.
    /// <see cref="Dispose"/> is the wait.
    /// </summary>
    /// <remarks>
    /// For a holder that will be asked to let go more than once — every source on the way out of a
    /// session is. Kept in a field and disposed again rather than built again, so a second ask does
    /// not start a second thread over handles the first is still inside, and so the re-check at
    /// zero in <see cref="Dispose"/> can notice one that came back a moment late.
    /// </remarks>
    /// <param name="name">What the thread is called in a debugger and a crash dump.</param>
    /// <param name="release">What lets the handles go. Runs once and returns.</param>
    public static DeviceRelease Of(string name, Action release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(release);

        var releasing = new DeviceRelease(name, release);
        releasing.thread.Start();
        return releasing;
    }

    /// <summary>
    /// Lets go of one handle, where a handle that refuses to close is not this line's business.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the two places that let go of several handles in a row and owe every one of them the
    /// attempt: the body of a release, and an attempt at opening a device that failed part-way and
    /// is about to throw. Both used to spell that out as a <c>finally</c> per handle, which is one
    /// place each to write the third one differently — and a release that threw on its first handle
    /// and so never reached its second is what that cost, permanently, since nothing tries again.
    /// A call that cannot throw needs no <c>finally</c> at all: the next line simply runs.
    /// </para>
    /// <para>
    /// Bounded by whoever is running it rather than here. This is called on a thread that can
    /// already be given up on — a <see cref="DeviceRelease"/> body, or the ask inside
    /// <see cref="DeviceOpen"/> — so a driver that wedges on being let go of is a thread that does
    /// not come back, and that is the deadline it is already inside. A second one here would be a
    /// second deadline over one device.
    /// </para>
    /// <para>
    /// Swallowing where the opening failed is not only about the process surviving: what a person
    /// acts on is why the device would not open, and that answer is already on its way up the
    /// stack. A handle refusing to close would replace it with a sentence about cleanup.
    /// </para>
    /// </remarks>
    /// <param name="handle">What to let go of, or nothing, which is letting go of nothing.</param>
    public static void LetGoOf(IDisposable? handle)
    {
        try
        {
            handle?.Dispose();
        }
        catch
        {
            // Swallowed on purpose: see the summary.
        }
    }

    /// <summary>
    /// Waits <see cref="CaptureLoop.StopsWithin"/> for the handles to come back. Coming back is the
    /// whole guarantee: either they are let go of, or <see cref="Abandoned"/> says a thread is
    /// still in there.
    /// </summary>
    public void Dispose()
    {
        // Nothing given up on is waited for again — a session lets go of three of these in
        // sequence, and a deadline each would turn one wedged device into a shutdown nobody sits
        // through. Asked again rather than skipped, though, since a release that came back a
        // moment after its deadline is one whose handles are free after all.
        Abandoned = !thread.Join(Abandoned ? TimeSpan.Zero : CaptureLoop.StopsWithin);
    }

    /// <summary>
    /// Lets go, and keeps a handle that refuses to close from becoming the end of the process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a source has always swallowed on the way out is an <see cref="IOException"/>, an
    /// <see cref="UnauthorizedAccessException"/> or a <see cref="COMException"/>, and this is where
    /// those now arrive instead: what a recording has to say about how it ended is said by the
    /// source finishing, not by a handle that would not close after it.
    /// </para>
    /// <para>
    /// Everything else is caught here too, and that is not the same judgement as
    /// <see cref="CaptureLoop"/>'s. A loop is what a recording is being written by, so an exception
    /// nobody thought about ending the process is better than that loop deciding the meeting goes
    /// on without the audio. A release runs after the meeting is already on disk, and this thread
    /// has no boundary above it: ending the process here would take the other source's release, the
    /// session's own way of finishing, and whatever a person was about to be told with it — over a
    /// handle whose only remaining job was to close. Which is also what these calls used to do,
    /// before they moved off the caller's thread and out from under the catch that was there.
    /// </para>
    /// </remarks>
    private static void Run(Action release)
    {
        try
        {
            release();
        }
        catch
        {
        }
    }
}
