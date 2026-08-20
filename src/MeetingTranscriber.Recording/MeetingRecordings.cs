using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Recording;

/// <summary>A meeting that exists and has somewhere to record into, before anything is recorded.</summary>
/// <param name="MeetingId">The meeting, settled before any audio of it exists.</param>
/// <param name="Spool">The folder its blocks and the card beside them go in.</param>
public sealed record PreparedRecording(Guid MeetingId, DirectoryInfo Spool);

/// <summary>What a recording became once somebody stopped it.</summary>
/// <param name="MeetingId">The meeting.</param>
/// <param name="Audio">The row describing the meeting's audio, hashed as it was written.</param>
/// <param name="Length">How long the meeting turned out to be, pauses included.</param>
/// <param name="Queued">
/// What stopping set going, which is nothing — see <see cref="WhatStoppingStarts"/>. Reported
/// rather than assumed, so a caller reads the answer instead of the absence of one.
/// </param>
public sealed record FinishedRecording(
    Guid MeetingId,
    Artifact Audio,
    Duration Length,
    IReadOnlyList<JobKind> Queued);

/// <summary>
/// Recording a meeting into a corpus: the steps that put rows and files where the rest of the
/// application looks for them, with no device in any of them.
/// </summary>
/// <remarks>
/// <para>
/// This is the join the two halves of the product were built either side of. The audio engine
/// takes a folder and a meeting id and knows nothing about a corpus; the corpus takes rows and
/// artifacts and knows nothing about WASAPI. Neither may reference the other — SQLite would drag
/// WASAPI into rendering a transcript, and the engine would stop being provable on a machine with
/// no corpus — so the composition is a project of its own, and this is it.
/// </para>
/// <para>
/// Every step here is a call somebody could make with no microphone on the machine, which is what
/// makes the corpus side of recording testable at all: a build agent has no device, so the parts
/// that must be proved automatically are the parts that never open one. Opening the devices is
/// <see cref="MeetingRecording"/>, which is as thin as it can be for exactly that reason.
/// </para>
/// </remarks>
public static class MeetingRecordings
{
    /// <summary>
    /// Everything that has to be true before the first sample: the meeting exists, the corpus says
    /// so, and the folder its audio goes into is there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the whole of it. The identity is minted here and never derived from a title, a
    /// file name or anything a provider says, so a meeting is a meeting from the moment somebody
    /// presses record — before it has a name, before there is a byte of it, and whether or not
    /// anything it needs later is reachable. What that buys is a recording that can always be
    /// attached to something: audio found in a folder after a crash belongs to a row that was
    /// written before the audio was.
    /// </para>
    /// <para>
    /// The row is saved before the folder is made, and that way round on purpose. A folder with no
    /// row is what the reconciler calls a spooled recording and recovery already knows how to
    /// offer; a row with no folder is a meeting with nothing in it, which is what pressing record
    /// and having the disk refuse actually is.
    /// </para>
    /// </remarks>
    /// <param name="corpus">The corpus the meeting is being recorded into.</param>
    /// <param name="language">
    /// What the meeting is expected to be spoken in. Asked for rather than assumed: nothing here
    /// can know it, and a default guessed from the application's own language would file an
    /// English meeting as Spanish for having a Spanish menu.
    /// </param>
    /// <param name="now">When record was pressed.</param>
    public static PreparedRecording Open(CorpusDbContext corpus, string language, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = now,
            SourceProfile = CapturedAudio.Profile,
            Language = language,
            LifecycleState = LifecycleState.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        corpus.Meetings.Add(meeting);
        corpus.SaveChanges();

        var spool = CorpusFiles.SpoolFolderFor(corpus.Root, meeting.Id);
        spool.Create();

        return new PreparedRecording(meeting.Id, spool);
    }

