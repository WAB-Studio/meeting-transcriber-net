using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The reads the model cannot express — a PRAGMA, an FTS5 match — run on the corpus's own
/// connection.
/// </summary>
/// <remarks>
/// <para>
/// It exists for the connection rather than for the tidiness. A context does not always have one
/// open, so a caller borrowing one has to open it, and whoever opens one owes the close: leaving it
/// open pins the SQLite handle for the life of the context, which for the UI is the whole session.
/// A connection that was already open is left exactly as it was found, because something else is
/// holding it and closing it underneath would take it away mid-use.
/// </para>
/// <para>
/// The command joins the context's transaction when there is one. Microsoft.Data.Sqlite refuses a
/// command that does not, so a check run inside a transaction throws instead of reading — and the
/// caller who would have to remember that is the reason this is one function and not three.
/// </para>
/// </remarks>
internal static class RawSql
{
    public static List<T> Rows<T>(
        CorpusDbContext context,
        string sql,
        Func<DbDataReader, T> read,
        Action<DbCommand>? bind = null)
    {
        var database = context.Database;
        var connection = database.GetDbConnection();
        var borrowed = connection.State is not ConnectionState.Open;

        if (borrowed)
        {
            database.OpenConnection();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();
            bind?.Invoke(command);

            var rows = new List<T>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            if (borrowed)
            {
                database.CloseConnection();
            }
        }
    }

    /// <summary>One text parameter on a command.</summary>
    public static void Bind(DbCommand command, string name, string value) => Add(command, name, value);

    /// <summary>One integer parameter on a command.</summary>
    public static void Bind(DbCommand command, string name, int value) => Add(command, name, value);

    /// <summary>One identity parameter on a command, written the way every id column stores one.</summary>
    public static void Bind(DbCommand command, string name, Guid value) => Add(command, name, value);

    /// <summary>
    /// One parameter on a command, because what the caller has is a <see cref="DbCommand"/> and
    /// every raw read here binds rather than interpolates.
    /// </summary>
    /// <remarks>
    /// Private, with a typed overload per thing a caller actually binds, so that a
    /// <see cref="DateTime"/> cannot be handed to it. The provider would write one as
    /// <c>yyyy-MM-dd HH:mm:ss.FFFFFFF</c> while every instant in the corpus is stored as
    /// <c>UtcTimestamp.ToStorage</c> writes it, and the comparison would match nothing at all
    /// without failing — which is the one way a raw read here can be quietly wrong. An instant that
    /// has to be bound gets a fourth overload taking <c>UtcTimestamp</c>, not a widening of this
    /// one.
    /// </remarks>
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
