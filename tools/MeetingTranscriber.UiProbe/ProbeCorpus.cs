using System.IO;

using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// A corpus of the probe's own, and the user's pointer put back afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed, the only thing between a probe pressing <em>Grabar</em> and a meeting landing
/// in somebody's real corpus was a paragraph in <c>docs/ui-probe.md</c> asking agents not to — and
/// prose is not a guard. Sixteen screens are about to be built by agents driving this tool, several
/// of which are the recording screens, so pressing it is the point.
/// </para>
/// <para>
/// <b>It moves the product's own setting and puts it back.</b> The application reads where its
/// corpus is from one file, <c>%USERPROFILE%\MeetingTranscriber\corpus-location</c>, and ISC-114.2
/// already promises the corpus opens wherever that file says. So this is a location the probe sets,
/// exactly the way a person moving their corpus sets it, and not a new concept, not an argument to
/// the application and not a mode the shipped product carries. The alternatives were worse in the
/// same direction: an environment variable does not survive shell activation, and a
/// <c>--corpus</c> switch on the application would be a product surface built for a tool.
/// </para>
/// <para>
/// <b>Nothing here ever destroys what is put aside.</b> That one rule is what the whole type is
/// built on, and it replaced a cleverer one. The put-aside file is the only copy of where somebody's
/// meetings are; every state this can be found in is either <em>a put-aside exists</em>, and then it
/// is the user's and is left alone, or <em>none does</em>, and then whatever the pointer says is the
/// user's and goes aside. Deciding by reading the pointer as well — <em>is it already naming the
/// probe's folder?</em> — looks safer and is not: it answers <em>no</em> for a pointer it merely
/// could not read, for one a walk moved on purpose, and for one another probe process is between
/// two writes of, and the branch it then takes deletes the user's only copy. Two probe processes
/// interleaving here cost a redundant marker file that <see cref="PutBack"/> clears up, and never a
/// path.
/// </para>
/// <para>
/// <b>It is asserted on every <c>start</c>, not only the first.</b> The pointer is a file on a
/// machine and several things move it between two starts: a walk that drove the application's own
/// move-corpus screen, another probe process closing, the user. The application reads it once, when
/// it launches, so the only moment it has to be right is the moment before a launch — which is why
/// <see cref="StillPointedAtItsOwn"/> exists and why <see cref="McpHost"/> calls it every time
/// rather than making this once and trusting it.
/// </para>
/// <para>
/// <b>It lasts exactly as long as a host has an application open, and never longer.</b> Not the
/// process: <c>.mcp.json</c> starts this server at every Claude Code session in this checkout,
/// whether or not anything is ever driven, so a pointer moved when the server starts would hold a
/// person's own corpus behind a probe folder for hours at a time for nothing. And not a
/// <see cref="Session"/> either: <see cref="McpHost"/>'s <c>start</c> opens the new session before
/// letting the old one go, so a per-session arrangement would have the new one's put-aside file
/// unwound by the old one's disposal and leave a live probe pointed at the user's real corpus. So
/// the host owns one of these, made on the first <c>start</c> and let go on <c>close</c>.
/// </para>
/// <para>
/// <b>Putting it back after the window opens instead was weighed and not taken.</b> The application
/// resolves the pointer once, at launch, so a pointer held for a second rather than a session would
/// make a killed run almost harmless. What it would also do is leave the pointer as the user's while
/// an agent drives — and an agent driving the application's own move-corpus screen would then write
/// over the user's pointer with a folder it picked, with nothing left to put back. The wide window
/// fails towards a corpus nobody loses; the narrow one fails towards a pointer nobody can recover.
/// </para>
/// <para>
/// <b>What it cannot cover is being killed rather than closed.</b> <see cref="Dispose"/> puts the
/// pointer back for every ending this tool can see, and there is no ending it cannot see that also
/// runs code. So a killed run costs the user an application that opens on the probe's corpus until
/// the next probe session — which puts their pointer back on its way out — or until they move the
/// file back by hand. <c>docs/ui-probe.md</c> says which file and where.
/// </para>
/// <para>
/// One folder per machine and not per checkout. Two probe sessions at once are two writers over one
/// SQLite file, which is what <c>docs/ui-probe.md</c> already says to run one at a time — and a
/// folder per checkout would make that rule look solved while the real corpus was still one.
/// </para>
/// </remarks>
internal sealed class ProbeCorpus : IDisposable
{
    /// <summary>
    /// Where the probe's meetings go: beside the folder the application keeps its own data in, and
    /// never inside it. Inside would put spool folders and audio among a person's own corpus files
    /// with nothing but a name telling them apart.
    /// </summary>
    private const string FolderName = CorpusLocation.ApplicationFolderName + ".ui-probe";

    /// <summary>What the user's pointer is called while this tool is holding it.</summary>
    private const string PutAsideSuffix = ".before-the-probe";

    /// <summary>
    /// What says there was no pointer at all, which is a different thing from a pointer that says
    /// nothing.
    /// </summary>
    /// <remarks>
    /// A name of its own rather than an empty put-aside file, and the difference is not
    /// hypothetical: <see cref="CorpusRefusal.SettingSaysNothingUsable"/> is what a blank
    /// <c>corpus-location</c> means, and its own remarks argue at length that it is a refusal and
    /// not a fall back to the folder the application would have chosen for itself — because the
    /// alternative is a second, empty corpus while somebody's meetings sit on another disk.
    /// <see cref="CorpusLocation.Choose"/> cannot write a blank pointer, but a truncated file, a
    /// hand edit or a disk that filled can leave one, and putting that aside under a name that also
    /// means <em>there was none</em> would restore it by deleting it: a loud refusal turned into a
    /// silent new corpus, by a tool the user did not ask to touch their pointer. Two names, and
    /// each one says on disk what it is, which is what somebody healing this by hand reads.
    /// </remarks>
    private const string NoneAsideSuffix = ".none-before-the-probe";

