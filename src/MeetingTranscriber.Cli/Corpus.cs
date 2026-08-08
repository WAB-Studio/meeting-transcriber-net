using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The corpus a command was pointed at, and the four ways one is opened.
/// </summary>
/// <remarks>
/// <para>
/// The directory is always given and never inferred. A default would be a corpus in whichever
/// folder this build happened to guess, and the corpus holds artifacts that cannot be obtained a
/// second time — the same reason nothing in <c>Infrastructure</c> has one either.
/// </para>
/// <para>
/// Only <see cref="Migrate"/> brings a corpus into existence or moves its schema. Every other
/// command refuses a directory with no corpus in it rather than creating one, because the usual
/// cause of a corpus that is not there is a path typed wrong, and answering that with an empty
/// corpus reads as success.
/// </para>
/// </remarks>
public sealed class Corpus
{
    /// <summary>Where a corpus keeps its database, as arquitectura.md §4.1 lays a corpus out.</summary>
    public const string DatabaseName = "corpus.db";

    /// <summary>The option every command takes.</summary>
    public const string Option = "--corpus";

    public Corpus(DirectoryInfo root) => Root = root;

    public DirectoryInfo Root { get; }

    public string DatabasePath => Path.Combine(Root.FullName, DatabaseName);

    public static Corpus At(Arguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new Corpus(new DirectoryInfo(arguments.Required(Option)));
    }

    /// <summary>
    /// Read only, and behind is allowed. What <c>status</c> opens, so the command that says why
    /// the others refused can still answer.
    /// </summary>
    public CorpusDbContext Inspect() => CorpusDatabase.OpenReadOnly(Existing());

    /// <summary>Read only and up to date: the connection itself refuses to write.</summary>
    public CorpusDbContext Read() => Current(CorpusDatabase.OpenReadOnly(Existing()));

    /// <summary>Read and write, up to date.</summary>
    public CorpusDbContext Write() => Current(CorpusDatabase.Open(Existing()));

    /// <summary>Creates the corpus if there is none and brings its schema up to this build.</summary>
    public CorpusDbContext Migrate() => Answered(() =>
    {
        Root.Create();
        DiscardEmpty();
        return CorpusDatabase.OpenMigrated(DatabasePath);
    });

    /// <summary>
    /// The migrations this build has and the corpus does not. It is also the first thing anything
    /// asks the file, so it is where a file SQLite will not have is found out about.
    /// </summary>
    public IReadOnlyList<string> Pending(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Answered(() =>
        {
            try
            {
                return context.Database.GetPendingMigrations().ToArray();
            }
            catch
            {
                // Every caller throws the corpus away on the way out, and one that did not would
                // leave the file held by a process on its way to printing why it could not read it.
                context.Dispose();
                throw;
            }
        });
    }

    /// <summary>
    /// Throws unless there is a corpus here. What a command that only touches the files calls, so
    /// that a path typed wrong is refused by the same sentence as everywhere else.
    /// </summary>
    public void EnsureThere()
    {
        var file = new FileInfo(DatabasePath);
        if (!file.Exists)
        {
            throw new CommandException(
                $"There is no {DatabaseName} in '{Root.FullName}'. 'migrate' makes one there.");
        }

        if (file.Length == 0)
        {
            throw new CommandException(
                $"The {DatabaseName} in '{Root.FullName}' is empty: nothing was ever written into "
                + "it. 'migrate' makes one there.");
        }
    }

    /// <summary>
    /// Throws away a <c>corpus.db</c> with nothing in it, which is what a create that was cut off
    /// leaves behind.
    /// </summary>
    /// <remarks>
    /// SQLite will not put an empty file into WAL — the attempt comes back as <c>attempt to write
    /// a readonly database</c> — so that file is the one thing that can stop a corpus from ever
    /// being made in this directory, and it holds nothing that deleting it could lose. It is also
    /// the only file this program removes without being asked to.
    /// </remarks>
    private void DiscardEmpty()
    {
        var file = new FileInfo(DatabasePath);
        if (file.Exists && file.Length == 0)
        {
            file.Delete();
        }
    }

    private string Existing()
    {
        EnsureThere();
        return DatabasePath;
    }

    /// <summary>
    /// Refuses a corpus this build's schema has moved past. The alternative is a command failing
    /// against a missing column, which names the column and not the thing to do about it.
    /// </summary>
    private CorpusDbContext Current(CorpusDbContext context)
    {
        var pending = Pending(context);
        if (pending.Count == 0)
        {
            return context;
        }

        context.Dispose();
        throw new CommandException(
            $"This corpus is {pending.Count} migration(s) behind this build, the first being "
            + $"'{pending[0]}'. 'migrate' brings it up.");
    }

    /// <summary>
    /// Names the file when SQLite will not have it.
    /// </summary>
    /// <remarks>
    /// A <c>corpus.db</c> that is not a database — half a copy, a disk that filled part way
    /// through one — comes back as <c>file is not a database</c>, and a corpus this user may not
    /// write to comes back as something equally short. SQLite's own words are kept, because this
    /// program cannot tell those two apart and guessing would send somebody to look at the wrong
    /// thing. What it can add is which file, which is the part the message never has.
    /// </remarks>
    private T Answered<T>(Func<T> ask)
    {
        try
        {
            return ask();
        }
        catch (SqliteException refused)
        {
            throw new CommandException(
                $"'{DatabasePath}' did not open as a corpus: {refused.Message.TrimEnd()} A file that "
                + "is not a database and a corpus this user may not write to both arrive here.");
        }
    }
}
