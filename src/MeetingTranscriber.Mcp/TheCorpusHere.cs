using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Mcp;

/// <summary>
/// The corpus this server answers about, opened read-only, one context per tool call.
/// </summary>
/// <remarks>
/// <para>
/// One per call and never one held open, for the reason <c>MeetingsDrawer</c> gives about the
/// window: what is answered has to be what is on disk, and a meeting whose transcription landed
/// while an agent was reading it is exactly the case a remembered corpus gets wrong. An agent's
/// session outlives a window's by hours.
/// </para>
/// <para>
/// <see cref="LetGo"/> is the other half of that and is not optional. Disposing a context returns
/// its connection to a pool rather than closing it, so a session that had answered one call would
/// otherwise hold <c>corpus.db</c>, its write-ahead log and its shared-memory file open until the
/// agent quit — and somebody wanting to move, rename or back up their corpus meanwhile could not.
/// One context per call that keeps the file is one context held open with extra steps.
/// </para>
/// <para>
/// Nothing calls this at start-up, and that ordering is in <see cref="CorpusServer.RunAsync"/>: an
/// MCP client starts this server in every session of every checkout, most of which never call a
/// tool, and resolving a corpus before one would hold somebody's SQLite file open for hours for a
/// call nobody made.
/// </para>
/// </remarks>
internal static class TheCorpusHere
{
    /// <summary>
    /// The corpus, or the reason there is not one to read, in words.
    /// </summary>
    /// <remarks>
    /// Three refusals and they are three different states, which is why none of them is left to
    /// SQLite. A refusal names the folder and what stopped it; a folder with no corpus in it is the
    /// single most likely first call this server ever gets and says so rather than coming back as
    /// <em>unable to open database file</em>; and a corpus this build's schema has moved past would
    /// otherwise fail on a missing column and name neither the count nor the migration.
    /// </remarks>
    internal static CorpusDbContext OpenReadOnly(CorpusLocation where)
    {
        var located = where.Resolve();

        if (located.Refusal is { } refusal)
        {
            throw new McpRefused(Says(refusal, located.Path));
        }

        var folder = located.Folder!;

        // Asked again rather than read off the answer above, which says what was true when it was
        // resolved. The corpus comes into existence under a running server — the first meeting
        // somebody keeps makes one — so an agent that was told there is nothing here has to be
        // able to ask again and be told otherwise.
        if (!CorpusDatabase.HoldsACorpus(folder))
        {
            throw new McpRefused(
                $"There is no corpus at '{folder.FullName}' yet, so there is nothing to read. "
                + "Record a meeting in the application first, or point it at the folder where the "
                + "meetings already are.");
        }

        var context = CorpusDatabase.OpenReadOnly(folder);

        try
        {
            // The first thing anything asks the file, which is also where a file SQLite will not
            // have is found out about — the same order src/MeetingTranscriber.Cli/Corpus.cs reads
            // it in. Without it the first tool call fails on a missing column and names neither
            // this build nor the corpus.
            var behind = context.Database.GetPendingMigrations().ToArray();

            if (behind.Length > 0)
            {
                throw new McpRefused(
                    $"The corpus at '{folder.FullName}' is {behind.Length} migration(s) behind this "
                    + $"build, the first being '{behind[0]}'. Open the application, which brings a "
                    + "corpus up to date when it opens one, and then ask again.");
            }
        }
        catch
        {
            // Every caller lets the corpus go on the way out, and one that did not would leave the
            // file held by a server on its way to saying it could not read it.
            LetGo(context);
            throw;
        }

        return context;
    }

    /// <summary>
    /// Closes the corpus and lets go of its file, which are two things and not one.
    /// </summary>
    /// <remarks>
    /// Disposing returns the connection to a pool keyed by the connection string, so the handle
    /// stays open and the file stays held. <see cref="CorpusDatabase.ClearPoolsFor"/> is what
    /// actually lets go, and it reaches this corpus and no other — a process-wide clear would
    /// dispose the idle connections of every corpus open in it. The cost is that the next tool call
    /// opens a fresh connection instead of taking a warm one, which for a server that resolves the
    /// corpus per call anyway is the price of the promise rather than a regression.
    /// </remarks>
    internal static void LetGo(CorpusDbContext context)
    {
        // Read before the dispose: the root is derived from the connection string, and a disposed
        // context has nothing to derive it from.
        var folder = context.Root;

        context.Dispose();
        CorpusDatabase.ClearPoolsFor(folder);
    }

    /// <summary>
    /// What each refusal is, said to whoever can do something about it. The enum's own names are
    /// not sentences, and an agent relaying <c>SettingSaysNothingUsable</c> to a person has relayed
    /// nothing.
    /// </summary>
    private static string Says(CorpusRefusal refusal, string path) => refusal switch
    {
        CorpusRefusal.SettingSaysNothingUsable =>
            $"'{path}' is supposed to say where the corpus is and says nothing usable. Open the "
            + "application and say where the meetings are kept.",

        CorpusRefusal.FolderDoesNotAnswer =>
            $"'{path}' does not answer: it is not there, or this user may not read it. A corpus on "
            + "a disk that is not plugged in reads exactly like this.",

        CorpusRefusal.NoCorpusInTheFolder =>
            $"'{path}' is where the corpus is supposed to be and holds none. The usual cause is a "
            + "path that no longer reaches the corpus it used to.",

        CorpusRefusal.GoesWhenThePackageDoes =>
            $"'{path}' would go when the application is uninstalled, so nothing may keep a corpus "
            + "there. Say where the meetings are kept, somewhere outside application data.",

        // A fifth refusal with no sentence here is a defect and not an answer: the alternative is
        // relaying the enum's own name, which is exactly what the summary above says must not
        // happen. `CorpusStatements.StoredIn` takes the same line about its fourth section.
        _ => throw new ArgumentOutOfRangeException(
            nameof(refusal), refusal, "This refusal has no sentence to say to anybody."),
    };
}
