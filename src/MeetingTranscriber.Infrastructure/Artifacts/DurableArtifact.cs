using System.Security.Cryptography;
using System.Text;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>A durable write that could not be completed, named so the caller can say which one.</summary>
public sealed class ArtifactWriteException(string message) : Exception(message);

/// <summary>
/// The one way an artifact reaches the corpus: written whole somewhere else, checked, and only
/// then put where its name says it is.
/// </summary>
/// <remarks>
/// <para>
/// Six steps, in this order and never another: a temporary on the same volume, buffers emptied and
/// the handle closed, size and SHA-256, the content read back to prove it is there, an atomic
/// replace, and the confirmed artifact recorded in a SQLite transaction. The order is the whole
/// design. The filesystem and the database cannot be moved together, so one of them has to be
/// wrong first when the power goes, and this is the direction where being wrong is recoverable: a
/// file nothing has recorded is found by <see cref="ArtifactReconciler"/> and can be looked at,
/// while a row pointing at a file that was never written is a corpus that lies about what it holds
/// and says nothing when it is read.
/// </para>
/// <para>
/// So the invariant this type exists for is one sentence: <b>a row in <c>artifacts</c> always names
/// a file that is there and re-reads to the size and the hash the row carries.</b> Every way a
/// write can be cut leaves either less than that or nothing at all.
/// </para>
/// <para>
/// A meeting's two derived files are one act and not two, so <see cref="WriteAllText"/> is what
/// writes them: steps one to four happen for every file, and every destination is emptied, before
/// step five happens for any of them. Writing them one whole write after another was the shape
/// until 2026-09-02, and it left a meeting holding one file of each generation whenever the second
/// was refused — a transcript naming turns the jsonl does not, with every row still agreeing with
/// the file it names, so nothing reported it.
/// </para>
/// </remarks>
public static class DurableArtifact
{
    /// <summary>Writes an artifact and records it. What almost every caller wants.</summary>
    /// <param name="context">
    /// The corpus: the rows and the folder they describe. Its transaction, if it has one, is the
    /// one the row joins.
    /// </param>
    /// <param name="contents">
    /// Writes the artifact. Called once, on a stream that is hashing what goes through it, and a
    /// throw from it leaves nothing behind.
    /// </param>
    public static Artifact Write(
        CorpusDbContext context,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        UtcTimestamp now,
        Action<Stream> contents)
    {
        using var staged = StagedArtifact.Stage(context, meetingId, kind, relativePath, contents);
        return staged.Commit(now);
    }

    /// <summary>Writes a text artifact — a transcript, a manifest — as UTF-8 with no BOM.</summary>
    /// <remarks>
    /// No BOM because these are read by things that are not this application: a person, a diff, an
    /// MCP server. UTF-8 is the encoding, and announcing it inside the file only breaks the readers
    /// that assume it already.
    /// </remarks>
    public static Artifact WriteText(
        CorpusDbContext context,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        UtcTimestamp now,
        string text) =>
        Write(context, meetingId, kind, relativePath, now, Utf8(text));

    /// <summary>
    /// Writes several of one meeting's text artifacts as one act: all of them replace what was
    /// there, or none of them does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every file is written whole beside its destination, flushed, hashed and read back before any
    /// of them is put in place, so everything a render can get wrong — the disk filling on the
    /// second file, a read-back that disagrees, the content itself throwing halfway — arrives while
    /// the corpus still holds the generation it had. What can still refuse at the replace is asked
    /// by <see cref="StagedArtifact.CommitAll"/>, which empties every destination first. That is
    /// the whole of what a caller with two derived files was missing, and it is why this exists
    /// rather than a loop over <see cref="WriteText"/>.
    /// </para>
    /// <para>
    /// They come back in the order they were given, and the rows land in one save.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Artifact> WriteAllText(
        CorpusDbContext context,
        Guid meetingId,
        UtcTimestamp now,
        params (ArtifactKind Kind, string RelativePath, string Text)[] files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var staged = new List<StagedArtifact>(files.Length);
        try
        {
            foreach (var (kind, relativePath, text) in files)
            {
                staged.Add(StagedArtifact.Stage(context, meetingId, kind, relativePath, Utf8(text)));
            }

            return StagedArtifact.CommitAll(now, [.. staged]);
        }
        finally
        {
            // Whatever was staged and not put in place. After a commit that finished this is every
            // one of them holding nothing, and disposing is what throws away the temporaries of a
            // set that stopped halfway.
            foreach (var pending in staged)
            {
                pending.Dispose();
            }
        }
    }

    private static Action<Stream> Utf8(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(text);
        };
    }
}

