using System.Data;

using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Testing;

/// <summary>
/// Constraints are asserted through raw SQL on purpose. What is under test is what the database
/// refuses, not what the model would have stopped before getting there.
/// </summary>
public static class Sql
{
    public static void Execute(CorpusDbContext context, string sql) => context.Database.ExecuteSqlRaw(sql);

    public static object? Scalar(CorpusDbContext context, string sql)
    {
        using var command = Command(context, sql);
        return command.ExecuteScalar();
    }

    public static List<string> Strings(CorpusDbContext context, string sql)
    {
        using var command = Command(context, sql);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>
    /// Every row of a table as text, columns and rows both in a stable order, for comparing a table
    /// against itself across an operation that was supposed to leave it alone.
    /// </summary>
    /// <remarks>
    /// The columns are read out of the table rather than listed by the caller, so a column added
    /// later is compared without anybody remembering to add it here — which is the whole point,
    /// since the failure being looked for is a column of the human layer quietly being deleted.
    /// </remarks>
    public static List<string> Rows(CorpusDbContext context, string table)
    {
        var columns = Strings(context, $"SELECT name FROM pragma_table_info('{table}') ORDER BY name;");
        var fields = columns.Select(column => $"coalesce(cast(\"{column}\" AS TEXT), '<null>')");
        var row = string.Join(" || '|' || ", fields);

        return Strings(context, $"SELECT {row} AS stored FROM \"{table}\" ORDER BY stored;");
    }

    /// <summary>
    /// Every table the corpus holds, leaving out SQLite's own and the shadow tables an external
    /// content FTS5 index keeps beside itself.
    /// </summary>
    public static List<string> Tables(CorpusDbContext context) => Strings(
        context,
        """
        SELECT name FROM sqlite_master
        WHERE type = 'table' AND name NOT LIKE 'sqlite\_%' ESCAPE '\' AND name NOT LIKE '%fts%'
        ORDER BY name;
        """);

    private static IDbCommand Command(CorpusDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State is not ConnectionState.Open)
        {
            context.Database.OpenConnection();
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
