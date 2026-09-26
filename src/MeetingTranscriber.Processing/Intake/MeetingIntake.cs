using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Processing.Intake;

/// <summary>A response that cannot become a meeting, saying which one and what stopped it.</summary>
public sealed class IntakeException(string message) : Exception(message);

/// <summary>
/// What a caller knows about a meeting that its response does not say.
/// </summary>
/// <remarks>
/// The profile is asked for rather than read off the response's channel count, and that is the
/// whole point of it being here: a recording made on two channels that came back as one track was
/// paid for as something other than what was sent, and inferring the profile from what arrived
/// would file that away as a single-track meeting instead of refusing it.
/// </remarks>
/// <param name="StartedAt">When the meeting was recorded, which no response knows.</param>
/// <param name="Language">
/// What was spoken, as it was asked for. The response carries no language either — it carries the
/// consequences of the one the request named.
/// </param>
public sealed record MeetingDetails(
    UtcTimestamp StartedAt,
    SourceProfile Profile,
    string Language,
    string? Title = null,
    string? Context = null);

/// <summary>What one intake produced, and whether the corpus already had it.</summary>
/// <param name="PutBack">
/// The paths that had no file and have one again, because the response handed over turned out to
/// be what a row of this corpus was missing. A caller that answers "already here" and has quietly
/// written a paid file back is telling somebody nothing changed while something did, and that
/// their corpus had a hole in it is the one thing they would have wanted to know.
/// </param>
public sealed record ReceivedMeeting(
    Guid MeetingId,
    bool WasAlreadyThere,
    int Turns,
    Artifact Response,
    Artifact Manifest,
    Artifact Transcript,
    Artifact Utterances,
    IReadOnlyList<string> PutBack);

/// <summary>
/// What <see cref="MeetingIntake.ReceiveWhatWasRefused"/> did: the filing itself, which run it
/// finished, and whether that run's job was this call's to settle.
/// </summary>
/// <param name="JobSettled">
/// True when the run's job was <see cref="JobState.AwaitingUser"/> and this call moved it to
/// <see cref="JobState.Succeeded"/>. False when somebody had already requeued it, in which case it
/// is left exactly where it was: a job somebody put back in the queue is the runner's to send, not
/// this door's to declare finished under it.
/// </param>
/// <param name="KeptPath">
/// The stored path of the file this run's own refusal kept — this method's caller already needs it
/// named, so it is handed over rather than left for a report to reconstruct from a run id and a
/// naming convention that belongs to <c>TranscribingAMeeting</c>.
/// </param>
public sealed record RefusedResponseFiled(ReceivedMeeting Received, Guid RunId, bool JobSettled, string KeptPath);