/// <summary>
/// An artifact written and checked, waiting to be put in place. Steps one to four have happened;
/// <see cref="Commit"/> is five and six.
/// </summary>
/// <remarks>
/// The two halves are separate because the seam is real: the audio, the response and the
/// derivatives of one meeting can all be staged and then land under a single transaction, and
/// until that transaction commits nothing in the corpus has moved. It also means every state a
/// crash can leave is a state this type can be stopped in, which is how the sequence is tested
/// without killing a process and hoping the timing lands where it was aimed.
/// <para>
/// The corpus arrives at the staging and not at the commit, even though only the commit writes a
/// row: the file goes into the folder that corpus is, so taking it later would be the two halves
/// arriving separately again — a file staged beside one corpus and a row recorded in another.
/// </para>
/// <para>
/// The kind arrives at the staging too, for a narrower reason: <see cref="CommitAll"/> asks what
/// may still refuse before it moves anything, and half of that question — may this path be
/// replaced at all — is the kind's to answer.
/// </para>
/// </remarks>
public sealed class StagedArtifact : IDisposable
{
    private readonly CorpusDbContext _corpus;
    private readonly Guid _meetingId;
    private FileInfo? _temporary;

    private StagedArtifact(
        CorpusDbContext corpus,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        FileInfo temporary,
        long byteSize,
        string sha256)
    {
        _corpus = corpus;
        _meetingId = meetingId;
        _temporary = temporary;
        Kind = kind;
        RelativePath = relativePath;
        ByteSize = byteSize;
        Sha256 = sha256;
    }

    /// <summary>What this is, which decides whether the corpus may write over its path.</summary>
    public ArtifactKind Kind { get; }

    /// <summary>Where this will be, once it is committed.</summary>
    public string RelativePath { get; }

    public long ByteSize { get; }

    /// <summary>Lowercase hex, agreed on by the write and by reading the file back.</summary>
    public string Sha256 { get; }

    /// <summary>Whether the temporary is still on disk, which it is until it is put in place.</summary>
    public bool IsPending => _temporary is not null;

    /// <summary>
    /// Steps one to four: write a temporary beside the destination, empty the buffers, measure it,
    /// and read it back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside the destination rather than in the system temp folder, because a rename is only
    /// atomic within a volume and the corpus is wherever the user put it. It also means a machine
    /// that dies mid-write leaves the evidence in the folder somebody would look in.
    /// </para>
    /// <para>
    /// The content is hashed twice on purpose. Once through the stream the caller writes into,
    /// which is the hash of what was meant, and once by reading the closed file off the disk. A
    /// single pass over the file would agree with itself whatever the disk did with it, and would
    /// record a hash for a corrupted artifact rather than refusing to.
    /// </para>
    /// </remarks>
    public static StagedArtifact Stage(
        CorpusDbContext corpus,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        Action<Stream> contents)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(contents);
        CorpusFiles.EnsureBelongsTo(meetingId, relativePath);

        var destination = CorpusFiles.Locate(corpus.Root, relativePath);
        destination.Directory!.Create();

        var temporary = new FileInfo(
            $"{destination.FullName}.{Guid.NewGuid():n}{CorpusFiles.UnfinishedSuffix}");

        string intended;
        try
        {
            // The handle is closed before anything reads the file back, and the reading is the
            // point: a check against a stream this method still holds open would be a check
            // against its own buffers.
            using var stream = new FileStream(
                temporary.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using (var hashing = new HashingStream(stream))
            {
                contents(hashing);
                hashing.Flush();
                intended = hashing.Hex();
            }

            // Step two, and the reason the whole sequence is worth anything: without this the
            // bytes are in a cache the operating system is free to lose, and every check below
            // would be checking that cache rather than the disk.
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            Delete(temporary);
            throw;
        }

        try
        {
            temporary.Refresh();
            var written = CorpusFiles.Sha256Of(temporary);
            if (!string.Equals(written, intended, StringComparison.Ordinal))
            {
                throw new ArtifactWriteException(
                    $"'{relativePath}' read back as {written} after being written as {intended}. "
                    + "The disk did not keep what it was given, and nothing was put in place.");
            }

            return new StagedArtifact(
                corpus, meetingId, kind, relativePath, temporary, temporary.Length, written);
        }
        catch
        {
            Delete(temporary);
            throw;
        }
    }

