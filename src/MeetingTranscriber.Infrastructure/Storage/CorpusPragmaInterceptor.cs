using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// Applies the pragmas the design depends on to every connection, including the ones EF opens
/// and closes on its own. Setting them once after opening a context would not survive that.
/// </summary>
internal sealed class CorpusPragmaInterceptor(bool readOnly) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void Apply(DbConnection connection)
    {
        // SQLite has foreign keys off per connection by default. Every constraint the schema
        // declares is worth nothing until this runs.
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, $"PRAGMA busy_timeout = {CorpusDatabase.BusyTimeoutMilliseconds};");

        if (!readOnly)
        {
            // WAL lets the UI and the MCP server read while a job writes. It is a property of
            // the file, so setting it again on a corpus that already has it costs nothing.
            Execute(connection, "PRAGMA journal_mode = WAL;");
        }
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
