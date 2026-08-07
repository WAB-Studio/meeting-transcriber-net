using System.Security.Cryptography;

namespace MeetingTranscriber.Infrastructure.Artifacts;

/// <summary>
/// Where a corpus keeps its files, and the two naming rules everything else depends on.
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
    public const string UnfinishedSuffix = ".partial";

    /// <summary>The stored path of a file of this meeting, always with forward slashes.</summary>
    public static string PathFor(Guid meetingId, string name) =>
        $"{Meetings}/{meetingId}/{Named(name)}";

    /// <summary>The stored path of a spool block of this meeting.</summary>
    public static string SpoolPathFor(Guid meetingId, string name) =>
        $"{Spool}/{meetingId}/{Named(name)}";

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
    public static bool IsUnfinished(string relativePath) =>
        relativePath is not null && relativePath.EndsWith(UnfinishedSuffix, StringComparison.Ordinal);

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
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Named(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}