/// <summary>
/// A paid Deepgram response on disk becoming a meeting of this corpus: the response filed as the
/// source it is, and everything derived from it produced here.
/// </summary>
/// <remarks>
/// <para>
/// The response is what identifies the meeting, not the file it arrived in and not the name it was
/// given. Handing the same bytes over twice is the same meeting a second time, so it re-renders
/// rather than making a second copy of something that was paid for once — which is what a person
/// retrying a command that half worked is doing, and the shape the whole thing has to survive.
/// </para>
/// <para>
/// Three doors and one type. <see cref="Receive"/> is the way in for a response that has no
/// meeting yet — somebody has the file and the corpus has nothing about it — and it takes the
/// identity off the bytes. <see cref="ReceiveInto"/> is the way in for a response belonging to a
/// meeting this application recorded, and it takes the identity off the meeting it was named.
/// <see cref="ReceiveAgainInto"/> files a later version onto a meeting that already has one, and it
/// is the only one of the three that can make a meeting hold two paid responses. What none of them
/// is is a call to a provider: nothing here spends anything, and a response arrives already paid
/// for. <see cref="ReceiveWhatWasRefused"/> is not a fourth door: it is
/// <see cref="ReceiveAgainInto"/> called for a response one of the meeting's own runs already paid
/// for and the corpus kept rather than filed.
/// </para>
/// <para>
/// Everything they share is written once and called by all three, and the list is worth reading
/// before any of them is changed: <c>Opened</c> is the handle, <c>AlreadyFiled</c> is what counts
/// as these bytes already being here, <c>AlreadyHere</c> is what a second filing of the same bytes
/// does, and <c>Derived</c> is the render and the answer. What is left written twice is what
/// genuinely differs — where the identity comes from, where the profile comes from, what each door
/// refuses before it files, and the verb the audit keeps. Two doors that had drifted on where the
/// recovery card went is what card #94 existed to fix; these are held together by call rather than
/// by anybody remembering.
/// </para>
/// </remarks>
public static class MeetingIntake
{
    public static ReceivedMeeting Receive(
        CorpusDbContext context,
        FileInfo response,
        MeetingDetails details,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(details);

        using var bytes = Opened(response);

        // Read before anything is written, and the order is the point. A response whose channel
        // count disagrees with the profile it is being filed under is refused here with the corpus
        // untouched; refused one step later, it would leave behind a meeting nothing can render and
        // a source file that is never allowed to be written a second time.
        var transcript = DeepgramTranscriptParser.Parse(bytes, details.Profile);

        var already = AlreadyFiled(context, bytes);
        var meetingId = already?.MeetingId ?? Guid.NewGuid();

        if (already is not null)
        {
            return Derived(context, meetingId, AlreadyHere(context, already, bytes, meetingId, now), now);
        }

        var (stored, manifest) = MeetingArchive.New(
            context,
            new NewMeeting(
                meetingId,
                details.StartedAt,

                // What the provider says it transcribed. Asking the caller for a length it would be
                // reading off the same file is asking it to be wrong.
                transcript.Audio,
                details.Profile,
                details.Language,
                details.Title,
                details.Context),
            new ArrivedOn(
                Kind: ArtifactKind.DeepgramResponse,
                FileName: ResponseVersions.First,
                Contents: bytes.CopyTo,
                Verb: "imported",
                Detail: $"the response at '{response.FullName}'"),
            now);

        return Derived(context, meetingId, new Filed(stored, manifest, [], WasAlreadyThere: false), now);
    }

