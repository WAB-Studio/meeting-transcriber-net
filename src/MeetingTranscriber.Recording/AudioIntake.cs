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
/// <param name="PutBack">
/// The paths that had no file and have one again, because the audio handed over turned out to be
/// what a row of this corpus was missing. It is here so that a caller can say it happened: a
/// command that answers "this audio was already here" and has quietly written a file is telling
/// somebody nothing changed while something did, and the one thing they would have wanted to know
/// is that their corpus had a hole in it.
/// </param>
public sealed record BroughtMeeting(
    Guid MeetingId,
    Artifact Audio,
    SourceProfile Profile,
    Duration Length,
    bool MixedDown,
    bool WasAlreadyThere,
    IReadOnlyList<string> PutBack);

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
/// conferencing tool, exported by something nobody here has heard of, and this application's own
/// recording arriving without the folder that says so — is a single track: averaged down to mono
/// and transcribed with the speakers told apart by the provider. Those are the two outcomes and
/// there is no third, because nothing here can tell this application's own recording, dragged out
/// of its folder, from a stereo export that happens to match it — so a refusal aimed at the first
/// turns away the second, which is a meeting somebody has. That is not a default anybody may
/// override either, and there is no argument for overriding it, because the cost of being wrong is
/// not a worse transcript: channel 0 is the loopback and channel 1 is the microphone, so a stereo
/// file taken as two sources puts the user's name on words a stranger said. What the mix down
/// costs is said instead of hidden — <see cref="BroughtMeeting.MixedDown"/> at the time, and the
/// meeting's own history afterwards.
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
    /// A file this build cannot open, or one that is already a file of this corpus, is refused with
    /// the corpus untouched; refused a step later it would leave a meeting with no audio under it
    /// and a folder somebody has to work out how to clean up.
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

            // One handle, opened here and kept for as long as these bytes are needed. What is
            // hashed and what is restored from have to be the same read: in the branch that did not
            // mix down these are the person's own file, on a path anything may replace, and two
            // opens would let the corpus be told one thing and given another.
            using var content = bytes.Open(new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
            });

            // The same bytes handed over twice are the meeting that is already here, which is what
            // somebody re-running a command that half worked is doing. Hashed after the mix down
            // because the mix down is deterministic — the same file poured through the same code
            // gives the same track — so what is compared is what would land, not what arrived.
            var sha256 = CorpusFiles.Sha256Of(content);

            // Ordered, so that identical input gives the same answer twice. Nothing today puts two
            // audio rows under one hash — this door dedupes on it and a meeting's own audio never
            // comes back through — but an unordered read of a non-unique index is a meeting id
            // chosen by whatever the database happened to return first, which is not a fact.
            var already = corpus.Artifacts
                .Where(row => row.Kind == ArtifactKind.Audio && row.Sha256 == sha256)
                .OrderBy(row => row.RelativePath)
                .FirstOrDefault();

            var filed = already
                ?? Filed(corpus, meetingId, audio, bytes, profile, format, stored, details, now);
            IReadOnlyList<string> putBack = [];

            if (already is not null)
            {
                // A meeting the corpus knows and whose audio the disk has lost. Somebody handing
                // the file over again is the ordinary way that gets noticed, and the bytes that
                // would put it right are open in this method. They go back through the same door
                // the restore command uses and on the same terms: the corpus finds the rows these
                // bytes belong under, and nothing here hands it a row of its own.
                //
                // What it did is carried back out rather than dropped. Restoring is a decision with
                // a person in it, and the person here decided by handing the file over — but they
                // asked to bring audio in, so a file put back is not what they were expecting and
                // is the half of the answer they would not otherwise get.
                putBack = ArtifactRestore.Restore(corpus, content, now).PutBack;

                // On every filing, including one that found the meeting already here. That is
                // ISC-50's rule and not a decision taken here: a meeting's folder carries a card
                // saying what the corpus now says about it, after it is filed, filed again, renamed
                // or rebuilt — so a filing that finds the card missing, or saying what the corpus
                // no longer says, is what puts it right. It is not reported for the same reason:
                // the card is produced from the row every time and replacing it destroys nothing,
                // which is what makes it different from the source that came back above.
                MeetingManifest.Write(corpus, already.MeetingId, now);
            }

            return new BroughtMeeting(
                filed.MeetingId, filed, profile, stored.Length, mixDown, already is not null, putBack);
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
    /// <b>Vouched or not vouched, and nothing in between.</b> A card that is not there, and a card
    /// that is there and does not read as one, are the same answer — because a refusal is the one
    /// outcome this cannot afford. Nothing in this build can tell this application's own recording,
    /// dragged out of its folder, from somebody's 16 kHz stereo export, so a refusal aimed at the
    /// first lands on the second, and what it turns away is a meeting somebody has.
    /// <c>manifest.json</c> is not a name this product owns either — a browser extension, an MSIX
    /// package and a web app all write one — so a file refused for the JSON beside it would be
    /// refused over a file its owner never thought about.
    /// </para>
    /// </remarks>
    private static SourceProfile ProfileOf(FileInfo audio, StreamFormat format)
    {
        if (!AudioFiles.IsWhatThisApplicationRecords(format))
        {
            return SourceProfile.Diarize;
        }

        return CardAbout(audio)?.Profile ?? SourceProfile.Diarize;
    }

    /// <summary>
    /// The recovery card this file is the audio of, or nothing when nothing beside it is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A card that will not read as a meeting's is nothing rather than a refusal, and it is the
    /// only honest answer: this is reached by a file whose name and shape somebody else's export
    /// can have, so throwing here would turn any <c>manifest.json</c> in the folder — a browser
    /// extension's, a package's, a web app's — into a reason to reject their audio. The rule that
    /// gives is the one nobody would design: delete the JSON you did not know was there and the
    /// same import goes through.
    /// </para>
    /// <para>
    /// It costs nothing that was being protected. A recording of this application that has not been
    /// stopped yet is spooled under the corpus's own <c>spool/</c>, so its playback never reaches
    /// this method at all — <see cref="EnsureItCameFromOutside"/> runs first and refuses it by
    /// where it is, which is a fact about this corpus rather than a guess about a file.
    /// </para>
    /// </remarks>
    private static MeetingCard? CardAbout(FileInfo audio)
    {
        if (!string.Equals(audio.Name, MeetingAudio.FileName, StringComparison.OrdinalIgnoreCase)
            || audio.Directory is not { } folder)
        {
            return null;
        }

        var card = new FileInfo(Path.Combine(folder.FullName, MeetingManifest.FileName));
        if (!card.Exists)
        {
            return null;
        }

        try
        {
            return MeetingManifest.Read(card);
        }
        catch (ManifestException)
        {
            return null;
        }
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
    /// What the file was when it arrived and what became of its channels, for the line the corpus
    /// keeps about it.
    /// </summary>
    /// <remarks>
    /// Both halves in one sentence, because the second only means anything against the first: a
    /// meeting that says one channel says nothing about whether there were ever two.
    /// </remarks>
    private static string ArrivedAs(StreamFormat arrived, StreamFormat stored) =>
        arrived.Channels == stored.Channels
            ? $"which arrived as {arrived} and went in as it was"
            : $"which arrived as {arrived} and went in with its {arrived.Channels} channels "
              + "averaged into one";

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
        StreamFormat arrived,
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
        //
        // It says what the file arrived as and not only where it was, and that half is the whole
        // point of the line. A file whose channels were averaged on the way in and a file that
        // only ever had one are the same meeting afterwards in every other place the corpus looks
        // — same profile, same length, same card, field for field — so without this the only
        // account of a meeting having lost its two sources is a line of console output from the
        // afternoon somebody ran the command.
        corpus.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = now,
            Actor = AuditActor.App,
            Action = "audio imported",
            MeetingId = meetingId,
            Detail = $"the audio at '{brought.FullName}', {ArrivedAs(arrived, stored.Format)}",
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
