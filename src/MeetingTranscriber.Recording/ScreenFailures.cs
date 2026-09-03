using MeetingTranscriber.Audio;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What a screen over the corpus says rather than throws over.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="InvalidOperationException"/>, which is caught on its own where it
/// means something: everywhere else it is a defect, and a screen that swallowed it would leave one
/// looking like a corpus somebody could not read.
/// </para>
/// <para>
/// The list is closed on purpose, and what it guards is worth being exact about: the handlers that
/// ask it are <c>async void</c>, so anything not named here reaches the dispatcher and takes the
/// application down in the middle of a meeting. What is named is what the layers underneath
/// actually throw — the audio engine's refusal, the recording's own, the filesystem's two, and
/// SQLite's, which arrives from a corpus that is locked, unwritable or not a database and which the
/// command line already answers with a refusal rather than with a stack trace.
/// </para>
/// <para>
/// One list, and every screen and every reader of the corpus asks it. A second copy is how this
/// application ends up unable to open again over a failure one of them had learnt to say and
/// another had not. It lives here rather than beside the screens because it is not only screens
/// that ask any more: <see cref="MeetingsWatch"/> reads the corpus on the thread a window is being
/// built on, and this is the one project that can see every exception the list names.
/// </para>
/// </remarks>
public static class ScreenFailures
{
    /// <summary>True when this is a thing to say and not a defect to stop over.</summary>
    public static bool Reportable(Exception thrown) => thrown
        is IOException
        or UnauthorizedAccessException
        or SqliteException
        or DbUpdateException
        or AudioCaptureException
        or RecordingException;
}