    /// <summary>
    /// Writes the row describing the run that has just opened, from what the recording wrote about
    /// itself when its devices opened.
    /// </summary>
    /// <remarks>
    /// Built from the card and from nothing else, which is what keeps the two from disagreeing:
    /// the card is the only account of what was true when the devices opened, and a row filled
    /// from what the caller believed it asked for would say the program was followed on a
    /// recording that opened on the whole machine instead. It is also why the card carries a run id
    /// at all — this row can be written again from a folder found after a crash.
    /// </remarks>
    /// <remarks>
    /// It takes no instant of its own: when the run started is the card's, read off the devices
    /// that opened, and a second answer here would be the moment this row happened to be written.
    /// </remarks>
    public static CaptureRun Began(CorpusDbContext corpus, SpoolCard card)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(card);

        var others = card.On(AudioChannel.Loopback);
        var me = card.On(AudioChannel.Microphone);

        var run = new CaptureRun
        {
            Id = card.CaptureRunId,
            MeetingId = card.MeetingId,
            StartedAt = card.StartedAt,
            // Empty, both of them, and not a branch that could fill them: channel 0 is not a
            // device either way round, and a card saying it was is refused where the card is read.
            // The two columns outlive this by a migration and nothing else.
            OthersDeviceId = null,
            OthersDeviceName = null,
            OthersProcess = card.Mode is CaptureMode.ProcessLoopback ? others.Heard : null,
            OthersCaptureMode = card.Mode,
            MeDeviceId = me.DeviceId,
            MeDeviceName = me.Heard,

            // The contract's numbers and not a device's: this says what the recording comes out
            // as, and every device's own format is already in the header of its own spool.
            SampleRate = CapturedAudio.SampleRate,
            ChannelCount = CapturedAudio.ChannelCount,
            BitsPerSample = CapturedAudio.BitsPerSample,
            Recovered = false,
        };

        corpus.CaptureRuns.Add(run);
        corpus.SaveChanges();

