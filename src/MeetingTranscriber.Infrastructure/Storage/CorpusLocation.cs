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

    /// <summary>
    /// The folder does not answer: it is not there, or this user may not read it.
    /// </summary>
    /// <remarks>
    /// One refusal for two causes because this cannot tell them apart — Windows answers a folder
    /// somebody may not list exactly as it answers one that is gone. The command line takes the
    /// same line where SQLite hands it one sentence for two very different faults: say what is
    /// known, name the path, and let somebody look. Guessing between them would send half of them
    /// to check the wrong thing.
    /// </remarks>
    FolderDoesNotAnswer = 2,

    /// <summary>
    /// The folder answers and holds no corpus. Refused rather than filled with a new one: somebody
    /// said their corpus is there, and the usual cause of it not being there is a path that no
    /// longer reaches the corpus it used to. Answering that with an empty corpus reads as success.
    /// </summary>
    NoCorpusInTheFolder = 3,

    /// <summary>
    /// It is inside the package's own data folder, which uninstalling deletes. Everything the
    /// corpus holds that was paid for would go with it, so it is refused however it was arrived
    /// at — including by a folder that only leads there through a link.
    /// </summary>
    InsideThePackageContainer = 4,
}

/// <summary>
/// Where the application would open its corpus, or what stopped it and the path that says so.
/// </summary>
/// <remarks>
/// Made only by <see cref="CorpusLocation"/>, so the two states it can be in are the two it is
/// built in: a folder with nothing against it, or a refusal and the path that refusal is about.
/// There is no third, and no caller can compose one.
/// </remarks>
public sealed record CorpusFolder
{
    private CorpusFolder(string path, CorpusRefusal? refusal, bool holdsACorpus)
    {
        Path = path;
        Refusal = refusal;
        HoldsACorpus = holdsACorpus;
    }

    /// <summary>
    /// Always something a person can be shown. The corpus folder when nothing stopped it;
    /// otherwise the path the refusal is about, which is the folder for every refusal but
    /// <see cref="CorpusRefusal.SettingSaysNothingUsable"/> — that one is about the file that was
    /// supposed to name a folder and could not.
    /// </summary>
    public string Path { get; }

    /// <summary>What stopped it, or <c>null</c> when nothing did.</summary>
    public CorpusRefusal? Refusal { get; }

    /// <summary>
    /// Whether there is already a corpus in that folder. False with no refusal is the one case
    /// where a caller is about to make one, and it is said out loud for the reason the whole
    /// refusal above exists: making a corpus where a person expected to find theirs is how
    /// somebody ends up looking at an empty list with nothing wrong on screen. Whoever opens this
    /// folder says so first and makes one because they were told, never because nothing objected.
    /// </summary>
    public bool HoldsACorpus { get; }

    /// <summary>The folder, which only an answer nothing stopped has.</summary>
    public DirectoryInfo? Folder => Refusal is null ? new DirectoryInfo(Path) : null;

    internal static CorpusFolder Opens(DirectoryInfo folder, bool holdsACorpus) =>
        new(folder.FullName, null, holdsACorpus);