    /// <summary>Steps five and six for one artifact: replace the destination, then record it.</summary>
    public Artifact Commit(UtcTimestamp now) => CommitAll(now, this)[0];

    /// <summary>
    /// Steps five and six for a set: every question the corpus can answer asked while nothing has
    /// moved, then every destination vacated, then the files put in place, then the rows in one
    /// save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An artifact the corpus cannot produce again is written with the replace refused, so the
    /// filesystem itself is what stops a paid response from being overwritten — not a check above
    /// it that a second writer could slip past between the looking and the moving. Everything the
    /// corpus can produce again replaces whatever was there, which is what re-rendering is, and
    /// what lets a recovery card be corrected rather than pinned to whatever it first said.
    /// </para>
    /// <para>
    /// Two files cannot be replaced at once, so a set that has to be all or nothing makes the
    /// replaces themselves unable to fail. <see cref="Vacate"/> is that: each destination is
    /// renamed out of the way first, which is the same operation the replace performs on it and so
    /// is refused in exactly the cases the replace would be — a program holding the file, an access
    /// rule that reaches deletion. A vacate that is refused puts back the ones already done, into
    /// names it emptied moments earlier, and nothing has been replaced. What follows lands on paths
    /// that are not there, which is the one move that cannot be refused for any of those reasons.
    /// </para>
    /// <para>
    /// Asking by doing rather than by looking, and that is the whole of why it is not the check the
    /// first paragraph refuses. A look is a different question from the move: opening the
    /// destination for writing refuses a file the user may delete and not write, which the replace
    /// goes on to do happily — measured — so a rebuild over a corpus restored under somebody
    /// else's access rules would refuse every meeting in it and blame the disk.
    /// </para>
    /// <para>
    /// The price is paid by a set and never by a single write, which is why only a set vacates. A
    /// replace is one atomic step and a vacate-then-move is two, so a machine dying between them
    /// leaves a destination that is not there at all with its row still naming it. One file has
    /// nothing to gain from that trade — there is no second file for it to agree with — so it keeps
    /// the atomic replace it has always had, and a meeting's two derived files give it up to gain
    /// being one generation. Only a derivative or a card is ever in that window, because a source
    /// is never vacated, and both of those the corpus produces again.
    /// </para>
    /// <para>
    /// Emptying first also replaces a derivative a straight replace could not: a read-only file
    /// refuses <c>File.Move</c> and renames without complaint. That is the right way round for
    /// something the corpus owns and can produce again — a rebuild over a folder restored off a
    /// medium that set the bit is the case — and it reaches no source, which is never vacated and
    /// so is still refused by the move.
    /// </para>
    /// <para>
    /// What is left is a machine that dies inside the run of moves. That leaves the first file
    /// replaced under a row still describing the one before it, which is findable by
    /// <c>check --verify-contents</c>, the one thing that hashes every recorded file, and by
    /// nothing the application runs on its own. It is still the better half of the trade: the mixed
    /// generation it replaces was findable by nothing at all, because there every row agreed with
    /// the file it named.
    /// </para>
    /// <para>
    /// One save for the set, so the rows arrive together in whatever unit of work the caller has,
    /// and a caller with none gets the one <c>SaveChanges</c> opens.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<Artifact> CommitAll(UtcTimestamp now, params StagedArtifact[] staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        if (staged.Length is 0)
        {
            return [];
        }

        RefuseTwoOfOnePath(staged);

        var recorded = new Artifact?[staged.Length];
        for (var i = 0; i < staged.Length; i++)
        {
            recorded[i] = staged[i].Refusals();
        }

        var superseded = staged.Length > 1 ? VacateAll(staged) : [];

        foreach (var one in staged)
        {
            one.Move();
        }

        // Every file of the set is in place, so whatever was standing there is superseded whether
        // or not the rows below are accepted — which is what an ordinary replace does to it too.
        // Left behind by a crash these are unfinished writes, the one thing on disk the reconciler
        // removes on its own.
        foreach (var old in superseded)
        {
            Delete(old);
        }

        var artifacts = new Artifact[staged.Length];
        for (var i = 0; i < staged.Length; i++)
        {
            artifacts[i] = staged[i].Record(recorded[i], now);
        }