    /// <summary>
    /// A paid response filed onto the meeting this application already recorded, rather than onto
    /// one of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other door, and the difference between the two is where the identity comes from.
    /// <see cref="Receive"/> reads it off the response, because a response somebody has and a corpus
    /// that knows nothing about it is all there is to go on. Here the meeting is named, so the
    /// response is filed onto it and nothing is minted — which is the whole of this method: a
    /// meeting recorded on this machine already has a row, a folder, an audio file, a length and a
    /// capture run, and a second row for the same conversation is a corpus saying a meeting happened
    /// twice.
    /// </para>
    /// <para>
    /// <b>Nothing is asked of the caller, and that is the contract.</b> When the meeting was, what
    /// it was recorded as and what was expected to be spoken in it are all on the row already; the
    /// profile especially, because it is what the parser is held to and what decides whether the
    /// channels are two sources or one track. A caller allowed to say otherwise could file a
    /// two-channel recording as diarized, and channel 0 is the loopback — so every turn of it would
    /// land with the speakers told apart by the provider and the user's own microphone lost.
    /// </para>
    /// <para>
    /// <b>The meeting's length is never touched.</b> It was counted off the audio the corpus holds,
    /// and what the provider says it transcribed is a second number about the same file. One meeting
    /// is never given two lengths.
    /// </para>
    /// <para>
    /// The same response handed over twice is the same filing a second time: it files nothing,
    /// restores what the disk has lost, writes the card again and re-renders — exactly as
    /// <see cref="Receive"/> does, and for the same reason, which is somebody re-running a command
    /// that half worked.
    /// </para>
    /// <para>
    /// <b>A refusal from here hands back a context nobody may save again.</b> Filing sets this
    /// meeting's <c>UpdatedAt</c> and adds an audit row inside the archive's transaction, and
    /// rolling that transaction back does not undo EF's change tracker — so a second
    /// <c>SaveChanges</c> on the same context would write a timestamp over a filing that never
    /// happened. Every caller today either disposes the context or lets the throw straight out, and
    /// <c>MeetingRecordings.Finish</c> says the same about its own.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting this response is of. It has to be in this corpus.</param>
    /// <exception cref="IntakeException">
    /// There is no response at that path, this corpus has no such meeting, the meeting has no audio
    /// for a response to be of, those bytes are already another meeting's response, or this meeting
    /// already has a response and these are not its bytes. Every one of those is refused before the
    /// first row is written.
    /// </exception>
    /// <exception cref="AudioContractException">
    /// The response's channel count disagrees with what this meeting was recorded as. Nothing is
    /// written: the refusal happens before the first row.
    /// </exception>
    public static ReceivedMeeting ReceiveInto(
        CorpusDbContext context,
        Guid meetingId,
        FileInfo response,
        UtcTimestamp now) =>
        ReceiveOnto(context, meetingId, response, now, (meeting, bytes) =>
        {
            // The other half of the guard `ReceiveOnto` already asked. `Receive` cannot reach this
            // state: it picks the meeting *by* the hash, so bytes the corpus has never seen are
            // always a meeting of their own. Here the meeting comes off the name and the response
            // off the bytes, so "these bytes are new" and "this meeting has no response" are two
            // questions, and only the first has been asked.
            //
            // `StagedArtifact` asks the second one too, about the file first and about the row once
            // the file is gone — a never-replaceable kind whose existing row for this meeting and
            // path records different bytes is refused there as of O-20260910-17. The block below is
            // still not redundant and still fires first: it is keyed on the kind rather than the
            // path, so it reaches a state the path check does not, and it says which meeting. A paid
            // response is never written over, and a row is as much the corpus's record of it as the
            // file is.
            if (context.Artifacts.FirstOrDefault(artifact =>
                    artifact.MeetingId == meetingId
                    && artifact.Kind == ArtifactKind.DeepgramResponse) is { } filed)
            {
                throw new IntakeException(
                    $"Meeting {meetingId} already has a response and these are not its bytes. A "
                    + "response is paid for once and never written over — "
                    + $"'{filed.RelativePath}' is recorded as {filed.Sha256}. Another response for "
                    + "the same meeting is a new version beside that one, which is what "
                    + "transcribing it again files; this door only ever files a meeting's first.");
            }

            // The meeting has changed — it has a source it did not have — and the instant that
            // happened is this one. Tracked here and saved by the archive's own SaveChanges, in the
            // transaction that files the response.
            meeting.UpdatedAt = now;

            var (stored, manifest) = MeetingArchive.Onto(
                context,
                meetingId,
                new ArrivedOn(
                    Kind: ArtifactKind.DeepgramResponse,
                    FileName: ResponseVersions.First,
                    Contents: bytes.CopyTo,

                    // A third verb, because it is a third thing to find later: not a meeting made
                    // out of a response and not one made out of a WAV, but a recording of this
                    // machine's meeting a paid response arrived for.
                    Verb: "response filed",
                    Detail: $"the response at '{response.FullName}'"),
                now);

            return Derived(context, meetingId, new Filed(stored, manifest, [], WasAlreadyThere: false), now);
        });

