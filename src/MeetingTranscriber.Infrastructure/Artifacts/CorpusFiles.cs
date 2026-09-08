using System.Security.Cryptography;

using MeetingTranscriber.Domain.Artifacts;

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
    /// The string itself is <see cref="RecordingFiles.UnfinishedSuffix"/>, because the audio engine
    /// writes one of these into a folder this sweep walks and the two may not drift; what the
    /// suffix claims, and that nothing else may end a file with it, is this type's and stays here.
    /// </remarks>
    public const string UnfinishedSuffix = RecordingFiles.UnfinishedSuffix;

    /// <summary>
    /// What the old copy of a derived file is called while its destination stands empty, so a set
    /// of them can be replaced together.
    /// </summary>
    /// <remarks>
    /// The opposite claim to <see cref="UnfinishedSuffix"/>: these bytes <i>were</i> an artifact,
    /// and while the file they were moved out of the way of is not there this is the last copy of
    /// one. So nothing may be concluded from the suffix alone — a sweep asks whether the
    /// destination is back, which is <see cref="DestinationOfSuperseded"/>, and takes only the
    /// copies where it is. Taking one where it is not would be taking exactly the copy a refused
    /// replace is about to put back.
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

    /// <summary>Whether this stored path is inside a recording a discard moved aside.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than beside <see cref="RecordingFiles.BeingRemovedPrefix"/> because the prefix is
    /// a name and this is the shape of the path it sits in, which is the stored-path form this type
    /// owns. A removal renames the recording's folder into <c>.removing-&lt;id&gt;</c> beside where
    /// it was and then moves the folder inside it, so the shortest path this holds of is
    /// <c>spool/.removing-&lt;id&gt;/&lt;id&gt;/…</c> — three segments, which is why two is not
    /// enough: a <em>file</em> called <c>.removing-something</c> dropped in the spool root is
    /// somebody else's file and gets the answer somebody else's file gets.
    /// </para>
    /// <para>
    /// Two comparisons and two answers. <see cref="StringComparison.Ordinal"/> on the
    /// <see cref="Spool"/> segment, so that it agrees with <see cref="NameInASpoolFolder"/> rather
    /// than half-agreeing: this is a spelling read back off the disk and not one this type composed,
    /// so a corpus whose spool folder is cased differently turns both of them off together and
    /// reports every file under it as one with no row, which is the safe answer, instead of
    /// silencing a discarded recording while classifying the rest.
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> on the prefix, which is a folder name on a
    /// Windows filesystem where two spellings are one folder, for the reason
    /// <see cref="PathComparer"/> gives.
    /// </para>
    /// <para>
    /// No artifact row can be under one: <see cref="EnsureBelongsTo"/> requires
    /// <c>spool/{meetingId}/</c>, and <c>.removing-{id}</c> is not <c>{id}</c>.
    /// </para>
    /// </remarks>
    internal static bool IsBeingRemoved(string relativePath)
    {
        var parts = Segments(relativePath);
        return parts.Length >= 3
            && string.Equals(parts[0], Spool, StringComparison.Ordinal)
            && parts[1].StartsWith(RecordingFiles.BeingRemovedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The file's own name when this stored path is a file sitting directly in one recording's
    /// spool folder, and null when it is anything else.
    /// </summary>
    /// <remarks>
    /// It is what makes <see cref="RecordingFiles.WhatIsInASpoolFolder"/>'s precondition true at the
    /// call site: that question is only answerable about a file beside a recording's blocks, and the
    /// scan walks every folder under <see cref="Spool"/> to any depth. Exactly the shape
    /// <see cref="EnsureBelongsTo"/> allows a row to be stored at, so what a walk classifies and what
    /// a write may compose are one rule rather than two that agree today. Anything deeper — a folder
    /// somebody restored inside a recording's, a copy made before a reinstall — is a file with no
    /// row and gets told that, rather than being told it came out of blocks that are not there.
    /// </remarks>
    internal static string? NameInASpoolFolder(string relativePath)
    {
        var parts = Segments(relativePath);
        return parts.Length == 3 && string.Equals(parts[0], Spool, StringComparison.Ordinal)
            ? parts[2]
            : null;
    }

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
    /// The file a superseded copy was moved out of the way of, and null when the name does not
    /// carry one.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="SupersededBeside"/>, and it holds only over names that one wrote:
    /// a destination, a full stop, the token that makes the copy unique, and the suffix. So the
    /// token is read back as the <see cref="Guid"/> it was written from rather than as whatever
    /// stands between the last two full stops, and a name no replace of this corpus wrote answers
    /// null. That answer is load-bearing on both sides: nothing is deleted on the strength of a
    /// suffix somebody else's file happens to end in, and nothing is reported as the copy of a
    /// derived file that never existed.
    /// </remarks>
    internal static FileInfo? DestinationOfSuperseded(FileInfo aside)
    {
        ArgumentNullException.ThrowIfNull(aside);

        if (aside.Directory is not { } folder || !IsSuperseded(aside.Name))
        {
            return null;
        }

        var named = aside.Name[..^SupersededSuffix.Length];
        var token = named.LastIndexOf('.');

        return token > 0 && Guid.TryParseExact(named[(token + 1)..], "N", out _)
            ? new FileInfo(Path.Combine(folder.FullName, named[..token]))
            : null;
    }

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

    /// <summary>A stored path in the parts the layout is made of.</summary>
    private static string[] Segments(string relativePath) => relativePath.Split('/');

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
