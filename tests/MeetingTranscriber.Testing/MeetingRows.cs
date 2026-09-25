using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Testing;

/// <summary>
/// The rows a meeting is made of, for a suite that needs one to read rather than one to build.
/// </summary>
/// <remarks>
/// <para>
/// A meeting an extraction has been accepted on is eight tables and about sixty lines of
/// construction: the meeting, its turns, the paid response filed against it, a job, a run, a
/// summary, and a decision, an action and an open question each carrying a citation that has to
/// land on a real turn or the corpus refuses it. Every suite that reads such a meeting was writing
/// those sixty lines again, and the citation rule is exactly the sort of thing one of the copies
/// eventually gets subtly wrong.
/// </para>
/// <para>
/// It builds and never asserts, like everything else here. What a suite is <em>about</em> stays in
/// the suite: which run answers, what a window takes in, what a tool says — none of that is decided
/// by this file, which only makes the rows those questions are asked of.
/// </para>
/// </remarks>
public static class MeetingRows
{
    /// <summary>A meeting with turns, and the response its turns were read out of.</summary>
    /// <param name="context">The corpus.</param>
    /// <param name="startedAt">When it was recorded, which is what every listing orders by.</param>
    /// <param name="said">
    /// What was said, one turn per entry, each a second after the last. The texts are the caller's
    /// because a suite about search needs words it can search for and one about ordering does not.
    /// </param>
    /// <param name="title">What somebody called it, or nothing.</param>
    /// <param name="responseSha256">
    /// The hash of the paid response, filed as an artifact and recorded on a finished transcription
    /// run — both, because the corpus's link from turns to what produced them runs through the run
    /// and not through the artifact. Nothing when the meeting's turns came from no response
    /// anybody paid for, which is a meeting imported from a file somebody already had.
    /// </param>
    /// <param name="root">
    /// The corpus root, where the meeting's own <c>audio.wav</c> and its <c>Artifact</c> row are wanted.
    /// Nothing where the suite is about rows alone, which is every caller but the one that reads a
    /// meeting's recording off the disk.
    /// </param>
    public static Guid Recorded(
        CorpusDbContext context,
        UtcTimestamp startedAt,
        IReadOnlyList<string> said,
        string? title = null,
        string? responseSha256 = null,
        DirectoryInfo? root = null)
    {
        ArgumentNullException.ThrowIfNull(said);

        var meeting = Guid.NewGuid();

        Add(context, new Meeting
        {
            Id = meeting,
            Title = title,
            StartedAt = startedAt,
            Duration = Duration.FromMilliseconds(600_000),
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
        });

        for (var ordinal = 0; ordinal < said.Count; ordinal++)
        {
            Add(context, new Utterance
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Ordinal = ordinal,
                Start = At(ordinal),
                End = At(ordinal) + Duration.FromMilliseconds(500),
                Channel = AudioChannel.Microphone,
                SpeakerLabel = SpeakerLabel,
                Text = said[ordinal],
            });
        }