    /// <summary>
    /// A later paid response filed beside the one(s) a meeting already has, rather than refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of what re-transcribing a meeting files, and the only one of the three doors
    /// that can leave a meeting holding two paid responses at once. Everything up to the point where
    /// <see cref="ReceiveInto"/> refuses is shared with it: the meeting lookup, the audio guard, the
    /// parse under the meeting's own profile, <c>AlreadyFiled</c> and <c>AlreadyHere</c>. Handing the
    /// same bytes over again is still the same filing a second time here, and never a new version —
    /// the version fork is asked only once that question has already been answered no.
    /// </para>
    /// <para>
    /// The version is <see cref="ResponseVersions"/>'s alone. Every existing response row is read,
    /// and a row whose name is not in the series stops this door cold: where the next version would
    /// go cannot be settled around a name nobody wrote by this rule. Otherwise the next version is
    /// one past the highest already filed, or the first when there are none — which is the shape a
    /// meeting <see cref="ReceiveInto"/> never touched reaches through this door alone.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting this response is of. It has to be in this corpus.</param>
    /// <exception cref="IntakeException">
    /// There is no response at that path, this corpus has no such meeting, the meeting has no audio
    /// for a response to be of, those bytes are already another meeting's response, or a response
    /// row of this meeting whose name is not in the series. Every one of those is refused before the
    /// first row is written.
    /// </exception>
    /// <exception cref="AudioContractException">
    /// The response's channel count disagrees with what this meeting was recorded as. Nothing is
    /// written: the refusal happens before the first row.
    /// </exception>
    public static ReceivedMeeting ReceiveAgainInto(
        CorpusDbContext context,
        Guid meetingId,
        FileInfo response,
        UtcTimestamp now) =>
        ReceiveOnto(context, meetingId, response, now, (meeting, bytes) =>
        {
            var placed = new List<int>();
            foreach (var row in context.Artifacts.Where(artifact =>
                         artifact.MeetingId == meetingId
                         && artifact.Kind == ArtifactKind.DeepgramResponse))
            {
                if (ResponseVersions.VersionOf(row) is not { } version)
                {
                    throw new IntakeException(
                        $"Meeting {meetingId} names '{row.RelativePath}' as a response and that is "
                        + "not a name in the series, so where another would go cannot be settled. "
                        + $"A response is '{ResponseVersions.First}' or 'deepgram.v<n>.json' from 2 "
                        + "up. Nothing was filed.");
                }

                placed.Add(version);
            }

            var next = placed.Count == 0 ? 1 : placed.Max() + 1;

            meeting.UpdatedAt = now;

            var (stored, manifest) = MeetingArchive.Onto(
                context,
                meetingId,
                new ArrivedOn(
                    Kind: ArtifactKind.DeepgramResponse,
                    FileName: ResponseVersions.Named(next),
                    Contents: bytes.CopyTo,
                    Verb: next == 1 ? "response filed" : "response filed again",
                    Detail: $"the response at '{response.FullName}', version {next}"),
                now);

            return Derived(context, meetingId, new Filed(stored, manifest, [], WasAlreadyThere: false), now);
        });

