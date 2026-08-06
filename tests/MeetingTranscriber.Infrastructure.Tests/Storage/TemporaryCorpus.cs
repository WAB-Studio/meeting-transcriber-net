using System.Data;

using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// A corpus on disk rather than in memory: WAL and busy_timeout only mean anything against a
/// file, and those are exactly the settings worth testing.
/// </summary>
internal sealed class TemporaryCorpus : IDisposable
{
    private readonly string _directory;

    public TemporaryCorpus()
    {
        _directory = Path.Combine(Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "corpus.db");
    }

    public string DatabasePath { get; }

    public CorpusDbContext Open() => CorpusDatabase.Open(DatabasePath);

    public CorpusDbContext OpenMigrated() => CorpusDatabase.OpenMigrated(DatabasePath);

    public void Dispose()
    {
        // Without this the pooled connection still holds the file and the delete fails. It empties
        // every pool in the process, so it also reaches the corpora of the tests running alongside
        // this one — which costs them a reconnection and nothing else: a connection somebody is
        // holding is not closed underneath them, only kept out of the pool once they hand it back.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over. Windows refuses a
            // delete two ways depending on how the other handle was opened — a sharing violation
            // when it forbids deletion, access denied when it allowed it and the delete is still
            // pending — and catching only the first leaves the test that happened to run while a
            // scanner had the file open failing for something it never asserted.
        }
    }
}

/// <summary>
/// Constraints are asserted through raw SQL on purpose. What is under test is what the database
/// refuses, not what the model would have stopped before getting there.
/// </summary>
internal static class Sql
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