    private readonly CorpusLocation _location;

    private readonly DirectoryInfo _folder;

    private readonly FileInfo _putAside;

    private readonly FileInfo _noneAside;

    private ProbeCorpus(CorpusLocation location, DirectoryInfo folder)
    {
        _location = location;
        _folder = folder;
        _putAside = new FileInfo(location.Setting.FullName + PutAsideSuffix);
        _noneAside = new FileInfo(location.Setting.FullName + NoneAsideSuffix);
    }

    /// <summary>The corpus the application this probe starts will open.</summary>
    internal string Folder => _folder.FullName;

    /// <summary>
    /// The one line both hosts say when an application opens, beside <see cref="Session.StartedAs"/>.
    /// It names the file as well as the folder because the file is what somebody puts back by hand
    /// after a run that was killed.
    /// </summary>
    internal string Arrangement =>
        $"its corpus is {_folder.FullName}, and the pointer in {_location.Setting.FullName} is put "
        + "back on close";

    /// <summary>
    /// Makes the probe's corpus if it is not there, and points this user's application at it.
    /// </summary>
    /// <remarks>
    /// The corpus before the pointer, in that order and never the other:
    /// <see cref="CorpusLocation.Choose"/> refuses a folder with no corpus in it, and so does the
    /// application at every start. A folder that already holds one costs a migration that finds
    /// nothing to do, which is what makes the second session cheap, and the pool is emptied
    /// afterwards so that this process is not still holding the file for the rest of the session.
    /// </remarks>
    internal static ProbeCorpus PointedAtItsOwn()
    {
        var location = CorpusLocation.OfThisUser();

        // Fallback is %USERPROFILE%\MeetingTranscriber, so its parent is the profile — the one
        // place the product already puts a folder of its own and the one CorpusLocation.Choose
        // will accept. Derived rather than spelled, so the day the product moves its folder the
        // probe's follows.
        var folder = new DirectoryInfo(
            Path.Combine(location.Fallback.Parent!.FullName, FolderName));
        folder.Create();

        using (CorpusDatabase.OpenMigrated(folder))
        {
        }

        CorpusDatabase.ClearPoolsFor(folder);

        var corpus = new ProbeCorpus(location, folder);
        corpus.StillPointedAtItsOwn();

        return corpus;
    }

    /// <summary>
    /// Points this user's application at the probe's corpus, whether or not it already was.
    /// </summary>
    /// <remarks>
    /// Called before every launch and not only the first, for the reason in this type's remarks.
    /// It is the same act every time — put the user's aside if nothing of this tool's is aside
    /// already, then choose — so calling it four times is calling it once.
    /// </remarks>
    internal void StillPointedAtItsOwn()
    {
        PutTheUsersAside();

        try
        {
            _location.Choose(_folder);
        }
        catch
        {
            // A pointer moved and then refused is still a pointer moved.
            PutBack();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            PutBack();
        }
        catch (Exception stuck) when (stuck is IOException or UnauthorizedAccessException)
        {
            // Never thrown out of a Dispose that is on the way out of a failure, and never silent
            // either: what is left behind is somebody's application opening on the wrong corpus,
            // and the two paths are what fixes it. Console.Error and not Out, because over MCP
            // stdout is the protocol.
            Console.Error.WriteLine(
                $"The corpus pointer could not be put back: move {_putAside.FullName} over "
                + $"{_location.Setting.FullName} by hand, or delete both to let the application "
                + $"choose for itself. ({stuck.Message})");
        }
    }

    /// <summary>
    /// Records what the pointer was before this tool touched one — unless something already is,
    /// in which case that is the record and it is left exactly as it is.
    /// </summary>
    private void PutTheUsersAside()
    {
        _putAside.Refresh();
        _noneAside.Refresh();

        if (_putAside.Exists || _noneAside.Exists)
        {
            return;
        }

        _location.Setting.Directory?.Create();
        _location.Setting.Refresh();

        try
        {
            if (_location.Setting.Exists)
            {
                File.Move(_location.Setting.FullName, _putAside.FullName);
            }
            else
            {
                File.WriteAllBytes(_noneAside.FullName, []);
            }
        }
        catch (IOException)
        {
            // Another probe process got between the look and the move — it has the user's pointer
            // aside under one of these two names by now, and that is the record. Nothing here
            // overwrites it, so the worst this costs is a marker file PutBack clears up.
        }
    }

    /// <summary>
    /// Puts the user's pointer back, whether it was moved a moment ago or by a run that was killed.
    /// Does nothing when there is nothing put aside, so calling it twice is calling it once.
    /// </summary>
    /// <remarks>
    /// Unconditional, and that is the promise this type makes: whatever the pointer says when a
    /// session ends, what the user had is what they get back. A walk that drove the application's
    /// own move-corpus screen has changed the pointer on purpose, inside a probe session, and
    /// leaving that behind would be the probe deciding where somebody's meetings live.
    /// </remarks>
    private void PutBack()
    {
        _putAside.Refresh();
        _noneAside.Refresh();

        if (_putAside.Exists)
        {
            File.Move(_putAside.FullName, _location.Setting.FullName, overwrite: true);
        }
        else if (_noneAside.Exists)
        {
            // There was no pointer before this tool touched one, so there is none afterwards — and
            // the application opens the corpus it would have chosen for itself.
            File.Delete(_location.Setting.FullName);
        }

        _noneAside.Refresh();
        if (_noneAside.Exists)
        {
            _noneAside.Delete();
        }
    }
}