    /// <summary>
    /// A paid response the corpus refused to file, filed now from the copy it kept — with no charge
    /// and no prompt to a provider, because these bytes have already been paid for once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order that has to hold: the meeting has to be here; hash <paramref name="response"/>;
    /// find, oldest first, the meeting's own unfinished <c>transcription_runs</c> row whose job is
    /// still <see cref="JobState.AwaitingUser"/> and whose kept refused file is these same bytes;
    /// file through <see cref="ReceiveAgainInto"/>; then finish that run and move its job to
    /// <see cref="JobState.Succeeded"/> — the one move <c>JobStates</c> keeps for a paid response
    /// already on disk. A run with no match is refused before anything is touched: this is not a
    /// call that files just any response onto just any unfinished run, only the one this meeting is
    /// still waiting on.
    /// </para>
    /// <para>
    /// <b>The job has to be <see cref="JobState.AwaitingUser"/>, and not merely the run
    /// unfinished.</b> Trying again is answered two ways: this door, and
    /// <c>MeetingWork.TryAgain</c> requeuing the same job for a fresh attempt. A requeue that then
    /// succeeds leaves the old run's row exactly as it was — <c>FinishedAt</c> null, its kept file
    /// still on disk — even though the meeting now has a real, accepted response from the newer
    /// attempt. Matching on the run alone would let that stale, already-superseded copy be filed as
    /// a later version than the one that actually came back; matching on the job's own state as
    /// well turns it into the same refusal an unmatched run gets, because by then this is no longer
    /// what the meeting is waiting on.
    /// </para>
    /// <para>
    /// <b>Trap.</b> A second run of this same command, after a first one that finished the run,
    /// finds no unfinished run left and is refused — which is the honest answer, not a fault: the
    /// run this bytes belonged to is not waiting on anybody any more.
    /// </para>
    /// <para>
    /// <b>Trap.</b> A first run that filed the response but could not save the run's own row is run
    /// again: <see cref="ReceiveAgainInto"/> takes its <c>AlreadyHere</c> branch — these bytes are
    /// already the meeting's response — and this method still goes on to finish the run and settle
    /// the job. That is why the run is found by reading the kept file, and not by asking whether an
    /// artifact already exists: the artifact from the first attempt is exactly what would make the
    /// second look unnecessary if it were asked instead.
    /// </para>
    /// </remarks>
    /// <param name="meetingId">The meeting the refused response belongs to.</param>
    /// <exception cref="IntakeException">
    /// There is no such meeting, no unfinished run of it still waiting on a person kept these bytes
    /// as its refused response, or <see cref="ReceiveAgainInto"/> itself refuses. Either way nothing
    /// is filed.
    /// </exception>
    public static RefusedResponseFiled ReceiveWhatWasRefused(
        CorpusDbContext context,
        Guid meetingId,
        FileInfo response,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        _ = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new IntakeException(
                $"There is no meeting {meetingId} in this corpus. A response is filed onto a "
                + "meeting that is already here; a response for a meeting nothing knows about "
                + "becomes a meeting of its own instead.");

        string hash;
        using (var opened = Opened(response))
        {
            hash = CorpusFiles.Sha256Of(opened);
        }

        var run = context.TranscriptionRuns
            .Where(row => row.MeetingId == meetingId && row.FinishedAt == null)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .AsEnumerable()
            .FirstOrDefault(candidate =>
                StillAwaitingAPerson(context, candidate.JobId)
                && KeptBytesMatch(context, meetingId, candidate.Id, hash));

        if (run is null)
        {
            throw new IntakeException(
                $"Meeting {meetingId} has no unfinished transcription whose kept response is these "
                + "bytes, so this is not a response the corpus refused. Only a response one of this "
                + "meeting's own runs paid for and the corpus would not file is filed this way. "
                + "Nothing was filed.");
        }

        var received = ReceiveAgainInto(context, meetingId, response, now);

        run.FinishedAt = now;
        run.ResponseArtifactId = received.Response.Id;

        var job = context.ProcessingJobs.First(row => row.Id == run.JobId);
        var settled = false;
        if (job.State == JobState.AwaitingUser)
        {
            job.Succeed(now);
            settled = true;
        }

        context.SaveChanges();

        var keptPath = CorpusFiles.PathFor(meetingId, TranscribingAMeeting.RefusedResponseFileName(run.Id));
        return new RefusedResponseFiled(received, run.Id, settled, keptPath);
    }

    /// <summary>Whether this job is still the one thing a stuck meeting is waiting on a person for.</summary>
    private static bool StillAwaitingAPerson(CorpusDbContext context, Guid jobId) =>
        context.ProcessingJobs.FirstOrDefault(row => row.Id == jobId)?.State == JobState.AwaitingUser;