        return run;
    }

    /// <summary>
    /// What stopping does: the spools become the meeting's audio, the corpus is told how long the
    /// meeting was, and nothing is set going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Making the recording is not work that was started — it is the recording being finished. It
    /// costs nothing, reaches nothing off this machine, and produces the same bytes every time
    /// from the same spools, so doing it now rather than queueing it is what lets somebody play
    /// the meeting the moment they stop it. What is deliberately not done here is everything that
    /// would spend money or somebody's quota, and <see cref="WhatStoppingStarts"/> is the one
    /// place that says so.
    /// </para>
    /// <para>
    /// The audio lands in the meeting's folder through the corpus's own write, so the bytes and the
    /// row describing them arrive together and the hash is of what was actually written. The copy
    /// the engine makes beside the spools is left where it is: those blocks are still the only
    /// recording of the meeting that exists independently of this write, and deciding what happens
    /// to a spool is somebody's, never a side effect of stopping.
    /// </para>
    /// </remarks>
    public static FinishedRecording Finish(CorpusDbContext corpus, Guid meetingId, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var meeting = corpus.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new RecordingException(
                $"There is no meeting {meetingId} in this corpus, so there is nothing for a "
                + "recording of it to be finished into.");

        // The folder is worked out from the meeting rather than taken from the caller. A meeting id
        // and a folder passed side by side are two facts that can disagree, and the disagreement is
        // silent and unrecoverable: one meeting's conversation written down, hashed and filed as
        // another's, with a card confidently naming the wrong one.
        var spool = CorpusFiles.SpoolFolderFor(corpus.Root, meeting.Id);
        var card = SpoolManifest.Find(spool);

        if (card is not null && card.MeetingId != meeting.Id)
        {
            throw new RecordingException(
                $"'{spool.FullName}' holds a recording of meeting {card.MeetingId}, not of "
                + $"{meeting.Id}. A recording says which meeting it is, and that is what decides.");
        }

        var made = MeetingAudio.Materialise(spool);
        var path = CorpusFiles.PathFor(meeting.Id, MeetingAudio.FileName);

        var audio = Filed(corpus, meeting.Id, path, made)
            ?? DurableArtifact.Write(
                corpus,
                meeting.Id,
                ArtifactKind.Audio,
                path,
                now,
                into =>
                {
                    using var recording = made.File.OpenRead();
                    recording.CopyTo(into);
                });

        meeting.Duration = made.Length;
        meeting.UpdatedAt = now;

        // The run the card names, and never the most recent one. A meeting recorded, recovered and
        // recorded again has several, and closing off whichever started last would put an end on a
        // run that is still going while leaving the one that just ended open — an ordering guess
        // standing in for an identity the recording already wrote down.
        var run = Ran(corpus, meeting.Id, card);
        if (run is not null)
        {
            run.FinishedAt = now;
        }

        corpus.SaveChanges();

        // After the meeting is whole, because the card is what the corpus now says about it and
        // its length is part of that.
        MeetingManifest.Write(corpus, meeting.Id, now);

        var queued = WhatStoppingStarts.For(meeting);
        if (queued.Count > 0)
        {
            // Nothing here queues, because the answer has always been nothing. Whoever makes it
            // answer otherwise is changing what stopping does, and this is the line that tells them
            // the queueing has to be written — rather than the meeting quietly waiting for work
            // that was decided on and never created.
            throw new RecordingException(
                $"Stopping meeting {meeting.Id} was answered with {string.Join(", ", queued)}, and "
                + "nothing here queues anything. What decides changed without what acts on it.");
        }

        return new FinishedRecording(meeting.Id, audio, made.Length, queued);
    }

    /// <summary>
    /// This meeting's audio, when the corpus already holds exactly the bytes this finish just
    /// made — and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What this is for is a finish that was cut off between its two commits. The audio row lands
    /// first and the meeting's length second, so a machine that dies in between leaves a meeting
    /// with its audio filed and no length — and finishing it again is the only thing that puts
    /// that right. An <see cref="ArtifactKind.Audio"/> is never rewritten, so without this the
    /// second attempt would be refused by the one rule that exists to stop a paid or unrepeatable
    /// file being destroyed.
    /// </para>
    /// <para>
    /// Only when the bytes are the same, and the hash is what says so. The recording is read out of
    /// the blocks by the same code every time, so the same spools give the same file; bytes that
    /// differ mean the folder is not the recording the corpus filed, and that is refused rather
    /// than reconciled — one of the two is a meeting nothing else would ever notice was wrong.
    /// </para>
    /// </remarks>
    private static Artifact? Filed(CorpusDbContext corpus, Guid meetingId, string path, Materialised made)
    {
        var filed = corpus.Artifacts.FirstOrDefault(
            row => row.MeetingId == meetingId
                && row.Kind == ArtifactKind.Audio
                && row.RelativePath == path);

        if (filed is null)
        {
            return null;
        }

        var hash = CorpusFiles.Sha256Of(made.File);
        if (!string.Equals(filed.Sha256, hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecordingException(
                $"Meeting {meetingId} already has its audio filed, and the recording in "
                + $"'{made.File.Directory?.FullName}' does not hash to it. One of the two is not "
                + "this meeting, and a recording is never written over another one.");
        }

        return filed;
    }

    /// <summary>
    /// The run this recording was made by: the one its card names, or the meeting's only one when
    /// the card was never written or was torn in half.
    /// </summary>
    /// <remarks>
    /// Nothing when a meeting whose card is gone has more than one run. That is a recovery somebody
    /// has to look at rather than a guess this may make: the runs are what say which devices caught
    /// which stretch, and putting an end on the wrong one is a lie about a meeting that nothing
    /// afterwards can detect. The meeting still finishes — its audio and its length are read from
    /// the blocks, which never needed the card.
    /// </remarks>
    internal static CaptureRun? Ran(CorpusDbContext corpus, Guid meetingId, SpoolCard? card)
    {
        if (card is not null)
        {
            return corpus.CaptureRuns.FirstOrDefault(row => row.Id == card.CaptureRunId);
        }

        var runs = corpus.CaptureRuns.Where(row => row.MeetingId == meetingId).Take(2).ToList();
        return runs.Count == 1 ? runs[0] : null;
    }
}
