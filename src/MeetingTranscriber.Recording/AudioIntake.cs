using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
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
/// When the meeting was, as somebody typed it, or nothing. The card of a folder this application
/// wrote says it instead, and a typed value that card contradicts is refused. With no such card it
/// has to be given, and it is still asked for rather than taken from the file's timestamp: when a
/// file was written is when somebody copied it, and a meeting filed under the day it was moved
/// between disks is one nothing later can put back.
/// </param>
/// <param name="Language">
/// What was spoken, as somebody typed it, or nothing — under the same rule as
/// <paramref name="StartedAt"/>.
/// </param>
/// <param name="Title">
/// What the meeting is called, as somebody typed it. A card with a name rules over it; a card with
/// none takes it.
/// </param>
/// <param name="Context">What somebody said the meeting was about. Always taken.</param>
/// <param name="LanguageWhenNothingSays">
/// What the meeting is taken to be spoken in when neither the folder nor <paramref name="Language"/>
/// says: the caller's default, applied only once the card has been read, so that it never stands in
/// for a card that says otherwise.
/// </param>
public sealed record BroughtDetails(
    UtcTimestamp? StartedAt,
    string? Language,
    string? Title = null,
    string? Context = null,
    string? LanguageWhenNothingSays = null);

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
/// <b>Nothing is queued here, and stopping a recording now does queue what was settled.</b> The two
/// are not the same act and the preference says which it is about: somebody answered what should
/// happen when a recording of theirs ends, and audio arriving from outside is not one. What that
/// person meant about a folder somebody hands the application is a question nobody has been
/// asked.
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
    /// and a folder somebody has to work out how to clean up. So is what the folder's card says
    /// about when, in what and under what name, and a typed value it contradicts is refused at the
    /// same point, before a row or a byte.
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

        if (details.Language is { } typed && string.IsNullOrWhiteSpace(typed))
        {
            throw new ArgumentException("A language that is given cannot be blank.", nameof(details));
        }

        if (details.LanguageWhenNothingSays is { } fallback && string.IsNullOrWhiteSpace(fallback))
        {
            throw new ArgumentException(
                "A language to fall back to that is given cannot be blank.", nameof(details));
        }

        EnsureItCameFromOutside(corpus, audio);

        var format = AudioFiles.FormatOf(audio);
        var card = Vouching(audio, format);
        var profile = card?.Profile ?? SourceProfile.Diarize;
        var settled = Settle(card, details, audio);

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

            // The card this produces is dropped, and that is the answer rather than an oversight.
            // `ImportAudio` reports no card and the `already` branch below writes one it does not
            // report either, so a field on `BroughtMeeting` would be a public record widened for a
            // reader that does not exist.
            var filed = already ?? MeetingArchive.New(
                corpus,
                new NewMeeting(
                    meetingId,
                    settled.StartedAt,

                    // Counted off the audio that is going in, and never off the file that arrived: a
                    // mixed down copy is the meeting from here, and a length taken from the other
                    // one would be the number every citation is checked against describing a file
                    // the corpus does not hold.
                    stored.Length,
                    profile,
                    settled.Language,
                    settled.Title,
                    settled.Context),
                new ArrivedOn(
                    Kind: ArtifactKind.Audio,
                    FileName: MeetingAudio.FileName,
                    Contents: into =>
                    {
                        using var from = bytes.OpenRead();
                        from.CopyTo(into);
                    },
                    Verb: "audio imported",

                    // What the file arrived as and not only where it was, which is the whole point
                    // of the line. A file whose channels were averaged on the way in and a file
                    // that only ever had one are the same meeting afterwards in every other place
                    // the corpus looks — same profile, same length, same card, field for field — so
                    // without this the only account of a meeting having lost its two sources is a
                    // line of console output from the afternoon somebody ran the command.
                    Detail: $"the audio at '{audio.FullName}', {ArrivedAs(format, stored.Format)}"),
                now).Source;
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
    /// The card this file is the audio of, when it is held to that file by shape; nothing otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The audio decides first because it is the half that cannot be typed. A card is held to the
    /// file beside it by shape: <c>multichannel</c> vouches only for a file that is exactly the
    /// shape this application's recordings come out as, and <c>diarize</c> only for a single track.
    /// So a hand-written card cannot turn somebody's 24-bit stereo export into a meeting whose
    /// second channel is asserted to be the user's own microphone. What the shape cannot tell apart
    /// is another recording of that same shape: nothing on a card binds it to one file's bytes, so a
    /// different recording saved in a copied folder under the card's own name takes what the card
    /// says. That was already true of the profile at the interchange shape; it is accepted rather
    /// than closed because the card is five keys a person may read and carry, and giving it a hash
    /// would make it a second copy of the corpus's own record.
    /// </para>
    /// <para>
    /// A file that <em>is</em> one of those shapes is this application's own recording or a
    /// coincidence, and the card is what tells the two apart. A meeting's folder holds one
    /// <c>audio.wav</c> and one <c>manifest.json</c> describing it, so a card is evidence about that
    /// file and about nothing else somebody happened to drop in the folder. The same card is what
    /// settles when the meeting started, what it was spoken in and what it was called, and it is
    /// held to the same test for that.
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
    private static MeetingCard? Vouching(FileInfo audio, StreamFormat format)
    {
        var card = CardAbout(audio);

        return card?.Profile switch
        {
            SourceProfile.Multichannel when AudioFiles.IsWhatThisApplicationRecords(format) => card,
            SourceProfile.Diarize when AudioFiles.IsOneTrack(format) => card,
            _ => null,
        };
    }

    private sealed record Settled(UtcTimestamp StartedAt, string Language, string? Title, string? Context);

    /// <summary>
    /// What the meeting's start, language and name are: the vouching card's when there is one, and
    /// what was typed otherwise.
    /// </summary>
    /// <remarks>
    /// A typed value a card contradicts is refused, all of them in one message, because a date
    /// silently replacing the one the recording carries is one nothing afterwards can account for.
    /// The start is compared to the millisecond, the language ignoring case and the name exactly,
    /// both after trimming what was typed; a card with no name contradicts none.
    /// </remarks>
    private static Settled Settle(MeetingCard? card, BroughtDetails details, FileInfo audio)
    {
        if (card is null)
        {
            var started = details.StartedAt
                ?? throw new RecordingException(
                    $"Nothing beside '{audio.FullName}' says when the meeting started, so it has to be given. Nothing was filed.");
            var language = details.Language ?? details.LanguageWhenNothingSays
                ?? throw new RecordingException(
                    $"Nothing beside '{audio.FullName}' says what the meeting was spoken in, so it has to be given. Nothing was filed.");

            return new Settled(started, language, details.Title, details.Context);
        }

        var contradictions = new List<string>();

        if (details.StartedAt is { } typedStart && typedStart != card.StartedAt)
        {
            contradictions.Add(
                $"It says the meeting started at {card.StartedAt.ToStorage()} and {typedStart.ToStorage()} was given.");
        }

        if (details.Language?.Trim() is { } typedLanguage
            && !string.Equals(typedLanguage, card.Language, StringComparison.OrdinalIgnoreCase))
        {
            contradictions.Add(
                $"It says it was spoken in '{card.Language}' and '{typedLanguage}' was given.");
        }

        if (card.Title is not null
            && details.Title?.Trim() is { } typedTitle
            && !string.Equals(typedTitle, card.Title, StringComparison.Ordinal))
        {
            contradictions.Add(
                $"It says it was called '{card.Title}' and '{typedTitle}' was given.");
        }

        if (contradictions.Count > 0)
        {
            var cardPath = Path.Combine(audio.Directory!.FullName, MeetingManifest.FileName);

            throw new RecordingException(
                $"'{cardPath}' is the card this application wrote beside '{audio.FullName}', and a typed value does not replace what it says about the recording. "
                + string.Join(' ', contradictions)
                + " If this file is not the one the card is about, move it out of this folder. Nothing was filed.");
        }

        return new Settled(card.StartedAt, card.Language, card.Title ?? details.Title, details.Context);
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
    /// <para>
    /// It swallows what it cannot do, for the reason <see cref="BlockSpool.Erase"/> does: it runs
    /// on the way out of a filing that may already be failing, and what the caller has to hear is
    /// why that happened rather than that a directory would not go.
    /// </para>
    /// <para>
    /// What it takes back is the corpus meeting folder <see cref="Bring"/> made for a mix down an
    /// instant earlier, under an id it minted itself, and only while it holds nothing at all. Never
    /// a folder under <c>spool/</c>, so never a recording somebody is still owed a decision about.
    /// That sentence is what the folder-removal guard in <c>UnfinishedRecordingsTests</c> allows
    /// this file on, and it is the half of the allowance no sweep over text can check: the guard
    /// sees which way a folder goes and never which folder. A removal here that could reach one
    /// takes this method off that list rather than the list off this method.
    /// </para>
    /// </remarks>
    private static void RemoveIfNothingLanded(DirectoryInfo folder)
    {
        try
        {
            folder.Refresh();
            if (folder.Exists && !folder.EnumerateFileSystemInfos().Any())
            {
                Directory.Delete(folder.FullName);
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
}
