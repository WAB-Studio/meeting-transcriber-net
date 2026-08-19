namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>What stopped the application opening a corpus where it went looking for one.</summary>
public enum CorpusRefusal
{
    /// <summary>
    /// The file recording where the corpus is is there and says nothing that can be used — it
    /// could not be read, it is blank, or what is in it is not a full path.
    /// </summary>
    /// <remarks>
    /// This is a refusal and not a fall back to the folder the application would have chosen for
    /// itself, which is the opposite of what an unreadable language preference does. The two
    /// costs are nothing alike: a language somebody re-picks in one click against a corpus of
    /// meetings on another disk, which the application would then leave sitting there while it
    /// made a second, empty one somewhere else.
    /// </remarks>
    SettingSaysNothingUsable = 1,

    /// <summary>The folder it names is not there — an unplugged disk, or a path typed wrong.</summary>
    FolderIsNotThere = 2,

    /// <summary>
    /// The folder is there and holds no corpus. Refused rather than filled with a new one, for the
    /// reason above: the usual cause is a path that no longer reaches the corpus it used to, and
    /// answering that with an empty corpus reads as success.
    /// </summary>
    NoCorpusInTheFolder = 3,

    /// <summary>
    /// It is inside the package's own data folder, which uninstalling deletes. Everything the
    /// corpus holds that was paid for would go with it, so it is refused however it was arrived
    /// at.
    /// </summary>
    InsideThePackageContainer = 4,
}

/// <summary>
/// Where the application would open its corpus, or what stopped it and the path that says so.
/// </summary>
/// <param name="Path">
/// Always something a person can be shown. The corpus folder when nothing stopped it; otherwise
/// the path the refusal is about, which is the folder for every refusal but
/// <see cref="CorpusRefusal.SettingSaysNothingUsable"/> — that one is about the file that was
/// supposed to name a folder and could not.
/// </param>
/// <param name="Refusal">What stopped it, or <c>null</c> when nothing did.</param>
public sealed record CorpusFolder(string Path, CorpusRefusal? Refusal)
{
    /// <summary>
    /// The folder, which only an answer nothing stopped has. Null exactly when
    /// <see cref="Refusal"/> is not, so a caller that reads this has already been made to look at
    /// the other.
    /// </summary>
    public DirectoryInfo? Folder => Refusal is null ? new DirectoryInfo(Path) : null;

    internal static CorpusFolder Opens(DirectoryInfo folder) => new(folder.FullName, null);

    internal static CorpusFolder Refused(CorpusRefusal refusal, string path) => new(path, refusal);
}

/// <summary>
/// Which folder this user's corpus is in. One place decides it, so nothing else in the
/// application has to know where a corpus goes or what makes a folder unfit to hold one.
/// </summary>
/// <remarks>
/// <para>
/// This is the one type in <c>Infrastructure</c> that has a default corpus folder, and it is not
/// an exception to the rule that <see cref="CorpusFiles"/> and <see cref="CorpusDatabase"/> state
/// — those are still always handed a root and still never infer one. The rule was about a
/// component quietly guessing which corpus it was working on. This is the opposite: it is the one
/// thing whose whole job is answering that question, out loud, with what stopped it when it
/// cannot.
/// </para>
/// <para>
/// It says where and never opens anything. What to do about a refusal — say it, offer another
/// folder — belongs to the screen, which is also the only thing that can say it in the language
/// somebody is reading in.
/// </para>
/// </remarks>
public sealed class CorpusLocation
{
    /// <summary>
    /// The folder this application keeps its own data in, under the user's local application
    /// data. It is both where the corpus goes when nobody has said otherwise and where the file
    /// saying otherwise is kept — the second of which is why the file stays behind when the
    /// corpus moves, since a pointer that travelled with what it points at would point at
    /// nothing.
    /// </summary>
    public const string ApplicationFolderName = "MeetingTranscriber";

    /// <summary>The file holding the folder somebody moved the corpus to.</summary>
    public const string SettingName = "corpus-location";

    /// <summary>
    /// The three segments every MSIX package's own data folder sits under. Recognising the
    /// container by its path rather than by asking the packaging API is deliberate and is the
    /// whole point of this check: the failure being guarded against is the local application data
    /// folder itself coming back redirected into the container, and a path that has been
    /// redirected still reads as being under these three.
    /// </summary>
    private static readonly string[] PackageContainer = ["AppData", "Local", "Packages"];

    public CorpusLocation(FileInfo setting, DirectoryInfo fallback)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(fallback);

