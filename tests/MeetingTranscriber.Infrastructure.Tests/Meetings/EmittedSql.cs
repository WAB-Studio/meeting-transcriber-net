using System.Diagnostics;

using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeetingTranscriber.Infrastructure.Tests.Meetings;

/// <summary>
/// What a corpus was really asked: every command EF sent it while this was listening, and how
/// many times each answer was pulled from.
/// </summary>
/// <remarks>
/// <para>
/// It listens rather than being wired in, so nothing about the application changes to be watched
/// and what comes back is the query as it ran through the call a screen makes. That is the point
/// of it: a reader that stopped narrowing in the database would still hand back the right
/// meetings, so the answer cannot be what says it went wrong.
/// </para>
/// <para>
/// Open it before the corpus. EF settles once per context whether anybody is listening and holds
/// that answer for about a second, so a listener that arrives after the first command hears
/// nothing until the window passes. A test that gets the order wrong finds no command rather than
/// a wrong one, and <see cref="Reading"/> says so in as many words.
/// </para>
/// <para>
/// Only this corpus. EF's diagnostic source is one per process and test classes run alongside each
/// other, so a command is kept only when it was sent to the database file this was opened over.
/// That is for attribution and not against going deaf — the suppression above is per context, so a
/// corpus next door cannot silence this one. What it does cost the tests running alongside is that
/// EF takes its logging path for every corpus in the process while this lives, which changes what
/// they allocate and nothing about what they assert.
/// </para>
/// <para>
/// It lives here, beside its one caller, rather than in <c>MeetingTranscriber.Testing</c>: that
/// project holds what a test opens, and this is the first thing that would decide whether
/// something is wrong. It moves there when a second suite needs it.
/// </para>
/// </remarks>
public sealed class EmittedSql : IDisposable
{
    /// <summary>EF's own diagnostic source, which every provider writes to.</summary>
    private const string EntityFramework = "Microsoft.EntityFrameworkCore";

    private readonly Lock guard = new();
    private readonly List<Command> commands = [];
    private readonly Dictionary<Guid, Command> byId = [];
    private readonly List<IDisposable> subscriptions = [];
    private readonly string database;
    private bool stopped;

    private EmittedSql(string database)
    {
        this.database = database;
        Keep(DiagnosticListener.AllListeners.Subscribe(new Listeners(this)));
    }

    /// <summary>Starts listening to the corpus in this folder. Stops on dispose.</summary>
    public static EmittedSql Over(DirectoryInfo root) =>
        new(Path.GetFullPath(CorpusDatabase.PathIn(root)));

    /// <summary>
    /// Drops everything heard so far, which is how a test separates building a corpus from the one
    /// call it is about.
    /// </summary>
    public void Forget()
    {
        lock (guard)
        {
            commands.Clear();
            byId.Clear();
        }
    }

    /// <summary>
    /// The one command that read this table, which is how a caller names the query it is about
    /// without depending on what ran around it.
    /// </summary>
    /// <remarks>
    /// Insisting on exactly one is part of the answer: a reader split into two statements over the
    /// same table is not the shape this was written against, and quietly asserting over the first
    /// of them would be reading the wrong query. It matches the table anywhere in the command, a
    /// subquery included, so it names a statement and not a top-level source.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing read it, or more than one thing did.</exception>
    public Asked Reading(string table)
    {
        var from = $"FROM \"{table}\"";

        lock (guard)
        {
            var matched = commands.Where(command => command.Sql.Contains(from, StringComparison.Ordinal)).ToList();

            if (matched.Count == 1)
            {
                return new Asked(matched[0].Sql, matched[0].Reads);
            }

            throw new InvalidOperationException(
                $"Expected one command reading {table} and heard {matched.Count} of the "
                + $"{commands.Count} sent to this corpus."
                + (commands.Count == 0
                    ? " Nothing at all was heard: this has to be opened before the corpus is."
                    : string.Concat(matched.Select(command => $"{Environment.NewLine}{command.Sql}"))));
        }
    }

    public void Dispose()
    {
        lock (guard)
        {
            stopped = true;
            subscriptions.ForEach(subscription => subscription.Dispose());
            subscriptions.Clear();
        }
    }

    /// <summary>One command as it was sent, and what came back, as it stood when it was asked for.</summary>
    /// <param name="Sql">The text EF emitted, with parameters left as placeholders.</param>
    /// <param name="Reads">
    /// How many times the answer was pulled from, once its reader was closed. A query read to its
    /// end reports one more than the rows it returned, because the pull that finds the end counts
    /// too — so it is worth comparing against another number and not read as a total.
    /// </param>
    public sealed record Asked(string Sql, int Reads);

    /// <summary>
    /// A command while it is still being heard about, since the text arrives on one event and the
    /// count on another. Nothing outside gets a reference to it.
    /// </summary>
    private sealed class Command(string sql)
    {
        public string Sql { get; } = sql;

        public int Reads { get; set; }
    }

    private void Keep(IDisposable subscription)
    {
        lock (guard)
        {
            // A listener can arrive on another thread after this was disposed. Storing it then
            // would leave something subscribed to a process-global source with nobody to stop it.
            if (stopped)
            {
                subscription.Dispose();
                return;
            }

            subscriptions.Add(subscription);
        }
    }

    private bool Mine(string? dataSource) =>
        dataSource is not null
        && string.Equals(Path.GetFullPath(dataSource), database, StringComparison.OrdinalIgnoreCase);

    private void Heard(Guid id, string sql)
    {
        lock (guard)
        {
            var command = new Command(sql);
            commands.Add(command);
            byId[id] = command;
        }
    }

    private void Counted(Guid id, int reads)
    {
        lock (guard)
        {
            if (byId.TryGetValue(id, out var command))
            {
                command.Reads = reads;
            }
        }
    }

    /// <summary>Picks EF's listener out of every diagnostic source in the process.</summary>
    private sealed class Listeners(EmittedSql owner) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name is EntityFramework)
            {
                owner.Keep(listener.Subscribe(new Events(owner)));
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    /// <summary>The two ends of a query: the command that went out, and the reader closing on it.</summary>
    private sealed class Events(EmittedSql owner) : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            switch (value.Value)
            {
                case CommandExecutedEventData executed
                    when owner.Mine(executed.Command.Connection?.DataSource):
                    owner.Heard(executed.CommandId, executed.Command.CommandText);
                    break;

                case DataReaderDisposingEventData done when owner.Mine(done.Command.Connection?.DataSource):
                    owner.Counted(done.CommandId, done.ReadCount);
                    break;
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
