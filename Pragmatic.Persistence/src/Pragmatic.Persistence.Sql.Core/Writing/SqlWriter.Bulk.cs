using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>The bulk path of <see cref="InsertAsync(SqlSession, IReadOnlyList{T}, CancellationToken)" />: the rows' values, as the insert binder would bind them, handed to the session's copier.</summary>
public sealed partial class SqlWriter<T>
{
    /// <summary>True when the batch goes to the copier: one is set, the batch is at or above the threshold, and the key is not the database's (a bulk load gives no keys back).</summary>
    private static bool Bulk(SqlSession session, int rows, SqlIdentity<T>? identity) => session.BulkCopier is not null && rows >= session.BulkThreshold && identity is null;

    /// <summary>The rows' values converted for the dialect (through the generated binder, into a scratch command's parameters), then copied.</summary>
    private async Task<int> BulkInsertAsync(SqlSession session, IReadOnlyList<T> rows, CancellationToken ct)
    {
        var dialect = session.Dialect;
        using var scratch = session.Connection.CreateCommand();
        for (var i = 0; i < _insertKinds.Length; i++)
        {
            var parameter = scratch.CreateParameter();
            parameter.ParameterName = "@p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SqlValue.Configure(parameter, _insertKinds[i], dialect);
            scratch.Parameters.Add(parameter);
        }

        var values = new object?[rows.Count][];
        for (var r = 0; r < rows.Count; r++)
        {
            _bindInsert(rows[r], scratch.Parameters, 0, dialect);
            var row = new object?[_insertColumns.Length];
            for (var c = 0; c < row.Length; c++)
                row[c] = scratch.Parameters[c].Value;
            values[r] = row;
        }

        return await session.BulkCopier!.CopyAsync(session.Connection, session.Transaction, session.Schema, _table, _insertColumns, _insertKinds, values, ct).ConfigureAwait(false);
    }
}
