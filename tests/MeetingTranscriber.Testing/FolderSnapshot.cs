using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Infrastructure.Artifacts;

namespace MeetingTranscriber.Testing;

/// <summary>What a folder holds, for a fact that has to say it is exactly what it was.</summary>
public static class FolderSnapshot
{
    /// <summary>
    /// The folder's own files, each as its name and the hash of its bytes, in a fixed order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder's own and not a walk: a recording being taken away is staged under
    /// <c>.removing-&lt;id&gt;/&lt;id&gt;/</c> inside the very folder these facts snapshot, and a
    /// recursive read would pull that staging tree into a comparison whose whole point is that
    /// nothing moved.
    /// </para>
    /// <para>
    /// Opened the way a backup opens a file rather than the way <c>File.ReadAllBytes</c> does: one
    /// of these files is a mark something is holding for writing, and a read sharing less than that
    /// would be refused by the very holder the comparison is here to survive.
    /// </para>
    /// <para>
    /// The <see cref="Stream"/> overload of <c>CorpusFiles.Sha256Of</c> and never the
    /// <see cref="FileInfo"/> one: that one opens with <see cref="FileShare.Read"/>, which is
    /// refused the moment a mark is held — exactly the case this snapshot exists for.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Of(DirectoryInfo folder) =>
    [
        .. folder.GetFiles()
            .Select(file => $"{file.Name} {Hashed(file)}")
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Whether the mark a save writes is lying in this folder.</summary>
    public static bool BeingSaved(DirectoryInfo folder) =>
        File.Exists(Path.Combine(folder.FullName, RecordingFiles.SavingMark));

    /// <summary>Whether the mark a read writes is lying in this folder.</summary>
    public static bool BeingRead(DirectoryInfo folder) =>
        File.Exists(Path.Combine(folder.FullName, RecordingFiles.ReadingMark));

    private static string Hashed(FileInfo file)
    {
        using var content = file.Open(
            FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return CorpusFiles.Sha256Of(content);
    }
}
