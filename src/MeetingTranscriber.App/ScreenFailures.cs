using MeetingTranscriber.Audio;
using MeetingTranscriber.Recording;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.App;

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
/// One list because two screens read the same corpus the same way, and a list that grew on one of
/// them would leave the other crashing over the failure the first learnt to say.
/// </para>
/// </remarks>
internal static class ScreenFailures
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
