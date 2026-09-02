using System.Security.Cryptography;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>
/// Where a corpus keeps its files, and the naming rules everything else depends on.
/// </summary>
/// <remarks>
/// <para>
/// The layout is in docs/corpus.md; this is the same thing in code, so a caller does not compose
/// the path a row will be found by and a scanner does not guess it back. The stored form is what
/// <c>artifacts.relative_path</c> holds and what the unique index on it compares, which is the
/// reason separators are fixed here rather than left to whichever platform wrote the row.
/// </para>
/// <para>
/// The corpus never lives in the MSIX package data folder, so the root is always passed in. This
/// type never reads it from configuration and never has a default: a wrong root is a corpus in a
/// folder uninstalling wipes.
/// </para>
/// </remarks>
public static class CorpusFiles
{
    /// <summary>The folder a meeting's own artifacts live under.</summary>
    public const string Meetings = "meetings";

    /// <summary>
    /// The folder the blocks of a recording in progress live under. They are a source for as long
    /// as they are the only recoverable copy of the audio.
    /// </summary>
    public const string Spool = "spool";

    /// <summary>
    /// What a write that has not been confirmed yet is called. A destination never ends in it, so
    /// the reconciler can tell an unfinished write from an artifact by the name alone — and the
    /// suffix is the whole of that claim, which is why nothing else may end a file with it.
    /// </summary>
    /// <remarks>
    /// It says the bytes under it were never an artifact, which is what lets a sweep remove one
    /// without asking anybody. So it belongs to bytes on their way in and to nothing else: the old
    /// copy of a file being replaced carries <see cref="SupersededSuffix"/> instead. Both wore this
    /// one until 2026-09-02, and what that cost was a sweep taking the last copy of a derived file
    /// out from under a replace — which is the argument against ever folding them back into one.
    /// <c>MeetingAudio</c> spells this same string a second time, for the reason its own comment
    /// gives.
    /// </remarks>
    public const string UnfinishedSuffix = ".partial";

    /// <summary>
    /// What the old copy of a derived file is called while its destination stands empty, so a set
    /// of them can be replaced together.
    /// </summary>
    /// <remarks>
    /// The opposite claim to <see cref="UnfinishedSuffix"/>: these bytes <i>were</i> an artifact,
    /// and for as long as one of these is on disk it is the last copy of one. Nothing removes it on
    /// its own — the reconciler reports it and a rebuild produces the file again — because a sweep
    /// that took one would be taking exactly the copy a refused replace was about to put back.
    /// </remarks>
    public const string SupersededSuffix = ".superseded";

    /// <summary>
    /// How two stored paths are told apart in memory: two spellings that differ only in case name
    /// one file, because the corpus is a folder on a Windows filesystem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comparing the stored strings is enough, rather than resolving each to a full path first,
    /// because <see cref="EnsureBelongsTo"/> has already refused everything else two spellings of
    /// one destination could differ by: a backslash, a <c>.</c> or <c>..</c>, a rooted path. What
    /// is left between two stored paths pointing at one file is case, and nothing else.
    /// </para>
    /// <para>
    /// <b>In memory</b> is the whole of what it settles. The unique index on
    /// <c>artifacts.relative_path</c> is SQLite's default binary collation, so a lookup that asks
    /// the database — the one a write makes for the row already at its path — still gets the exact
    /// answer, and two spellings across two writes would still make two rows over one file. What
    /// closes that is a collation on the column, which is a migration, and nothing has needed it:
    /// every stored path is composed by <see cref="PathFor"/> or <see cref="SpoolPathFor"/> out of
    /// a meeting id and a constant. docs/corpus.md says the same thing where a person looks.
    /// </para>
    /// </remarks>
    public static StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>The stored path of a file of this meeting, always with forward slashes.</summary>
    public static string PathFor(Guid meetingId, string name) =>
        $"{Meetings}/{meetingId}/{Named(name)}";

    /// <summary>The stored path of a spool block of this meeting.</summary>
    public static string SpoolPathFor(Guid meetingId, string name) =>
        $"{Spool}/{meetingId}/{Named(name)}";