        if (root is not null)
        {
            var audio = CorpusFiles.Locate(root, CorpusFiles.PathFor(meeting, "audio.wav"));
            audio.Directory!.Create();
            File.WriteAllBytes(audio.FullName, [0x52, 0x49, 0x46, 0x46]);

            Add(context, new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Kind = ArtifactKind.Audio,
                Origin = ArtifactKind.Audio.OriginOf(),
                RelativePath = CorpusFiles.PathFor(meeting, "audio.wav"),
                ByteSize = 4,
                Sha256 = new string('a', 64),
                ConfirmedAt = startedAt,
            });
        }

        if (responseSha256 is not null)
        {
            Transcribed(context, meeting, startedAt, responseSha256);
        }

        return meeting;
    }

    /// <summary>
    /// One extraction of a meeting, with a decision, an action and an open question on it, each
    /// cited to a turn of its own.
    /// </summary>
    /// <param name="accepted">
    /// When a person accepted it, or nothing. A run nobody accepted is the case every reader of an
    /// extraction has to leave alone, so it is a parameter and not an assumption.
    /// </param>
    /// <param name="saying">
    /// What it settled. The action and the open question say the same with a word after it, so a
    /// suite can tell which section answered without three more parameters.
    /// </param>
    /// <param name="citedTurn">
    /// Which turn the decision anchors on. It has to be a turn that exists, and it is what the
    /// action and the open question anchor on too where <paramref name="actionAt"/> or
    /// <paramref name="questionAt"/> is not given.
    /// </param>
    /// <param name="actionAt">Which turn the action anchors on, or the same one the decision does.</param>
    /// <param name="questionAt">Which turn the open question anchors on, or the same one.</param>
    public static Guid Extracted(
        CorpusDbContext context,
        Guid meeting,
        UtcTimestamp when,
        UtcTimestamp? accepted,
        string saying,
        int citedTurn = 0,
        int? actionAt = null,
        int? questionAt = null)
    {
        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Extract, $"{meeting}/{Guid.NewGuid()}", when);

        Add(context, job);

        var run = new ExtractionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "claude-code",
            PromptVersion = "1",
            SchemaVersion = "1",
            InputHash = new string('d', 64),
            CreatedAt = when,
            AcceptedAt = accepted,
        };

        Add(context, run);

        Add(context, new Summary
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Abstract = saying,
            CreatedAt = when,
        });

        Add(context, new Decision
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Statement = saying,
            Evidence = Citing(context, meeting, citedTurn),
            CreatedAt = when,
        });

        Add(context, new ActionItem
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Statement = $"{saying}, to do",
            Evidence = Citing(context, meeting, actionAt ?? citedTurn),
            CreatedAt = when,
        });

        Add(context, new OpenQuestion
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            ExtractionRunId = run.Id,
            Ordinal = 0,
            Question = $"{saying}, unresolved",
            Evidence = Citing(context, meeting, questionAt ?? citedTurn),
            CreatedAt = when,
        });

        return run.Id;
    }

    /// <summary>How far into the meeting the turn at each position was said.</summary>
    public static Duration At(int ordinal) => Duration.FromMilliseconds((ordinal + 1) * 1_000);

    /// <summary>
    /// The label every turn these build carries. A label and never a name, which is the contract:
    /// the channel is part of it, and a single track is the only thing without one.
    /// </summary>
    public const string SpeakerLabel = "ch1:speaker_0";

    /// <summary>
    /// The artifact every citation these build was quoted out of. Deliberately not the response
    /// <see cref="Recorded"/> files: what a decision was quoted out of and what the transcript is
    /// made of now are two facts, and they come apart the moment a meeting is transcribed twice —
    /// which is what a suite asserting either of them needs to be able to see.
    /// </summary>
    public const string QuotedFromSha256 =
        "0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e0e";

    public static void Add(CorpusDbContext context, object row)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Add(row);
        context.SaveChanges();
    }

    /// <summary>
    /// The paid response, filed and recorded, for a suite that needs a run without a meeting's turns
    /// coming out of one.
    /// </summary>
    /// <param name="finished">
    /// Whether the run came back. <c>FinishedAt</c> is set together with <c>ResponseArtifactId</c>
    /// and never apart from it, so a run that was queued and never came back — the state a reader
    /// has to leave alone — is a row with neither.
    /// </param>
    public static void Transcribed(
        CorpusDbContext context,
        Guid meeting,
        UtcTimestamp when,
        string? responseSha256 = null,
        bool finished = true)
    {
        Artifact? response = null;

        if (responseSha256 is not null)
        {
            response = new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting,
                Kind = ArtifactKind.DeepgramResponse,
                Origin = ArtifactKind.DeepgramResponse.OriginOf(),
                RelativePath = CorpusFiles.PathFor(meeting, "deepgram.json"),
                ByteSize = 4,
                Sha256 = responseSha256,
                ConfirmedAt = when,
            };

            Add(context, response);
        }

        var job = ProcessingJob.Queue(
            Guid.NewGuid(), meeting, JobKind.Transcribe, $"{meeting}/{Guid.NewGuid()}", when);

        Add(context, job);

        Add(context, new TranscriptionRun
        {
            Id = Guid.NewGuid(),
            MeetingId = meeting,
            JobId = job.Id,
            Provider = "deepgram",
            Model = "nova-3",
            SourceProfile = SourceProfile.Multichannel,
            Language = "es",
            AudioSha256 = new string('b', 64),
            BillableConfigHash = new string('c', 64),
            ResponseArtifactId = response?.Id,
            CreatedAt = when,
            FinishedAt = finished ? when : null,
        });
    }

    /// <summary>
    /// A citation anchored on a turn that is really there, carrying what that turn really says. The
    /// corpus refuses one that lands nowhere, and a quotation that does not match the turn is the
    /// failure every reader of a citation exists to catch.
    /// </summary>
    /// <remarks>
    /// <c>FirstOrDefault</c> and not <c>Single</c>: <c>(MeetingId, Ordinal)</c> is an alternate key
    /// on <c>utterances</c>, so a second turn at one ordinal is refused before this ever runs, and
    /// the two are equivalent here.
    /// </remarks>
    public static Citation Citing(CorpusDbContext context, Guid meeting, int ordinal)
    {
        var turn = context.Utterances
            .FirstOrDefault(row => row.MeetingId == meeting && row.Ordinal == ordinal)
            ?? throw new InvalidOperationException(
                $"Meeting {meeting} has no turn at {ordinal} to cite. Record it with enough turns "
                + "first: a citation that lands nowhere is refused by the corpus.");

        return new Citation
        {
            MeetingId = meeting,
            UtteranceOrdinal = turn.Ordinal,
            Start = turn.Start,
            End = turn.End,
            SpeakerLabel = turn.SpeakerLabel,
            QuotedText = turn.Text,
            SourceArtifactSha256 = QuotedFromSha256,
        };
    }
}
