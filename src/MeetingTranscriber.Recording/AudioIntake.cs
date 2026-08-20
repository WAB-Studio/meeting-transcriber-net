using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// What a caller knows about audio it is bringing in that the file cannot say.
/// </summary>
/// <remarks>
/// <b>There is no profile on it, and the absence is the contract.</b> What the audio is gets
/// decided by <see cref="AudioIntake"/> from the file and the folder it came in, so there is
/// nowhere for a caller — a command, a window, a person at a prompt — to declare that two channels
/// are the meeting's two sources. Somebody who could say it would eventually say it about a stereo
/// file off a phone, and every turn in that meeting would then be filed as having come from a
/// device that never recorded it.
/// </remarks>
/// <param name="StartedAt">
/// When the meeting was, which no audio file says. It is asked for rather than taken from the
/// file's timestamp: when a file was written is when somebody copied it, and a meeting filed under
/// the day it was moved between disks is one nothing later can put back.
/// </param>
/// <param name="Language">What was spoken, which nothing here can know either.</param>
public sealed record BroughtDetails(
    UtcTimestamp StartedAt,
    string Language,
    string? Title = null,
    string? Context = null);

/// <summary>What one file brought in became.</summary>
/// <param name="MeetingId">The meeting it is now, minted here and never read off the file.</param>
/// <param name="Audio">The row describing the meeting's audio, hashed as it was written.</param>
/// <param name="Profile">What it was filed as, which nobody was asked.</param>
/// <param name="Length">How long it turned out to be, counted off the audio the corpus now holds.</param>
/// <param name="MixedDown">
/// Whether the channels were averaged into one on the way in. Reported rather than inferred from
/// the profile: a file that was already a single track is diarized too, and it went in untouched.
/// </param>
/// <param name="WasAlreadyThere">
/// Whether the corpus already held this audio. The same file handed over twice is the meeting
/// that is already here, so this says which of the two happened rather than leaving a caller to
/// read it off a meeting id it has never seen before.
/// </param>
public sealed record BroughtMeeting(
    Guid MeetingId,
    Artifact Audio,
    SourceProfile Profile,
    Duration Length,
    bool MixedDown,
    bool WasAlreadyThere);

/// <summary>
/// Audio somebody brought becoming a meeting of this corpus: what the file is decided from the
/// file, the audio filed as the source it is, and nothing asked of anybody.
/// </summary>
/// <remarks>
/// <para>
/// The door beside <c>MeetingIntake</c>, which takes a paid response somebody already has. This
/// one takes audio nobody has paid for anything about yet, so what it produces is a meeting at
/// <see cref="MeetingStage.Recorded"/> — the same rung a recording that was just stopped lands on,
/// with transcribing it a separate press somebody makes once they have decided they want it.
/// Nothing is queued here for the same reason nothing is queued by stopping.
/// </para>
/// <para>
/// <b>Two channels are the meeting's two sources only when the audio is what this application
/// records and the folder it came in says this application recorded it.</b> Both halves, and the
/// audio is asked first: a card is five keys of plain JSON that docs/corpus.md invites a person to
/// read and carry around, so on its own it is the weakest evidence in the folder, while a file
/// being bit-for-bit the shape <see cref="MeetingAudio.Interchange"/> fixes is the hardest thing
/// there to arrive at by accident. Everything else — one track or six, off a phone, out of a
/// conferencing tool, exported by something nobody here has heard of — is a single track: averaged
/// down to mono and transcribed with the speakers told apart by the provider. That is not a
/// default anybody may override, and there is no argument for overriding it, because the cost of
/// being wrong is not a worse transcript: channel 0 is the loopback and channel 1 is the
/// microphone, so a stereo file taken as two sources puts the user's name on words a stranger
/// said.
/// </para>
/// <para>
/// It sits here rather than in <c>Processing</c> because it is the same join <c>Recording</c>
/// exists for: reading a WAV is the audio engine's and filing a meeting is the corpus's, and this
/// is the only project allowed to touch both.
/// </para>
/// </remarks>
public static class AudioIntake
{
    /// <summary>What the mix down is written under before the corpus takes it.</summary>
    private const string Mixing = ".mixdown";

    /// <summary>
    /// Brings <paramref name="audio"/> into <paramref name="corpus"/> as a meeting of its own, or
    /// answers with the meeting this audio already is.
    /// </summary>
    /// <remarks>
    /// The file is read and what it is is settled before a row exists, and the order is the point.
    /// A file this build cannot open, or one whose shape and whose folder cannot be reconciled, is
    /// refused with the corpus untouched; refused a step later it would leave a meeting with no
    /// audio under it and a folder somebody has to work out how to clean up.
    /// </remarks>
    public static BroughtMeeting Bring(
        CorpusDbContext corpus,
        FileInfo audio,
        BroughtDetails details,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.Language);

        EnsureItCameFromOutside(corpus, audio);

        var format = AudioFiles.FormatOf(audio);
        var profile = ProfileOf(audio, format);

        var meetingId = Guid.NewGuid();
        var destination = CorpusFiles.Locate(
            corpus.Root, CorpusFiles.PathFor(meetingId, MeetingAudio.FileName));

