using System.Data;
using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// One thing found wrong with a corpus, named so somebody can go and look at it.
/// </summary>
/// <param name="Check">Which check said so, so a report can be read without knowing this file.</param>
/// <param name="Table">
/// What is wrong, when the check says. <c>PRAGMA integrity_check</c> answers in prose and often
/// names nothing, which is why this is nullable rather than filled in with a guess.
/// </param>
public sealed record CorpusProblem(string Check, string? Table, string Detail)
{
    public override string ToString() =>
        Table is null ? $"{Check}: {Detail}" : $"{Check}: {Table}: {Detail}";
}

/// <summary>A corpus that is not sound, and everything found wrong with it.</summary>
public sealed class CorpusIntegrityException(IReadOnlyList<CorpusProblem> problems)
    : Exception($"The corpus is not sound:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}")
{
    public IReadOnlyList<CorpusProblem> Problems { get; } = problems;
}

/// <summary>
/// Whether a corpus is sound, and the one safe way to compact it.
/// </summary>
/// <remarks>
/// Runnable by hand, and the thing anything that copies the corpus has to pass first: a backup of
/// a corpus that was already wrong is a backup of being wrong, restored later with confidence.
/// </remarks>
public static class CorpusIntegrity
{
    /// <summary>
    /// The external content indexes, which are derived and can always be thrown away and rebuilt.
    /// </summary>
    public static IReadOnlyList<string> SearchIndexes { get; } = ["utterances_fts", "summaries_fts"];

    /// <summary>
    /// Everything wrong with this corpus, empty when there is nothing. It reports rather than
    /// throws, because the answer worth having is the whole list and not the first line of it.
    /// </summary>
    public static IReadOnlyList<CorpusProblem> Check(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problems = new List<CorpusProblem>();
        problems.AddRange(FileIsIntact(context));
        problems.AddRange(EveryReferencePointsAtSomething(context));
        problems.AddRange(SearchMatchesWhatItIndexes(context));
        return problems;
    }

    /// <summary>Throws unless the corpus is sound. What a backup calls before it starts.</summary>
    public static void Ensure(CorpusDbContext context)
    {
        var problems = Check(context);
        if (problems.Count > 0)
        {
            throw new CorpusIntegrityException(problems);
        }
    }

    /// <summary>
    /// Compacts the corpus and puts the search indexes back, in that order and never one without
    /// the other.
    /// </summary>
    /// <remarks>
    /// This exists so that no caller has to remember the second half. SQLite is free to renumber
    /// the rowids of a table with no INTEGER PRIMARY KEY while vacuuming, and both indexes key on
    /// the rowid of a table that has none — so a bare VACUUM is allowed to leave search answering
    /// with the wrong rows, and it would not raise anything on the way. Today's SQLite happens to
    /// keep them, which is the worst version of the problem: it is not a bug that shows up while
    /// anybody is looking for it.
    /// </remarks>
    public static void Compact(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Execute(context, "VACUUM;");
        RebuildSearchIndexes(context);
    }

    /// <summary>
    /// Builds both indexes again from the tables they index. Nothing is lost by it: the text lives
    /// in those tables and the index holds no copy of its own.
    /// </summary>
    public static void RebuildSearchIndexes(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var index in SearchIndexes)
        {
            Execute(context, $"INSERT INTO {index} ({index}) VALUES ('rebuild');");
        }
    }

    /// <summary>Whether the file itself still reads as a database.</summary>
    private static IEnumerable<CorpusProblem> FileIsIntact(CorpusDbContext context) => Rows(
            context,
            "PRAGMA integrity_check;",
            reader => reader.GetString(0))
        .Where(line => !string.Equals(line, "ok", StringComparison.Ordinal))
        .Select(line => new CorpusProblem("integrity_check", null, line));

    /// <summary>
    /// Orphans. SQLite has foreign keys off per connection, so a corpus written by anything that
    /// forgot to turn them on holds rows the schema says are impossible, and every one of them is
    /// still sitting there afterwards.
    /// </summary>
    private static IEnumerable<CorpusProblem> EveryReferencePointsAtSomething(CorpusDbContext context) => Rows(
        context,
        "PRAGMA foreign_key_check;",
        reader => new CorpusProblem(
            "foreign_key_check",
            reader.GetString(0),
            reader.IsDBNull(1)
                ? $"a row points at nothing in '{reader.GetString(2)}'"
                : $"row {reader.GetInt64(1)} points at nothing in '{reader.GetString(2)}'"));

    /// <summary>
    /// Whether each search index still agrees with the table it indexes.
    /// </summary>
    /// <remarks>
    /// The argument is the whole check. Without it — <c>VALUES ('integrity-check')</c> — FTS5 only
    /// verifies that the index is internally consistent, which an index built against the wrong
    /// rows is: delete one row's terms behind its back and the bare form still answers ok while
    /// search quietly stops returning it. Passing 1 is what compares the index against its content
    /// table, and that is the failure this whole check exists for.
    /// </remarks>
    private static IEnumerable<CorpusProblem> SearchMatchesWhatItIndexes(CorpusDbContext context)
    {
        foreach (var index in SearchIndexes)
        {
            SqliteException? failure = null;
            try
            {
                Execute(context, $"INSERT INTO {index} ({index}, rank) VALUES ('integrity-check', 1);");
            }
            catch (SqliteException corrupt)
            {
                failure = corrupt;
            }

            if (failure is not null)
            {
                yield return new CorpusProblem(
                    "fts_integrity_check",
                    index,
                    $"the index does not match the table it indexes ({failure.Message.TrimEnd('.')}); "
                    + "rebuilding it is safe and is what fixes this");
            }
        }
    }

    private static void Execute(CorpusDbContext context, string sql) =>
        context.Database.ExecuteSqlRaw(sql);

    private static List<T> Rows<T>(CorpusDbContext context, string sql, Func<DbDataReader, T> read)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State is not ConnectionState.Open)
        {
            context.Database.OpenConnection();
        }

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(read(reader));
        }

        return rows;
    }
}
