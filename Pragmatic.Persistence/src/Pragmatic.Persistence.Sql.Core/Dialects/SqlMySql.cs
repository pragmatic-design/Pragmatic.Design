using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Dialects;

/// <summary>
///     What a MySQL connection needs before this library speaks to it: the session's <c>sql_mode</c> with ANSI_QUOTES (double-quoted
///     identifiers, as every statement here writes them) and PIPES_AS_CONCAT, in strict mode, and the session's time zone at UTC (a
///     <c>DateTimeOffset</c> is stored as a UTC <c>DATETIME(6)</c> and read back as UTC). Run it on every connection the application
///     opens (a <see cref="SqlSessionPool" /> runs it on the connections it opens), or set the same <c>sql_mode</c> and <c>time_zone</c> on the server.
/// </summary>
public static class SqlMySql
{
    /// <summary>The <c>sql_mode</c> the statements are written for.</summary>
    public const string SqlMode = "ANSI_QUOTES,PIPES_AS_CONCAT,ONLY_FULL_GROUP_BY,STRICT_TRANS_TABLES,NO_ENGINE_SUBSTITUTION,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO";

    /// <summary>The statement that sets the session up.</summary>
    public const string PrepareSql = "SET SESSION sql_mode = '" + SqlMode + "', SESSION time_zone = '+00:00'";

    /// <summary>Sets the session's <c>sql_mode</c> and time zone on an open connection.</summary>
    public static void Prepare(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PrepareSql;
        command.ExecuteNonQuery();
    }

    /// <summary>Sets the session's <c>sql_mode</c> and time zone on an open connection.</summary>
    public static async Task PrepareAsync(DbConnection connection, CancellationToken ct = default)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PrepareSql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