        // Beside where the audio is going rather than in the system's temp folder: it is the
        // corpus's own volume, so nothing copies across one, and it carries the suffix that says a
        // write never finished — which is what makes a machine dying here leave something `sweep`
        // already knows to remove instead of a WAV nobody can account for.
        var mixed = new FileInfo($"{destination.FullName}{Mixing}{CorpusFiles.UnfinishedSuffix}");

        // Everything but this application's own recording, and only when there is more than one
        // channel to average. A multichannel file goes in as it is — mixing it down is exactly the
        // loss the profile exists to prevent.
        var mixDown = profile is SourceProfile.Diarize && !AudioFiles.IsOneTrack(format);

        try
        {
            AudioOnDisk stored;
            FileInfo bytes;

            if (mixDown)
            {
                destination.Directory!.Create();
                stored = AudioFiles.MixDownToOneTrack(audio, mixed);
                bytes = mixed;
            }
            else
            {
                stored = AudioFiles.Read(audio);
                bytes = audio;
            }

            // The contract's own rule, asked once and about the audio that is going in rather than
            // the one that arrived.
            //
            // It cannot fail as the two profiles stand, and that is said here rather than left for
            // somebody to work out: multichannel is only reached by a file that already matched
            // every field of the interchange format, and diarize is only left with one channel
            // because the mix down above put it there. What it is is the alarm for a third profile
            // — a member added to the enum reaches this line before it reaches the corpus, and
            // arrives at a channel count nobody decided for it rather than at a meeting.
            profile.EnsureChannelCount(stored.Format.Channels);

            // The same bytes handed over twice are the meeting that is already here, which is what
            // somebody re-running a command that half worked is doing. Hashed after the mix down
            // because the mix down is deterministic — the same file poured through the same code
            // gives the same track — so what is compared is what would land, not what arrived.
            var sha256 = CorpusFiles.Sha256Of(bytes);
            var already = corpus.Artifacts.FirstOrDefault(
                row => row.Kind == ArtifactKind.Audio && row.Sha256 == sha256);

            var filed = already ?? Filed(corpus, meetingId, audio, bytes, profile, stored, details, now);

            if (already is not null)
            {
                // A meeting the corpus knows and whose audio the disk has lost. Somebody handing
                // the file over again is the ordinary way that gets noticed, and the bytes that
                // would put it right are open in this method. They go back through the same door
                // the restore command uses and on the same terms: the corpus finds the rows these
                // bytes belong under, and nothing here hands it a row of its own.
                using var content = bytes.OpenRead();
                ArtifactRestore.Restore(corpus, content, now);
                MeetingManifest.Write(corpus, already.MeetingId, now);
            }

            return new BroughtMeeting(
                filed.MeetingId, filed, profile, stored.Length, mixDown, already is not null);
        }
        finally
        {
            if (mixDown)
            {
                // Whether it worked or not. The corpus has its own copy the moment the artifact
                // lands, and what is left here is a working file — kept, it would be a second copy
                // of a meeting's audio under a name nothing looks for.
                BlockSpool.Erase(mixed);
                RemoveIfNothingLanded(destination.Directory!);
            }
        }
    }

    /// <summary>
    /// What the audio is, decided from the audio first and from the folder it came in second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The audio decides first because it is the half that cannot be typed. A file that is not the
    /// exact shape this application's recordings come out as was not made by it, whatever JSON
    /// happens to be sitting beside it — which is also what keeps an unrelated
    /// <c>manifest.json</c> from being read at all, and keeps a hand-written one from turning
    /// somebody's 24-bit stereo export into a meeting whose second channel is asserted to be the
    /// user's own microphone.
    /// </para>
    /// <para>
    /// A file that <em>is</em> that shape is this application's own recording or a coincidence, and
    /// the card is what tells the two apart. A meeting's folder holds one <c>audio.wav</c> and one
    /// <c>manifest.json</c> describing it, so a card is evidence about that file and about nothing
    /// else somebody happened to drop in the folder.
    /// </para>
    /// <para>
    /// With no card there is no answer, and the refusal is the point. Both alternatives lose
    /// something nobody can get back: filed as two sources it would put a name on a stranger's
    /// words, and averaged to mono it would destroy the split between what the machine played and
    /// what the microphone heard — silently, on a recording this application made, which is the one
    /// loss nobody would ever find out about. Refusing is not asking what a channel carries; it is
    /// saying which folder the file has to be brought in from.
    /// </para>
    /// </remarks>
    private static SourceProfile ProfileOf(FileInfo audio, StreamFormat format)
    {
        if (!AudioFiles.IsWhatThisApplicationRecords(format))
        {
            return SourceProfile.Diarize;
        }

        if (CardAbout(audio) is not { } card)
        {
            throw new RecordingException(
                $"'{audio.FullName}' is {format}, which is exactly what a recording of this "
                + "application comes out as, and nothing beside it says whether it is one. Bring "
                + $"it in from the folder it was filed in, where the {MeetingManifest.FileName} "
                + "next to it says what its channels are — taking it as two sources would put a "
                + "name on words a stranger said, and mixing it down would throw the two sources "
                + "away.");
        }

        return card.Profile;
    }

    /// <summary>
    /// The recovery card this file is the audio of, or nothing when no card is about this file.
    /// </summary>
    /// <remarks>
    /// A card that is there and cannot be read is neither, and it throws rather than being treated
    /// as absent. A spool folder's card lands here too — <see cref="MeetingAudio.Materialise"/>
    /// writes an <c>audio.wav</c> beside the blocks — and it is refused by that reader, because it
    /// answers a different set of questions. That is the right answer for a different reason as
    /// well: those blocks reach the corpus through recovery, so bringing their playback in here
    /// would be the same meeting twice.
    /// </remarks>
    private static MeetingCard? CardAbout(FileInfo audio)
    {
        if (!string.Equals(audio.Name, MeetingAudio.FileName, StringComparison.OrdinalIgnoreCase)
            || audio.Directory is not { } folder)
        {
            return null;
        }

        var card = new FileInfo(Path.Combine(folder.FullName, MeetingManifest.FileName));
        return card.Exists ? MeetingManifest.Read(card) : null;
    }

    /// <summary>
    /// Throws when the audio is already a file of this corpus.
    /// </summary>
    /// <remarks>
    /// Audio is brought in from outside, and a meeting's own <c>audio.wav</c> handed to this would
    /// be that meeting a second time under a new id — with its card read as the origin evidence for
    /// the copy. It is the one duplicate the hash cannot catch on the way past, because the copy is
    /// mixed down and no longer hashes to what the corpus already holds.
    /// </remarks>
    private static void EnsureItCameFromOutside(CorpusDbContext corpus, FileInfo audio)
    {
        var relative = Path.GetRelativePath(corpus.Root.FullName, audio.FullName);

        if (!Path.IsPathRooted(relative)
            && !relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new RecordingException(
                $"'{audio.FullName}' is already a file of this corpus. Audio is brought in from "
                + "outside it, and a meeting's own audio brought in again would be that meeting a "
                + "second time under an id of its own.");
        }
    }

    /// <summary>
    /// Takes back the folder made for a mix down when nothing was filed into it.
    /// </summary>
    /// <remarks>
    /// It swallows what it cannot do, for the reason <see cref="BlockSpool.Erase"/> does: it runs
    /// on the way out of a filing that may already be failing, and what the caller has to hear is
    /// why that happened rather than that a directory would not go.
    /// </remarks>
    private static void RemoveIfNothingLanded(DirectoryInfo folder)
    {
        try
        {
            folder.Refresh();
            if (folder.Exists && !folder.EnumerateFileSystemInfos().Any())
            {
                folder.Delete();
            }
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see the remarks.
        }
    }

    /// <summary>
    /// The meeting, the audio it is built on and the card that says what it is, as one thing.
    /// </summary>
    /// <remarks>
    /// The row exists before an artifact can point at it, and the card is written before the
    /// commit rather than after it. Written after, a card that would not land would leave the
    /// command reporting a refusal over a meeting that is in the corpus for good — which is the
    /// one shape a person cannot act on, because what they are told happened and what happened
    /// disagree.
    /// </remarks>
    private static Artifact Filed(
        CorpusDbContext corpus,
        Guid meetingId,
        FileInfo brought,
        FileInfo bytes,
        SourceProfile profile,
        AudioOnDisk stored,
        BroughtDetails details,
        UtcTimestamp now)
    {
        using var filing = corpus.Database.CurrentTransaction is null
            ? corpus.Database.BeginTransaction()
            : null;

        corpus.Meetings.Add(new Meeting
        {
            Id = meetingId,
            Title = details.Title,
            Context = details.Context,
            StartedAt = details.StartedAt,

            // Counted off the audio that is going in, and never off the file that arrived: a mixed
            // down copy is the meeting from here, and a length taken from the other one would be
            // the number every citation is checked against describing a file the corpus does not
            // hold.
            Duration = stored.Length,
            SourceProfile = profile,
            Language = details.Language,
            LifecycleState = LifecycleState.Active,
            CreatedAt = now,
            UpdatedAt = now,
        });

        // Where this meeting came from, in the table provenance belongs in, and under a verb of its
        // own: a meeting made out of a WAV somebody had and one whose response they paid Deepgram
        // for are different things to find later, and one word for both would make the audit unable
        // to tell them apart.
        corpus.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = now,
            Actor = AuditActor.App,
            Action = "audio imported",
            MeetingId = meetingId,
            Detail = $"the audio at '{brought.FullName}'",
        });

        corpus.SaveChanges();

        var audio = DurableArtifact.Write(
            corpus,
            meetingId,
            ArtifactKind.Audio,
            CorpusFiles.PathFor(meetingId, MeetingAudio.FileName),
            now,
            into =>
            {
                using var source = bytes.OpenRead();
                source.CopyTo(into);
            });

        MeetingManifest.Write(corpus, meetingId, now);

        filing?.Commit();
        return audio;
    }
}