    /// <summary>
    /// Whether the file a run's own refusal kept is still there and is these same bytes. A file
    /// this cannot read — gone, or held, between the exists check and the read — is not a match,
    /// the same as one that was never there: the corpus's own report line on a successful filing
    /// says the kept copy "can be deleted", so a candidate racing that deletion is expected, not a
    /// fault this should crash on.
    /// </summary>
    private static bool KeptBytesMatch(CorpusDbContext context, Guid meetingId, Guid runId, string hash)
    {
        var kept = CorpusFiles.Locate(
            context.Root, CorpusFiles.PathFor(meetingId, TranscribingAMeeting.RefusedResponseFileName(runId)));
        kept.Refresh();

        if (!kept.Exists)
        {
            return false;
        }

        try
        {
            return CorpusFiles.Sha256Of(kept) == hash;
        }
        catch (Exception vanished) when (vanished is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Everything <see cref="ReceiveInto"/> and <see cref="ReceiveAgainInto"/> share: the meeting
    /// lookup, the audio guard, the parse under the meeting's own profile, <see cref="AlreadyFiled"/>
    /// and <see cref="AlreadyHere"/>. What is left to <paramref name="whenNoRowMatches"/> is exactly
    /// what the two doors decide differently — whether a response already on the meeting is a
    /// refusal or the reason a next version exists, and which name and verb the filing carries.
    /// </summary>
    /// <remarks>
    /// This is the private core Decides 4 asks for, so the two doors cannot drift the way card #94
    /// found them drifted: a bug fixed here is fixed for both, and a message worded here is worded
    /// once.
    /// </remarks>
    private static ReceivedMeeting ReceiveOnto(
        CorpusDbContext context,
        Guid meetingId,
        FileInfo response,
        UtcTimestamp now,
        Func<Meeting, Stream, ReceivedMeeting> whenNoRowMatches)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        using var bytes = Opened(response);

        var meeting = context.Meetings.FirstOrDefault(row => row.Id == meetingId)
            ?? throw new IntakeException(
                $"There is no meeting {meetingId} in this corpus. A response is filed onto a "
                + "meeting that is already here; a response for a meeting nothing knows about "
                + "becomes a meeting of its own instead.");

        // Not a stage lookup and not `MeetingStages.Of`: what this needs to know is whether there
        // is a recording for the response to be of, which is one row. A meeting still being
        // recorded, and one whose stop never finished, are both here — and filing a response onto
        // either would put a transcript over a recording nobody has finished reading off the disk.
        if (!context.Artifacts.Any(row =>
                row.MeetingId == meetingId && row.Kind == ArtifactKind.Audio))
        {
            throw new IntakeException(
                $"Meeting {meetingId} has no audio in this corpus, so there is nothing a response "
                + "could be of. A recording that is still running has none until it is stopped, "
                + "and one whose stop never finished has none until recovery completes it — "
                + "'recovery --meeting <id> --keep'.");
        }

        // Parsed under the meeting's own profile and never under one a caller named. A response
        // whose channel count disagrees with what this meeting was recorded as is refused here,
        // with the corpus untouched and no paid file moved. What it parsed to is discarded: the
        // parse is the refusal, and the rows come from `MeetingRenderer.Render` reading the file
        // the corpus stored — which is what `Receive` does too.
        _ = DeepgramTranscriptParser.Parse(bytes, meeting.SourceProfile);

        var already = AlreadyFiled(context, bytes);

        if (already is not null && already.MeetingId != meetingId)
        {
            throw new IntakeException(
                $"These bytes are already meeting {already.MeetingId}'s response, and a response "
                + $"is one meeting's. Filing them onto {meetingId} as well would put one "
                + "conversation under two meetings with nothing afterwards able to tell which is "
                + "which.");
        }

        if (already is not null)
        {
            return Derived(context, meetingId, AlreadyHere(context, already, bytes, meetingId, now), now);
        }

        return whenNoRowMatches(meeting, bytes);
    }

    /// <summary>What one filing produced, before the derivatives are made from it.</summary>
    private sealed record Filed(
        Artifact Stored,
        Artifact Card,
        IReadOnlyList<string> PutBack,
        bool WasAlreadyThere);

    /// <summary>
    /// The handle a filing reads the response through: opened once, with no writer allowed while it
    /// is held.
    /// </summary>
    /// <remarks>
    /// Reading the file three times would let something replace it in between, and the meeting
    /// would then take its length from one version, its identity from a second and its stored bytes
    /// from a third — with nothing afterwards able to notice.
    /// </remarks>
    private static FileStream Opened(FileInfo response)
    {
        response.Refresh();
        if (!response.Exists)
        {
            throw new IntakeException($"There is no response at '{response.FullName}'.");
        }

        return response.Open(new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
        });
    }

