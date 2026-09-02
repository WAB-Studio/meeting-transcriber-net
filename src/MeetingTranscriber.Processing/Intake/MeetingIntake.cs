using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
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
/// It is the way in for a response that has no meeting yet — somebody has the file and the corpus
/// has nothing about it — and it is deliberately not written wider than that. A response a
/// transcription job brings back belongs to a meeting that already exists and to the run that
/// paid for it, so it arrives through its own door when there is a job to bring it; what the two
/// share is <see cref="MeetingRenderer"/>, which is where the derivatives come from either way.
/// </para>
/// </remarks>
public static class MeetingIntake
{
    /// <summary>The name a response is stored under, which docs/corpus.md fixes.</summary>
    public const string ResponseFileName = "deepgram.json";

    public static ReceivedMeeting Receive(
        CorpusDbContext context,
        FileInfo response,
        MeetingDetails details,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(details);

        response.Refresh();
        if (!response.Exists)
        {
            throw new IntakeException($"There is no response at '{response.FullName}'.");
        }

        // One handle for all three reads, and no writer allowed while it is held. Reading the file
        // three times would let something replace it in between, and the meeting would then take
        // its length from one version, its identity from a second and its stored bytes from a
        // third — with nothing afterwards able to notice.
        using var bytes = response.Open(new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
        });

        // Read before anything is written, and the order is the point. A response whose channel
        // count disagrees with the profile it is being filed under is refused here with the corpus
        // untouched; refused one step later, it would leave behind a meeting nothing can render and
        // a source file that is never allowed to be written a second time.
        var transcript = DeepgramTranscriptParser.Parse(bytes, details.Profile);
        bytes.Position = 0;
        var sha256 = CorpusFiles.Sha256Of(bytes);
        bytes.Position = 0;

        var already = context.Artifacts.FirstOrDefault(artifact =>
            artifact.Kind == ArtifactKind.DeepgramResponse && artifact.Sha256 == sha256);

        var meetingId = already?.MeetingId ?? Guid.NewGuid();
        var stored = already ?? Record(context, meetingId, response, details, transcript, bytes, now);

        // A meeting the corpus knows and whose paid file is gone. Somebody handing the original
        // over again is the ordinary way that gets noticed, and it used to go straight to a render
        // that failed on the file the row names — with the bytes that would have fixed it open in
        // this method. They go back before anything is derived from them, through the same door the
        // restore command uses and on the same terms: the corpus finds the rows these bytes belong
        // under, and this hands over no row of its own even though it is holding one.
        IReadOnlyList<string> putBack = [];
        if (already is not null)
        {
            putBack = ArtifactRestore.Restore(context, bytes, now).PutBack;
        }

        // Before the render and on every filing, including one that found the meeting already
        // here. It is the cheapest artifact to produce and the only one that says which meeting
        // this folder is, so it goes down as early as there is a row to write it from — and a
        // filing that finds the card missing, or saying what the corpus no longer says, is what
        // puts it right.
        var manifest = MeetingManifest.Write(context, meetingId, now);

        // Before the render, so the transcript this filing writes already reads the name rather
        // than the label. A meeting whose first rendering says `ch1:speaker_0` about the person
        // who recorded it, and says otherwise only once something happens to render it again, is
        // a corpus answering one question two ways.
        //
        // On every filing rather than only the first, which is what re-filing a response means
        // everywhere else here: the same bytes handed over again re-derive everything they can. So
        // a response filed before anybody said who is using this install picks the name up when it
        // is filed again, and one filed after does not need to be. What it never does is overrule
        // a person — `Assign` refuses that in its own right.
        new HumanLayer(context, now).SettleTheMicrophone(meetingId, details.Profile, transcript.Segments);

        // Outside the filing above, and deliberately. The file and the database cannot be written
        // together, so a response that has landed on disk is recorded the moment it can be; a
        // render that then fails leaves a meeting whose paid source is in the corpus and whose
        // derivatives are not, which this command puts right by being run again — where one
        // transaction over both would have rolled the response's row back and left the file
        // behind as something nothing may adopt.
        var rendered = MeetingRenderer.Render(context, meetingId, now);

        return new ReceivedMeeting(
            meetingId,
            already is not null,
            rendered.Turns,
            stored,
            manifest,
            rendered.Transcript,
            rendered.Utterances,
            putBack);
    }

    /// <summary>
    /// The meeting and the response it is built on, in that order — the row has to exist before an
    /// artifact can point at it — and as one thing: a corpus with a response nothing owns is the
    /// state this transaction exists to prevent.
    /// </summary>
    private static Artifact Record(
        CorpusDbContext context,
        Guid meetingId,
        FileInfo response,
        MeetingDetails details,
        DeepgramTranscript transcript,
        Stream bytes,
        UtcTimestamp now)
    {
        using var filing = context.Database.CurrentTransaction is null
            ? context.Database.BeginTransaction()
            : null;

        context.Meetings.Add(new Meeting
        {
            Id = meetingId,
            Title = details.Title,
            Context = details.Context,
            StartedAt = details.StartedAt,

            // What the provider says it transcribed. Asking the caller for a length it would be
            // reading off the same file is asking it to be wrong.
            Duration = transcript.Audio,
            SourceProfile = details.Profile,
            Language = details.Language,
            CreatedAt = now,
            UpdatedAt = now,
        });

        // Where this meeting came from, in the table provenance belongs in. A corpus holding a
        // meeting nobody recorded on this machine is worth being able to explain later.
        context.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = now,
            Actor = AuditActor.App,
            Action = "imported",
            MeetingId = meetingId,
            Detail = $"the response at '{response.FullName}'",
        });

        context.SaveChanges();

        var artifact = DurableArtifact.Write(
            context,
            meetingId,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meetingId, ResponseFileName),
            now,
            bytes.CopyTo);

        filing?.Commit();
        return artifact;
    }
}