    /// <summary>
    /// The folder this meeting's recording is written into while it is being recorded.
    /// </summary>
    /// <remarks>
    /// A folder rather than a file, because what goes in it is decided by the audio engine — one
    /// spool per channel and the card beside them, none of whose names this project knows. It is
    /// here anyway for the same reason every other path is: the reconciler scans <c>spool/</c> and
    /// a recording written a step to the side of where it scans is audio nothing will look for.
    /// </remarks>
    public static DirectoryInfo SpoolFolderFor(DirectoryInfo root, Guid meetingId)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new DirectoryInfo(Path.Combine(root.FullName, Spool, meetingId.ToString()));
    }

    /// <summary>
    /// The folder every recording in progress is spooled under, whether or not any is.
    /// </summary>
    /// <remarks>
    /// The one folder a start after a crash has to look in. It is here rather than composed by
    /// whoever looks for the same reason <see cref="SpoolFolderFor"/> is: a scan a step to the side
    /// of where recordings are written is a scan that finds none and says so.
    /// </remarks>
    public static DirectoryInfo SpoolRootIn(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        return new DirectoryInfo(Path.Combine(root.FullName, Spool));
    }

    /// <summary>Where the stored path points on this machine.</summary>
    public static FileInfo Locate(DirectoryInfo root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return new FileInfo(Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>The stored path of a file found under <paramref name="root"/> by a scan.</summary>
    public static string RelativePathOf(DirectoryInfo root, FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(file);

        return Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
    }

    /// <summary>Whether this is the name of a write that never finished.</summary>
    /// <remarks>
    /// Case-insensitively, for the reason <see cref="PathComparer"/> gives. Told apart exactly,
    /// these would be a second answer — in this file — to the question that one settles, and a
    /// <c>.PARTIAL</c> off a medium that mangled the case would be reported as an artifact nothing
    /// recorded rather than as the dead write it is.
    /// </remarks>
    public static bool IsUnfinished(string relativePath) =>
        relativePath is not null
        && relativePath.EndsWith(UnfinishedSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this is the name of a copy a replace moved out of the way.</summary>
    public static bool IsSuperseded(string relativePath) =>
        relativePath is not null
        && relativePath.EndsWith(SupersededSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A name for a write on its way to <paramref name="destination"/>, beside it on the same
    /// volume and unique to this write.
    /// </summary>
    /// <remarks>
    /// Beside it because a rename is only atomic within a volume and the corpus is wherever the
    /// user put it; unique because two writes to one destination must not meet in the temporary
    /// they were each about to move from.
    /// </remarks>
    internal static FileInfo UnfinishedBeside(FileInfo destination) =>
        Beside(destination, UnfinishedSuffix);

    /// <summary>
    /// A name for what is standing at <paramref name="destination"/> now, while the file that
    /// replaces it is put in place.
    /// </summary>
    internal static FileInfo SupersededBeside(FileInfo destination) =>
        Beside(destination, SupersededSuffix);

    /// <summary>
    /// Throws unless the path is one this meeting's files are allowed to be stored at.
    /// </summary>
    /// <remarks>
    /// Not tidiness. The stored path is what a backup walks, what a rebuild deletes and what the
    /// reconciler scans for, and all three start from the meeting's folder — so a file written
    /// anywhere else has a row nothing will ever look at, and is exactly the artifact that goes
    /// missing quietly. A separator or a <c>..</c> is refused for the same reason one step down:
    /// two spellings of one file are two rows the unique index cannot see are the same.
    /// </remarks>
    public static void EnsureBelongsTo(Guid meetingId, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (relativePath.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' is a Windows path. A stored path separates with '/' on every machine.",
                nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath)
            || relativePath.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException(
                $"'{relativePath}' is not a path inside the corpus.", nameof(relativePath));
        }

        if (IsUnfinished(relativePath))
        {
            throw new ArgumentException(
                $"'{relativePath}' ends in '{UnfinishedSuffix}', which names a write that never finished.",
                nameof(relativePath));
        }

        if (IsSuperseded(relativePath))
        {
            throw new ArgumentException(
                $"'{relativePath}' ends in '{SupersededSuffix}', which names the copy a replace "
                + "moved out of the way.",
                nameof(relativePath));
        }

        var mine = $"{Meetings}/{meetingId}/";
        var spooled = $"{Spool}/{meetingId}/";
        if (!relativePath.StartsWith(mine, StringComparison.Ordinal)
            && !relativePath.StartsWith(spooled, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{relativePath}' is not under '{mine}' or '{spooled}', so nothing that walks "
                + $"meeting {meetingId} would find it.",
                nameof(relativePath));
        }
    }

    /// <summary>The lowercase hex SHA-256 of a file, read back from the disk it is on.</summary>
    public static string Sha256Of(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var stream = file.OpenRead();
        return Sha256Of(stream);
    }

    /// <summary>
    /// The same hash, of a stream somebody else is holding open. What a caller reads from one
    /// handle uses, so that the bytes it hashed and the bytes it went on to store are the same
    /// bytes rather than two reads of a file anything could have replaced in between.
    /// </summary>
    public static string Sha256Of(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static string Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    private static FileInfo Beside(FileInfo destination, string suffix)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return new FileInfo($"{destination.FullName}.{Guid.NewGuid():n}{suffix}");
    }
}
