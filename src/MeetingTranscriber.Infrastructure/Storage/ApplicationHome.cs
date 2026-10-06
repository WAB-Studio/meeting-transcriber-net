namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The folder where the corpus pointer, the Claude Code pointer and the default corpus live.
/// </summary>
/// <remarks>
/// <para>
/// Every user's is <c>%USERPROFILE%\MeetingTranscriber</c> and nothing in the product ever asks
/// for another. The one other caller is the UI probe, which hands the application a folder of its
/// own on the launch line so that a walk never has to move, read or write the owner's: the
/// application is started with <c>--home "&lt;folder&gt;"</c> and settles it once, first thing.
/// </para>
/// <para>
/// A launch line and not an environment variable, because shell activation of a packaged
/// application carries arguments and does not carry an environment; and not a pointer kept in the
/// package's data folder, because that would be a second place the shipped product reads its
/// pointer from for ever.
/// </para>
/// </remarks>
public sealed class ApplicationHome
{
    private const string Switch = "--home";

    /// <summary>The file a launch that was told its home writes, saying which home it resolved.</summary>
    public const string ReportName = "resolved-home";

    private ApplicationHome(DirectoryInfo folder, bool asked)
    {
        Folder = folder;
        Asked = asked;
        Corpus = CorpusLocation.Under(folder);
        ClaudeCode = ClaudeCodeLocation.Under(folder);
    }

    /// <summary>Whether the launch line named this home, as against it being this user's by default.</summary>
    public bool Asked { get; }

    /// <summary>The folder everything below is kept in.</summary>
    public DirectoryInfo Folder { get; }

    /// <summary>Where this home's corpus is.</summary>
    public CorpusLocation Corpus { get; }

    /// <summary>Where this home says Claude Code is.</summary>
    public ClaudeCodeLocation ClaudeCode { get; }

    /// <summary><c>%USERPROFILE%\MeetingTranscriber</c>.</summary>
    public static ApplicationHome OfThisUser() => Under(ProfileFolder());

    /// <summary>
    /// This user's application folder, <c>%USERPROFILE%\MeetingTranscriber</c>: the one spelling of
    /// it. Why the profile and never application data is <see cref="CorpusLocation.OfThisUser"/>'s
    /// remark.
    /// </summary>
    public static DirectoryInfo ProfileFolder() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        CorpusLocation.ApplicationFolderName));

    /// <summary>A home in <paramref name="folder"/>.</summary>
    public static ApplicationHome Under(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new ApplicationHome(folder, asked: false);
    }

    /// <summary>
    /// The home a launch line asks for: this user's when it carries no <c>--home</c>, and the
    /// folder after it otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <c>--home</c> is there and nothing usable follows it — not a quoted or bare fully qualified
    /// path. Only the probe writes this line, so a malformed one is loud.
    /// </exception>
    public static ApplicationHome FromLaunch(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return OfThisUser();
        }

        // The switch as a word of its own: `--homeless` is not it.
        var found = System.Text.RegularExpressions.Regex.Match(
            arguments, @"(^|\s)" + Switch + @"(?=\s|$)");
        if (!found.Success)
        {
            return OfThisUser();
        }

        var after = arguments[(found.Index + found.Length)..].TrimStart();
        string path;
        if (after.StartsWith('"'))
        {
            var close = after.IndexOf('"', 1);
            path = close < 0 ? string.Empty : after[1..close];
        }
        else
        {
            var space = after.IndexOf(' ', StringComparison.Ordinal);
            path = space < 0 ? after : after[..space];
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                $"The launch line has {Switch} and no fully qualified folder after it: {arguments}",
                nameof(arguments));
        }

        return new ApplicationHome(new DirectoryInfo(path), asked: true);
    }

    /// <summary>
    /// Says, inside the home the launch line named, that this launch resolved it. Does nothing for
    /// a home nobody named, which is the point: the owner's folder is never written to by this.
    /// </summary>
    /// <remarks>
    /// A launch line that does not arrive leaves the application on the owner's home, and nothing
    /// in the application can tell that from a person starting it. What can is the probe, which
    /// started it and knows which home it asked for: it waits for this file in that home, and a
    /// launch that never wrote one is a launch that was not told. Absence is the signal, so the
    /// silent fallback costs the probe's start rather than somebody's meetings.
    /// </remarks>
    public void ReportIfAsked()
    {
        if (Asked)
        {
            Folder.Create();
            File.WriteAllText(Report.FullName, Folder.FullName);
        }
    }

    /// <summary>The report this home would have written.</summary>
    public FileInfo Report => new(Path.Combine(Folder.FullName, ReportName));

    /// <summary>Removes a report left by an earlier launch, so only this launch's can be read.</summary>
    public void ForgetReport() => File.Delete(Report.FullName);

    /// <summary>Whether a launch has reported resolving exactly this home.</summary>
    public bool HasBeenReported()
    {
        var report = Report;
        report.Refresh();
        if (!report.Exists)
        {
            return false;
        }

        try
        {
            return string.Equals(File.ReadAllText(report.FullName), Folder.FullName, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            // Being written at this moment; asked again.
            return false;
        }
    }

    /// <summary>The launch line that <see cref="FromLaunch"/> reads back as <paramref name="folder"/>.</summary>
    public static string LaunchArgumentsFor(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return $"{Switch} \"{folder.FullName}\"";
    }
}