        staged[0]._corpus.SaveChanges();
        return artifacts;
    }

    /// <summary>Refuses a set naming one path twice, which is a caller's slip and not a corpus state.</summary>
    /// <remarks>
    /// Both writes would go to the same destination, the second over the first, and both would
    /// record a row — two rows for one file, which the unique index on the path refuses at the
    /// save, after both files have moved. That is the one direction this whole type exists to
    /// avoid, reached by two lines in a caller, so it is asked before anything happens rather than
    /// found afterwards.
    /// </remarks>
    private static void RefuseTwoOfOnePath(StagedArtifact[] staged)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var one in staged)
        {
            if (!named.Add(one.RelativePath))
            {
                throw new ArtifactWriteException(
                    $"'{one.RelativePath}' is named twice in one set. A path holds one artifact, so "
                    + "the second write would replace the first and leave the corpus with two rows "
                    + "for one file.");
            }
        }
    }

    /// <summary>Empties every destination of the set, or leaves every one of them as it was.</summary>
    /// <remarks>
    /// The undo is what makes this safe to do before anything is replaced, and it is not the undo
    /// the corpus refuses elsewhere: no row has been written, and every name it moves a file back
    /// into is one this emptied moments ago rather than one somebody else may have taken. A
    /// put-back that fails anyway says nothing worth carrying — <see cref="PutBack"/> gives the
    /// argument — and the refusal that caused it is the sentence the caller needs.
    /// </remarks>
    private static IReadOnlyList<FileInfo> VacateAll(StagedArtifact[] staged)
    {
        var emptied = new List<(FileInfo Aside, FileInfo Destination)>(staged.Length);

        try
        {
            foreach (var one in staged)
            {
                if (one.Vacate() is { } moved)
                {
                    emptied.Add(moved);
                }
            }
        }
        catch
        {
            for (var i = emptied.Count - 1; i >= 0; i--)
            {
                PutBack(emptied[i]);
            }

            throw;
        }

        return [.. emptied.Select(moved => moved.Aside)];
    }

    /// <summary>
    /// Renames this artifact's destination out of the way and answers with where it went. Null when
    /// there was nothing there, and when the kind is one the corpus never replaces.
    /// </summary>
    /// <remarks>
    /// A source is never moved aside, not even for a moment. The move that would follow is refused
    /// for it anyway — <see cref="Refusals"/> has already asked whether it is there — so vacating
    /// one would relocate the only copy of something that was paid for in order to enable a write
    /// that is not going to happen. Everything this does move is a derivative the corpus can
    /// produce again, which is what <c>MayBeReplaced</c> means.
    /// </remarks>
    private (FileInfo Aside, FileInfo Destination)? Vacate()
    {
        if (!Kind.MayBeReplaced())
        {
            return null;
        }

        var destination = CorpusFiles.Locate(_corpus.Root, RelativePath);
        if (!StillThere(destination))
        {
            // Nothing to empty. A directory standing here is not a file, and it is left to the
            // move, which is where a replace has always met one.
            return null;
        }

        var aside = new FileInfo(
            $"{destination.FullName}.{Guid.NewGuid():n}{CorpusFiles.UnfinishedSuffix}");
        File.Move(destination.FullName, aside.FullName);
        return (aside, destination);
    }

    /// <summary>Puts a vacated file back, without ever becoming the reason the caller hears about.</summary>
    /// <remarks>
    /// The name it goes back into was emptied by this same run of vacates, so the ways this can
    /// fail are the ways the machine is already failing, and saying so would cost the sentence that
    /// matters — the refusal on its way out is what tells somebody which file could not be taken
    /// and why. What it leaves when it does fail is the old file under an unfinished write's name
    /// and a row naming a file that is not there, which the reconciler reports as missing: loud,
    /// and a derivative, which a rebuild produces again.
    /// </remarks>
    private static void PutBack((FileInfo Aside, FileInfo Destination) moved)
    {
        try
        {
            File.Move(moved.Aside.FullName, moved.Destination.FullName);
        }
        catch (Exception unrecoverable) when (unrecoverable is IOException or UnauthorizedAccessException)
        {
            // Deliberately nothing. The refusal on its way out is the one worth having.
        }
    }

    /// <summary>
    /// Everything that can still refuse this write on what the corpus knows, and the row it already
    /// holds for this path if it holds one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row is looked up before the move rather than after, because it is the only thing that
    /// can refuse on what the corpus knows. Replaceability is decided by the kind the caller
    /// passed, and the path is passed by the same caller: on their own, the two say nothing about
    /// each other, so a manifest addressed at 'deepgram.json' would overwrite a paid response and
    /// then relabel its row. What the corpus already knows about this path is what closes that, and
    /// it is worth nothing after <c>File.Move</c> has run.
    /// </para>
    /// <para>
    /// A source already on disk is refused here as well as at the move, and the two are not
    /// redundant. The move is still the guarantee — a second writer arriving between the two cannot
    /// slip a rewrite past <c>overwrite: false</c> — and this is what keeps a set's order, because
    /// a refusal found at the move would already have vacated the destinations ahead of it. The
    /// sentence is the same one either way, so nothing a single write does changes.
    /// </para>
    /// </remarks>
    private Artifact? Refusals()
    {
        if (_temporary is null)
        {
            throw new InvalidOperationException($"'{RelativePath}' has already been put in place.");
        }

        var artifact = _corpus.Artifacts.FirstOrDefault(
            row => row.MeetingId == _meetingId && row.RelativePath == RelativePath);

        if (artifact is not null && artifact.Kind != Kind)
        {
            throw new ArtifactWriteException(
                $"'{RelativePath}' is this meeting's {artifact.Kind} and this write calls it a "
                + $"{Kind}. A path holds one kind for as long as the corpus does, so this is a "
                + "caller naming the wrong file rather than an artifact changing what it is.");
        }

        if (!Kind.MayBeReplaced() && StillThere(CorpusFiles.Locate(_corpus.Root, RelativePath)))
        {
            throw AlreadyThere();
        }

        return artifact;
    }

    /// <summary>Step five: the replace, which is the only irreversible thing here.</summary>
    private void Move()
    {
        var temporary = _temporary!;
        var destination = CorpusFiles.Locate(_corpus.Root, RelativePath);
        var replaceable = Kind.MayBeReplaced();

        try
        {
            File.Move(temporary.FullName, destination.FullName, overwrite: replaceable);
        }
        catch (IOException) when (!replaceable && StillThere(destination))
        {
            throw AlreadyThere();
        }

        _temporary = null;
    }

    /// <summary>Step six: the row, added or brought up to the file that is now there.</summary>
    private Artifact Record(Artifact? artifact, UtcTimestamp now)
    {
        if (artifact is null)
        {
            artifact = new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = _meetingId,
                Kind = Kind,
                Origin = Kind.OriginOf(),
                RelativePath = RelativePath,
                ByteSize = ByteSize,
                Sha256 = Sha256,
                ConfirmedAt = now,
            };
            _corpus.Artifacts.Add(artifact);
        }
        else
        {
            // The kind and the origin are not reassigned: the row already carries this kind, which
            // is what the refusal in Refusals established, and nothing else may turn one into
            // another.
            artifact.ByteSize = ByteSize;
            artifact.Sha256 = Sha256;

            // The row says when the file that is there now was confirmed, not when the first
            // file to carry this name was. A rerender produces a different artifact.
            artifact.ConfirmedAt = now;
        }

        return artifact;
    }

    private ArtifactWriteException AlreadyThere() =>
        new($"'{RelativePath}' is already there and a {Kind} is never rewritten. Writing it "
            + "again would destroy the only copy of something that cannot be obtained a "
            + "second time.");

    /// <summary>
    /// Throws away a write that was never put in place. Doing nothing would be safe too — an
    /// unfinished write is never mistaken for an artifact — but leaving one behind on every
    /// handled failure turns the reconciler's report into a list nobody reads.
    /// </summary>
    public void Dispose()
    {
        if (_temporary is { } temporary)
        {
            _temporary = null;
            Delete(temporary);
        }
    }

    /// <summary>Asks the disk again. A <see cref="FileInfo"/> answers from when it was made.</summary>
    private static bool StillThere(FileInfo file)
    {
        file.Refresh();
        return file.Exists;
    }

    private static void Delete(FileInfo file)
    {
        try
        {
            file.Refresh();
            if (file.Exists)
            {
                file.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The write already failed, and failing to tidy up after it is not the failure worth
            // reporting. What is left is an unfinished write, which is the one thing on disk the
            // reconciler is allowed to remove on its own.
        }
    }

    /// <summary>
    /// The bytes on their way to the file, hashed as they pass. What was meant to be written,
    /// which is the only thing worth comparing the file against.
    /// </summary>
    private sealed class HashingStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public string Hex() => Convert.ToHexStringLower(_digest.GetCurrentHash());

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            _digest.AppendData(buffer);
        }

        public override void WriteByte(byte value) => Write([value]);

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _digest.Dispose();
            }

            // The file stream is the caller's and is closed by the caller: closing it here would
            // put the flush to disk before the hash is asked for.
            base.Dispose(disposing);
        }
    }
}