        Setting = setting;
        Fallback = fallback;
    }

    /// <summary>The file that says where the corpus is, whether or not it is there.</summary>
    public FileInfo Setting { get; }

    /// <summary>Where the corpus is when nobody has said otherwise.</summary>
    public DirectoryInfo Fallback { get; }

    /// <summary>
    /// Where this user's corpus is.
    /// </summary>
    /// <remarks>
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> and never
    /// <c>ApplicationData.Current.LocalFolder</c>. The second is the package's own folder —
    /// <c>%LOCALAPPDATA%\Packages\&lt;family&gt;\LocalCache</c> — which uninstalling the
    /// application deletes, and inside it would be the provider responses somebody already paid
    /// for and cannot ask for again. The two read alike in a debugger, so what keeps them apart is
    /// not this comment: it is that no folder any answer here names is allowed to be inside the
    /// container, and that nothing in <c>src/</c> mentions the other one at all.
    /// </remarks>
    public static CorpusLocation OfThisUser()
    {
        var applicationFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);

        return new CorpusLocation(
            new FileInfo(System.IO.Path.Combine(applicationFolder, SettingName)),
            new DirectoryInfo(applicationFolder));
    }

    /// <summary>
    /// Whether this path is inside a package's own data folder, and so would be deleted with the
    /// package. Anything at or under the container root counts.
    /// </summary>
    public static bool InsideThePackageContainer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = System.IO.Path.GetFullPath(path)
            .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        for (var start = 0; start + PackageContainer.Length <= segments.Length; start++)
        {
            var run = PackageContainer.Index().All(segment =>
                string.Equals(segments[start + segment.Index], segment.Item, StringComparison.OrdinalIgnoreCase));

            if (run)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What a folder somebody named would be as a corpus location. What a picker asks before
    /// offering a folder, and what <see cref="Choose"/> holds a caller to, so the rules are
    /// written once and every way of arriving at a folder meets the same ones.
    /// </summary>
    /// <remarks>
    /// A folder somebody names has to hold a corpus already, which the folder in
    /// <see cref="Fallback"/> does not: that one is where the application puts a corpus, and this
    /// is where it is told one already is. Moving a corpus is therefore moving the files and then
    /// saying so, in that order — the other order names a folder that does not answer yet.
    /// </remarks>
    public static CorpusFolder Inspect(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (InsideThePackageContainer(folder.FullName))
        {
            return CorpusFolder.Refused(CorpusRefusal.InsideThePackageContainer, folder.FullName);
        }

        // Directory.Exists rather than folder.Exists: a DirectoryInfo caches the answer at its
        // first read, and this one may have been made before the disk it names was plugged in.
        if (!Directory.Exists(folder.FullName))
        {
            return CorpusFolder.Refused(CorpusRefusal.FolderIsNotThere, folder.FullName);
        }

        return CorpusDatabase.HoldsACorpus(folder)
            ? CorpusFolder.Opens(folder)
            : CorpusFolder.Refused(CorpusRefusal.NoCorpusInTheFolder, folder.FullName);
    }

    /// <summary>
    /// Which folder the corpus is in this time the application starts.
    /// </summary>
    /// <remarks>
    /// The folder this falls back to is checked for one thing where a folder somebody named is
    /// checked for three, and the difference is not laxity: it does not have to be there and does
    /// not have to hold a corpus, because it is where the first corpus is made. What it does have
    /// to do is survive an uninstall, which is the one check that is about the folder rather than
    /// about what is in it.
    /// </remarks>
    public CorpusFolder Resolve()
    {
        if (Chosen() is { } chosen)
        {
            return Inspect(chosen);
        }

        if (File.Exists(Setting.FullName))
        {
            return CorpusFolder.Refused(CorpusRefusal.SettingSaysNothingUsable, Setting.FullName);
        }

        return InsideThePackageContainer(Fallback.FullName)
            ? CorpusFolder.Refused(CorpusRefusal.InsideThePackageContainer, Fallback.FullName)
            : CorpusFolder.Opens(Fallback);
    }

    /// <summary>
    /// Records that the corpus is in this folder from now on. Throws unless a corpus could be
    /// opened there, because a location written down and then refused at every start is a corpus
    /// somebody cannot reach and cannot correct.
    /// </summary>
    public void Choose(DirectoryInfo folder)
    {
        var inspected = Inspect(folder);
        if (inspected.Refusal is { } refusal)
        {
            throw new ArgumentException(
                $"'{inspected.Path}' cannot be recorded as where the corpus is: {refusal}.",
                nameof(folder));
        }

        Setting.Directory?.Create();
        File.WriteAllText(Setting.FullName, folder.FullName);
    }

    /// <summary>
    /// The folder the setting names, or <c>null</c> when it names nothing usable — which covers
    /// nobody having chosen and the file saying something this cannot use, two answers
    /// <see cref="Resolve"/> tells apart by whether the file is there at all.
    /// </summary>
    private DirectoryInfo? Chosen()
    {
        try
        {
            // File.Exists rather than Setting.Exists, which a FileInfo answers from what it saw
            // the first time it was asked. This object outlives a Choose that writes the file.
            if (!File.Exists(Setting.FullName))
            {
                return null;
            }

            var written = File.ReadAllText(Setting.FullName).Trim();

            // Fully qualified rather than merely rooted: a path resolved against the working
            // directory is a different corpus depending on where the application was started
            // from, and this is read before there is a window to say so in.
            return System.IO.Path.IsPathFullyQualified(written) ? new DirectoryInfo(written) : null;
        }
        catch (Exception unusable) when (
            unusable is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Every way the file can fail to name a folder is the one answer: it says nothing this
            // can use. Which of them it was would send somebody to look at the file, and what the
            // application offers instead is choosing a folder again — after which the file is
            // written over and whatever was in it stops mattering.
            return null;
        }
    }
}
