namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// Which executable this user chose as their own Claude Code, when they chose one at all.
/// </summary>
/// <remarks>
/// Kept the way <see cref="CorpusLocation"/> keeps where the corpus is: one file, beside it under
/// <see cref="CorpusLocation.ApplicationFolderName"/>, written to a file beside itself and moved
/// into place, so a machine that stops half way through a write leaves the old choice whole rather
/// than an empty one.
/// <para>
/// Unlike a corpus, nobody has to choose one at all — a search of <c>PATH</c> is what a machine
/// with nothing chosen falls back to, and that search is <c>ClaudeCodeExecutable.Find</c>'s to run,
/// not this class's: this class answers only what was chosen, or nothing, the same division
/// <c>ClaudeCodeExecutable.Find(FileInfo? chosen, string? path)</c> already draws between the two.
/// </para>
/// </remarks>
public sealed class ClaudeCodeLocation
{
    /// <summary>The file holding the executable somebody chose, beside <see cref="CorpusLocation.SettingName"/>.</summary>
    public const string SettingName = "claude-code-path";

    /// <summary>
    /// Where the setting is written before it is put in place — see
    /// <see cref="CorpusLocation.Choose"/>'s own remark for why.
    /// </summary>
    private const string SettingBeingWritten = SettingName + ".new";

    public ClaudeCodeLocation(FileInfo setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        Setting = setting;
    }

    /// <summary>The file that says which executable was chosen, whether or not it is there.</summary>
    public FileInfo Setting { get; }

    /// <summary>The one kept beside where this user's corpus location is kept.</summary>
    public static ClaudeCodeLocation OfThisUser() => Under(ApplicationHome.ProfileFolder());

    /// <summary>The one kept in <paramref name="applicationFolder"/>.</summary>
    public static ClaudeCodeLocation Under(DirectoryInfo applicationFolder)
    {
        ArgumentNullException.ThrowIfNull(applicationFolder);
        return new(new FileInfo(Path.Combine(applicationFolder.FullName, SettingName)));
    }

    /// <summary>
    /// The file somebody chose, or <c>null</c> when nobody has — which is also what a setting file
    /// that cannot be read, or that names something other than a full path, answers. Nothing here
    /// asks whether the file still exists: that is <c>ClaudeCodeExecutable.Find</c>'s question, over
    /// whatever this answers.
    /// </summary>
    public FileInfo? Chosen()
    {
        string written;
        try
        {
            written = File.ReadAllText(Setting.FullName).Trim();
        }
        catch (Exception missing) when (
            missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception unreadable) when (
            unreadable is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        // Fully qualified rather than merely rooted, for the reason CorpusLocation's own read
        // gives: a path resolved against a working directory this process did not choose is a
        // different file depending on where the application happened to be started from.
        return Path.IsPathFullyQualified(written) ? new FileInfo(written) : null;
    }

    /// <summary>Records that this is the executable Claude Code runs from, from now on.</summary>
    public void Choose(FileInfo executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        Setting.Directory?.Create();

        // Written beside itself and moved into place, so the pointer is the old one or the new one
        // and never a file that was being written when the machine stopped.
        var staging = Path.Combine(Setting.Directory?.FullName ?? ".", SettingBeingWritten);
        File.WriteAllText(staging, executable.FullName);
        File.Move(staging, Setting.FullName, overwrite: true);
    }
}
