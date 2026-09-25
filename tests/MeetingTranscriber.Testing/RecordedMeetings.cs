using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Testing;

/// <summary>A meeting this application recorded, for a suite that needs one with no response yet.</summary>
/// <remarks>
/// Here and not beside <see cref="MeetingRows"/> because this project's own remarks already give
/// the reason: <c>Processing</c> stays out of this project's references on purpose, and this
/// builder — unlike <see cref="MeetingRows"/>, which fabricates a transcription's rows directly —
/// is what a suite in <c>Processing.Tests</c> or in <c>Recording.Tests</c> reaches for when it
/// needs a meeting <c>TranscribingAMeeting.TranscribeAsync</c> or <c>JobsARestartFound</c> would
/// really find, and both suites already reference this project rather than each other.
/// </remarks>
public static class RecordedMeetings
{
    /// <summary>Deliberately not what any fixture says it transcribed.</summary>
    private static readonly Duration AnHour = Duration.FromMilliseconds(3_600_000);

    /// <summary>
    /// A meeting this corpus recorded, built out of exactly what <c>ReceiveInto</c> reads: a row
    /// with a profile and a length, and one <c>audio</c> artifact under it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By hand rather than through <c>AudioIntake.Bring</c>, and not by choice: this project targets
    /// <c>net10.0</c> and <c>MeetingTranscriber.Recording</c> targets <c>net10.0-windows…</c>, so
    /// there is no reference to make. It is also the only way to get a <c>Multichannel</c> meeting
    /// with no response, which is what a real recording is and what the audio door never produces —
    /// it mixes anything it is not sure about down to one track and files it as <c>Diarize</c>.
    /// </para>
    /// <para>
    /// The bytes under the audio row are not audio. Nothing on this path opens that file: the
    /// response is what is parsed and the length is the row's. What the file has to be is present
    /// and hashed, which is what makes <c>ArtifactReconciler.Check</c> sound afterwards.
    /// </para>
    /// </remarks>
    public static Guid Recorded(
        CorpusDbContext context, SourceProfile profile, UtcTimestamp when, Duration? length = null)
    {
        var meetingId = Guid.NewGuid();

        context.Meetings.Add(new Meeting
        {
            Id = meetingId,
            Title = "la de los jueves",
            StartedAt = when,
            Duration = length ?? AnHour,
            SourceProfile = profile,
            Language = "es",
            LifecycleState = LifecycleState.Active,
            CreatedAt = when,
            UpdatedAt = when,
        });
        context.SaveChanges();

        DurableArtifact.Write(
            context,
            meetingId,
            ArtifactKind.Audio,
            CorpusFiles.PathFor(meetingId, RecordingFiles.Recording),
            when,
            into => into.Write("stands in for the recording"u8));

        return meetingId;
    }

    /// <summary>
    /// The same meeting, with its transcription already queued and started: what
    /// <c>TranscribingAMeeting.TranscribeAsync</c> and <c>JobRunner</c> both read as a job to send.
    /// </summary>
    public static Guid Started(CorpusDbContext context, Guid meeting, UtcTimestamp when)
    {
        var job = new MeetingWork(context, when).Take(meeting);
        job.Start(when);
        context.SaveChanges();

        return job.Id;
    }
}
