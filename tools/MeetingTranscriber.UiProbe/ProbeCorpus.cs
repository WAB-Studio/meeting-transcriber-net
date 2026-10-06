using System.IO;

using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The home the probe's application runs in: a folder of its own, holding a corpus, handed to the
/// application on its launch line.
/// </summary>
/// <remarks>
/// <para>
/// Until this was written, the probe made its corpus the application's by moving the owner's own
/// pointer, <c>%USERPROFILE%\MeetingTranscriber\corpus-location</c>, aside, choosing its own, and
/// putting the owner's back on close. Every ending it could see was handled, and the one it could
/// not — being killed — left the owner's installed application opening on the probe's corpus with
/// the real pointer under another name. The recovery was a sentence in <c>docs/ui-probe.md</c>.
/// A tool whose failure mode is the owner's meetings looking gone should not be built on moving
/// the owner's files at all, so it does not: the application is told its home on its launch line
/// (<see cref="ApplicationHome"/>), keeps its two pointers and its first corpus there, and nothing
/// under <c>%USERPROFILE%\MeetingTranscriber</c> is read, moved or written by a probe session.
/// There is nothing to put back and nothing to heal by hand.
/// </para>
/// <para>
/// That reverses the remark this type used to carry, that a launch switch would be a product
/// surface built for a tool. It is one switch, the MCP server already takes <c>--corpus</c> the same
/// way, and it is the only way the probe stops touching the owner's folder. An environment
/// variable does not survive shell activation, and a pointer in the package's own data folder
/// would be a second place the shipped product reads its pointer from for ever.
/// </para>
/// <para>
/// The home is <c>%USERPROFILE%\MeetingTranscriber.ui-probe</c>, which is the folder the probe's
/// corpus has always been in, so earlier walks' meetings are still there. One per machine and not
/// per checkout: two probe sessions at once are two writers over one SQLite file, which is what
/// <c>docs/ui-probe.md</c> already says to run one at a time — and a folder per checkout would make
/// that rule look solved while the real corpus was still one.
/// </para>
/// <para>
/// What a home does not isolate is in <c>docs/ui-probe.md</c>, and in the server's description: the
/// Deepgram key lives in Credential Manager per Windows user, and on a checkout with no package
/// suffix the installed package's own language and theme files are the installed application's.
/// </para>
/// </remarks>
internal sealed class ProbeCorpus
{
    /// <summary>
    /// The probe's home: beside the folder the application keeps its own data in, and never inside
    /// it. Inside would put spool folders and audio among a person's own corpus files with nothing
    /// but a name telling them apart.
    /// </summary>
    private const string FolderName = CorpusLocation.ApplicationFolderName + ".ui-probe";

    /// <summary>The home of a corpus that will not open, beside <see cref="FolderName"/>.</summary>
    private const string RefusedFolderName = CorpusLocation.ApplicationFolderName + ".ui-probe.refused";

    /// <summary>Where, inside the refused home, the corpus that is no longer there was.</summary>
    private const string RefusedCorpusFolderName = "corpus";

    private readonly DirectoryInfo _home;

    private ProbeCorpus(DirectoryInfo home) => _home = home;

    /// <summary>The home the application this probe starts is asked to run in.</summary>
    internal string Folder => _home.FullName;

    /// <summary>What the application is started with, which says where its home is.</summary>
    internal string LaunchArguments => ApplicationHome.LaunchArgumentsFor(_home);

    /// <summary>
    /// Clears what an earlier launch reported, so that only the one about to start can confirm the
    /// home.
    /// </summary>
    internal void ForgetWhatWasReported() => ApplicationHome.Under(_home).ForgetReport();

    /// <summary>Whether the application has said it resolved this home.</summary>
    internal bool HasBeenConfirmed() => ApplicationHome.Under(_home).HasBeenReported();

    /// <summary>
    /// The one line both hosts say when an application opens, beside <see cref="Session.StartedAs"/>.
    /// </summary>
    internal string Arrangement =>
        $"its home is {_home.FullName}; nothing under %USERPROFILE%\\{CorpusLocation.ApplicationFolderName} "
        + "is read or written; the Deepgram key is shared";

    /// <summary>
    /// Makes the probe's home and the corpus in it if they are not there.
    /// </summary>
    /// <remarks>
    /// The application opens a corpus at every start and refuses a folder with none, so the home
    /// holds one before the first launch — exactly as the pointer's folder always did. A home that
    /// already holds one costs a migration that finds nothing to do, which is what makes the second
    /// session cheap, and the pool is emptied afterwards so that this process is not still holding
    /// the file for the rest of the session.
    /// </remarks>
    internal static ProbeCorpus PointedAtItsOwn()
    {
        var home = HomeNamed(FolderName);
        MakeACorpusIn(home);

        return new ProbeCorpus(home);
    }

    /// <summary>
    /// A home whose own pointer names a folder that once held a corpus and no longer does, so the
    /// launch resolves <see cref="CorpusRefusal.NoCorpusInTheFolder"/> and the refused-corpus screen
    /// is the one a script walks.
    /// </summary>
    /// <remarks>
    /// The pointer is chosen while the corpus is there, because <see cref="CorpusLocation.Choose"/>
    /// refuses a folder with none, and the corpus files go afterwards; the pointer is never written
    /// by hand.
    /// </remarks>
    internal static ProbeCorpus PointedAtOneThatWillNotOpen()
    {
        var home = HomeNamed(RefusedFolderName);
        var corpus = new DirectoryInfo(Path.Combine(home.FullName, RefusedCorpusFolderName));
        MakeACorpusIn(corpus);

        ApplicationHome.Under(home).Corpus.Choose(corpus);

        // The pool first, so nothing of this process holds the files about to go.
        CorpusDatabase.ClearPoolsFor(corpus);

        var database = CorpusDatabase.PathIn(corpus);
        foreach (var file in new[] { database, database + "-wal", database + "-shm" })
        {
            File.Delete(file);
        }

        return new ProbeCorpus(home);
    }

    private static DirectoryInfo HomeNamed(string name) => new(Path.Combine(Profile(), name));

    private static string Profile() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static void MakeACorpusIn(DirectoryInfo folder)
    {
        folder.Create();

        using (CorpusDatabase.OpenMigrated(folder))
        {
        }

        CorpusDatabase.ClearPoolsFor(folder);
    }
}