    internal static CorpusFolder Refused(CorpusRefusal refusal, string path) =>
        new(path, refusal, holdsACorpus: false);
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
/// It says where and never opens anything, and it never makes a corpus. What to do about a refusal
/// — say it, offer another folder — belongs to the screen, which is also the only thing that can
/// say it in the language somebody is reading in.
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
    /// Where the setting is written before it is put in place, so that a machine that stops
    /// half way leaves the old pointer whole rather than an empty file. A corpus nothing points at
    /// any more is not lost — the refusal names it — but it costs somebody the walk back through a
    /// picker to say where their meetings are, and this is three lines.
    /// </summary>
    private const string SettingBeingWritten = SettingName + ".new";

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
        var applicationFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName);

        return new CorpusLocation(
            new FileInfo(Path.Combine(applicationFolder, SettingName)),
            new DirectoryInfo(applicationFolder));
    }

    /// <summary>
    /// The folder every MSIX package's own data lives under, for this user. Anything at or under
    /// it goes when a package does.
    /// </summary>
    /// <remarks>
    /// Anchored on the profile rather than on the local application data folder, which is the
    /// point rather than a detail. A packaged process may be handed a local application data
    /// folder that is already inside the container; anchoring on it would then compare the
    /// container against itself and find nothing wrong with anywhere. The profile is not
    /// redirected, so the anchor holds whichever folder the process was handed — and a corpus in a
    /// folder that merely happens to be spelled <c>AppData\Local\Packages</c> on some other disk
    /// is somebody's own folder and is left alone.
    /// </remarks>
    public static string PackageContainerOfThisUser() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "Local", "Packages");

    /// <summary>
    /// Whether this path is inside a package's own data folder, and so would be deleted with the
    /// package.
    /// </summary>
    public static bool InsideThePackageContainer(string path) =>
        InsideThePackageContainer(path, PackageContainerOfThisUser());

    /// <summary>
    /// The same question against a named container folder, which is how it is tested: the answer
    /// has to be about a real profile, and a build agent's profile is not the one a test can
    /// write literal paths for.
    /// </summary>
    /// <remarks>
    /// Where the path is written and where it leads are two questions, and only the second is the
    /// one that matters — a folder on another disk that is a link into the container is deleted
    /// with the package exactly like a folder spelled that way. So every step from the path up to
    /// its root is asked whether it is a link, and where a link goes is asked the first question
    /// again. One hop, not a chain: a link into a link into the container is not a thing anybody
    /// has, and following forever is how a loop of links becomes a start-up that never finishes.
    /// </remarks>
    public static bool InsideThePackageContainer(string path, string container)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(container);

        var full = Path.GetFullPath(path);
        if (IsAtOrUnder(full, container))
        {
            return true;
        }

        for (var step = new DirectoryInfo(full); step is not null; step = step.Parent)
        {
            if (LinkTargetOf(step) is { } target && IsAtOrUnder(Path.GetFullPath(target), container))
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

        try
        {
            // Directory.Exists rather than folder.Exists, which a DirectoryInfo answers from what
            // it saw the first time it was asked. This one may have been made before the disk it
            // names was plugged in.
            if (!Directory.Exists(folder.FullName))
            {
                return CorpusFolder.Refused(CorpusRefusal.FolderDoesNotAnswer, folder.FullName);
            }

            return CorpusDatabase.HoldsACorpus(folder)
                ? CorpusFolder.Opens(folder, holdsACorpus: true)
                : CorpusFolder.Refused(CorpusRefusal.NoCorpusInTheFolder, folder.FullName);
        }
        catch (Exception unanswered) when (
            unanswered is IOException or UnauthorizedAccessException)
        {
            // A disk that went away between the two questions, or a folder this user may not read
            // into. Neither is worth throwing out of a start-up: what a person can do about either
            // is name a different folder, which is what a refusal offers.
            return CorpusFolder.Refused(CorpusRefusal.FolderDoesNotAnswer, folder.FullName);
        }
    }

    /// <summary>
    /// Which folder the corpus is in this time the application starts. What the setting says when
    /// there is one, and where the first corpus goes when there is not.
    /// </summary>
    public CorpusFolder Resolve() => WhatTheSettingSays() ?? WhereTheFirstCorpusGoes();

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

        // Written beside itself and moved into place, so the pointer is the old one or the new one
        // and never a file that was being written when the machine stopped.
        var staging = Path.Combine(Setting.Directory?.FullName ?? ".", SettingBeingWritten);
        File.WriteAllText(staging, folder.FullName);
        File.Move(staging, Setting.FullName, overwrite: true);
    }

    /// <summary>Whether this path is that folder or inside it.</summary>
    private static bool IsAtOrUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);

        // A path on another disk comes back rooted, and one above the root comes back climbing.
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Where this folder leads if it is a link, and <c>null</c> if it is not one or cannot say.
    /// </summary>
    private static string? LinkTargetOf(DirectoryInfo folder)
    {
        try
        {
            // Not the final target: a link into the container is read from the link itself, and
            // asking for the end of the chain would need every step of it to be there — which a
            // link into a folder an uninstall already deleted is exactly not.
            return folder.ResolveLinkTarget(returnFinalTarget: false)?.FullName;
        }
        catch (Exception unanswered) when (
            unanswered is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the setting file makes of itself, or <c>null</c> when there is no setting file — which
    /// is the one thing that means nobody has chosen.
    /// </summary>
    /// <remarks>
    /// The read is what answers both questions, rather than an existence check followed by a read.
    /// Asked as two, the file that goes missing between them reads as nobody having chosen, which
    /// is the one answer that must never be reached by accident: it is what sends the application
    /// off to its own folder while somebody's meetings sit where the file used to point.
    /// </remarks>
    private CorpusFolder? WhatTheSettingSays()
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
            return CorpusFolder.Refused(CorpusRefusal.SettingSaysNothingUsable, Setting.FullName);
        }

        // Fully qualified rather than merely rooted: a path resolved against the working directory
        // is a different corpus depending on where the application was started from, and this is
        // read before there is a window to say so in.
        if (!Path.IsPathFullyQualified(written))
        {
            return CorpusFolder.Refused(CorpusRefusal.SettingSaysNothingUsable, Setting.FullName);
        }

        try
        {
            return Inspect(new DirectoryInfo(written));
        }
        catch (ArgumentException)
        {
            // A path Windows will not have at all — an embedded null, a device name. It says
            // nothing usable like the rest, and which of them it was would only send somebody to
            // look at a file the next choice writes over anyway.
            return CorpusFolder.Refused(CorpusRefusal.SettingSaysNothingUsable, Setting.FullName);
        }
    }

    /// <summary>
    /// Where the application puts a corpus when nobody has said otherwise. The one thing asked of
    /// it is that it survive an uninstall: it does not have to be there yet, because this is the
    /// folder the first corpus is made in — and whether there is one there yet is what
    /// <see cref="CorpusFolder.HoldsACorpus"/> says, so that making one is something the caller
    /// was told about rather than something nothing objected to.
    /// </summary>
    private CorpusFolder WhereTheFirstCorpusGoes()
    {
        if (InsideThePackageContainer(Fallback.FullName))
        {
            return CorpusFolder.Refused(CorpusRefusal.InsideThePackageContainer, Fallback.FullName);
        }

        try
        {
            return CorpusFolder.Opens(Fallback, CorpusDatabase.HoldsACorpus(Fallback));
        }
        catch (Exception unanswered) when (
            unanswered is IOException or UnauthorizedAccessException)
        {
            return CorpusFolder.Refused(CorpusRefusal.FolderDoesNotAnswer, Fallback.FullName);
        }
    }
}