    /// <summary>
    /// The response row these bytes already are, anywhere in this corpus, or nothing.
    /// </summary>
    /// <remarks>
    /// It rewinds on both sides, so the caller's handle is where it left it and the next read of it
    /// starts at the beginning. Both doors ask this question and each does something different with
    /// the answer — one takes the meeting off it, the other refuses if it names another meeting —
    /// but what counts as "already here" is one rule, and a corpus where the two doors disagreed
    /// about that would file the same paid bytes twice.
    /// </remarks>
    private static Artifact? AlreadyFiled(CorpusDbContext context, Stream bytes)
    {
        bytes.Position = 0;
        var sha256 = CorpusFiles.Sha256Of(bytes);
        bytes.Position = 0;

        return context.Artifacts.FirstOrDefault(artifact =>
            artifact.Kind == ArtifactKind.DeepgramResponse && artifact.Sha256 == sha256);
    }

    /// <summary>
    /// The filing that found these bytes already here: nothing is filed, what the disk has lost
    /// goes back, and the card is written again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A meeting the corpus knows and whose paid file is gone. Somebody handing the original over
    /// again is the ordinary way that gets noticed, and it used to go straight to a render that
    /// failed on the file the row names — with the bytes that would have fixed it open in this
    /// method. They go back before anything is derived from them, through the same door the restore
    /// command uses and on the same terms: the corpus finds the rows these bytes belong under, and
    /// this hands over no row of its own even though it is holding one.
    /// </para>
    /// <para>
    /// The card, on a filing that found the meeting already here — which is the only branch that
    /// has to write one, because the other gets it from the archive after the commit that filed it.
    /// It is the cheapest artifact to produce and the only one that says which meeting this folder
    /// is, so a filing that finds the card missing, or saying what the corpus no longer says, is
    /// what puts it right.
    /// </para>
    /// </remarks>
    private static Filed AlreadyHere(
        CorpusDbContext context, Artifact already, Stream bytes, Guid meetingId, UtcTimestamp now)
    {
        var putBack = ArtifactRestore.Restore(context, bytes, now).PutBack;
        return new Filed(already, MeetingManifest.Write(context, meetingId, now), putBack, true);
    }

    /// <summary>
    /// Everything a filing produces after the source is settled, which is the same either way the
    /// meeting was identified.
    /// </summary>
    /// <remarks>
    /// Outside the filing above, and deliberately. The file and the database cannot be written
    /// together, so a response that has landed on disk is recorded the moment it can be; a render
    /// that then fails leaves a meeting whose paid source is in the corpus and whose derivatives
    /// are not, which running the command again puts right — where one transaction over both would
    /// have rolled the response's row back and left the file behind as something nothing may adopt.
    /// </remarks>
    private static ReceivedMeeting Derived(
        CorpusDbContext context, Guid meetingId, Filed filed, UtcTimestamp now)
    {
        var rendered = MeetingRenderer.Render(context, meetingId, now);

        return new ReceivedMeeting(
            meetingId,
            filed.WasAlreadyThere,
            rendered.Turns,
            filed.Stored,
            filed.Card,
            rendered.Transcript,
            rendered.Utterances,
            filed.PutBack);
    }
}
