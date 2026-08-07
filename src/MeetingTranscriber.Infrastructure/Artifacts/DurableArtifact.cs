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
/// </remarks>
public static class DurableArtifact
{
    /// <summary>Writes an artifact and records it. What almost every caller wants.</summary>
    /// <param name="context">The corpus. Its transaction, if it has one, is the one the row joins.</param>
    /// <param name="root">The corpus root. Never the package data folder.</param>
    /// <param name="contents">
    /// Writes the artifact. Called once, on a stream that is hashing what goes through it, and a
    /// throw from it leaves nothing behind.
    /// </param>
    public static Artifact Write(
        CorpusDbContext context,
        DirectoryInfo root,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        UtcTimestamp now,
        Action<Stream> contents)
    {
        using var staged = StagedArtifact.Stage(root, meetingId, relativePath, contents);
        return staged.Commit(context, kind, now);
    }

    /// <summary>Writes a text artifact — a transcript, a manifest — as UTF-8 with no BOM.</summary>
    /// <remarks>
    /// No BOM because these are read by things that are not this application: a person, a diff, an
    /// MCP server. UTF-8 is the encoding, and announcing it inside the file only breaks the readers
    /// that assume it already.
    /// </remarks>
    public static Artifact WriteText(
        CorpusDbContext context,
        DirectoryInfo root,
        Guid meetingId,
        ArtifactKind kind,
        string relativePath,
        UtcTimestamp now,
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Write(context, root, meetingId, kind, relativePath, now, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            writer.Write(text);
        });
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
/// </remarks>
public sealed class StagedArtifact : IDisposable
{
    private readonly DirectoryInfo _root;
    private readonly Guid _meetingId;
    private FileInfo? _temporary;

    private StagedArtifact(
        DirectoryInfo root,
        Guid meetingId,
        string relativePath,
        FileInfo temporary,
        long byteSize,
        string sha256)
    {
        _root = root;
        _meetingId = meetingId;
        _temporary = temporary;
        RelativePath = relativePath;
        ByteSize = byteSize;
        Sha256 = sha256;
    }

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
        DirectoryInfo root,
        Guid meetingId,
        string relativePath,
        Action<Stream> contents)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(contents);
        CorpusFiles.EnsureBelongsTo(meetingId, relativePath);

        var destination = CorpusFiles.Locate(root, relativePath);
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

            return new StagedArtifact(root, meetingId, relativePath, temporary, temporary.Length, written);
        }
        catch
        {
            Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Steps five and six: replace the destination, then record the artifact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source is written with the replace refused, so the filesystem itself is what stops a paid
    /// response from being overwritten — not a check above it that a second writer could slip past
    /// between the looking and the moving. A derivative replaces whatever was there, which is what
    /// re-rendering is.
    /// </para>
    /// <para>
    /// <c>SaveChanges</c> is the transaction. A caller committing several staged artifacts
    /// together opens one first and every row joins it, which is what makes the derivatives of one
    /// meeting appear as a set or not at all.
    /// </para>
    /// </remarks>
    public Artifact Commit(CorpusDbContext context, ArtifactKind kind, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(context);
        var temporary = _temporary
            ?? throw new InvalidOperationException($"'{RelativePath}' has already been put in place.");

        var destination = CorpusFiles.Locate(_root, RelativePath);
        var rebuildable = kind.IsRebuildable();

        try
        {
            File.Move(temporary.FullName, destination.FullName, overwrite: rebuildable);
        }
        catch (IOException) when (!rebuildable && StillThere(destination))
        {
            throw new ArtifactWriteException(
                $"'{RelativePath}' is already there and a {kind} is a source, which is never "
                + "rewritten. Writing it again would destroy the only copy of something that "
                + "cannot be obtained a second time.");
        }

        _temporary = null;

        var artifact = context.Artifacts.FirstOrDefault(
            row => row.MeetingId == _meetingId && row.RelativePath == RelativePath);

        if (artifact is null)
        {
            artifact = new Artifact
            {
                Id = Guid.NewGuid(),
                MeetingId = _meetingId,
                Kind = kind,
                Origin = kind.OriginOf(),
                RelativePath = RelativePath,
                ByteSize = ByteSize,
                Sha256 = Sha256,
                CreatedAt = now,
            };
            context.Artifacts.Add(artifact);
        }
        else
        {
            artifact.Kind = kind;
            artifact.Origin = kind.OriginOf();
            artifact.ByteSize = ByteSize;
            artifact.Sha256 = Sha256;

            // The row says when the file that is there now was confirmed, not when the first
            // file to carry this name was. A rerender produces a different artifact.
            artifact.CreatedAt = now;
        }

        context.SaveChanges();
        return artifact;
    }

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
