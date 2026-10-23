using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A provider's bulk load, behind <see cref="SqlSession.InsertManyAsync{T}" />: above <see cref="SqlSession.BulkThreshold" /> rows, a
///     session with a copier hands it the table, the columns, the kinds and the rows' values (converted as the parameters would be)
///     instead of running multi-row INSERT statements. This assembly references no provider: the copiers live with them
///     (<c>Pragmatic.Persistence.Sql.Npgsql</c>: <c>COPY … FROM STDIN (FORMAT BINARY)</c>; <c>Pragmatic.Persistence.Sql.SqlClient</c>: <c>SqlBulkCopy</c>).
/// </summary>
public interface ISqlBulkCopier
{
    /// <summary>Loads the rows into <paramref name="table" /> (of <paramref name="schema" />, the session's, when one is set); the count the database took. Inside <paramref name="transaction" /> when one is open on the session.</summary>
    Task<int> CopyAsync(DbConnection connection, DbTransaction? transaction, string? schema, string table, string[] columns, SqlParamKind[] kinds, IReadOnlyList<object?[]> rows, CancellationToken ct);
}
