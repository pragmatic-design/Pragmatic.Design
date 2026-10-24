using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     <c>SyncAsync</c>: the generated statement carries the rows' VALUES as a placeholder (<c>{rows}</c>) and, on SQLite, the keys of the
///     rows as another (<c>{keys}</c>), both expanded here per call from the rows' count; the chain's parameters come first (its predicates
///     say which rows are the set), the rows' values after, bound by the entity's generated writer in its columns' order.
/// </summary>
public sealed partial class SqlPlan<T>
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> SyncAsync(SqlQuery<T> query, IReadOnlyList<T> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            throw new ArgumentException("SyncAsync takes the rows the set becomes; to empty the set, ExecuteDeleteAsync says it.", nameof(rows));
        // INVESTIGATE: a sync's deletes read the chain's rows; with filters they would need the caller's variant and its values. Refused until then.
        if (Dynamic)
            throw new NotSupportedException("SyncAsync over Pragmatic's filtered entities is not supported yet: the statement depends on the caller's filters.");
        var session = query.Session;
        var dialect = session.Dialect;
        var writer = SqlEntityMap<T>.Current.Writer ?? throw new InvalidOperationException($"No writer is registered for {typeof(T).Name}.");
        var columns = writer.Columns;
        var width = columns.Length;
        var first = Parameters.Length;

        // The VALUES tuples and the keys list, named @pN from the chain's parameters on; the key is the first key column (one-column keys: the generator refuses the others).
        var values = new StringBuilder();
        var keys = new StringBuilder();
        var keyIndex = Array.IndexOf(columns, SqlEntityMap<T>.Current.KeyColumn);
        for (var r = 0; r < rows.Count; r++)
        {
            values.Append(r > 0 ? ", (" : "(");
            for (var c = 0; c < width; c++)
                values.Append(c > 0 ? ", @p" : "@p").Append((first + r * width + c).ToString(CultureInfo.InvariantCulture));
            values.Append(')');
            keys.Append(r > 0 ? ", @p" : "@p").Append((first + r * width + keyIndex).ToString(CultureInfo.InvariantCulture));
        }

        var sql = Sql(dialect).Replace("{rows}", values.ToString()).Replace("{keys}", keys.ToString());
        // MySQL counts an updated row of INSERT … ON DUPLICATE KEY UPDATE twice: the actions are the rows given plus the rows deleted, read after the delete.
        // Oracle: the MERGE and the DELETE in one block; the actions counted the same way (the rows given, merged one each, plus the rows deleted).
        if (dialect is SqlDialect.MySql or SqlDialect.Oracle)
            sql += "; SELECT ROW_COUNT()";
        var kinds = new SqlParamKind[first + rows.Count * width];
        Parameters.CopyTo(kinds, 0);
        for (var r = 0; r < rows.Count; r++)
            writer.Kinds.CopyTo(kinds, first + r * width);

        using var command = session.FreshCommand(sql, kinds);
        Bind(query, command.Parameters, dialect);
        for (var r = 0; r < rows.Count; r++)
            writer.BindRow(rows[r], command.Parameters, first + r * width, dialect);
        var execution = SqlTelemetry.Begin(session, Origin, command);
        Exception? failure = null;
        try
        {
            if (dialect == SqlDialect.MySql)
                return rows.Count + SqlValue.Count(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (dialect == SqlDialect.Oracle)
                return rows.Count + await SqlOracle.ExecuteCountAsync(command, ct).ConfigureAwait(false);
            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            failure = e;
            throw;
        }
        finally
        {
            SqlTelemetry.End(execution, session, Origin, failure);
        }
    }
}
