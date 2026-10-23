using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The parents' aggregates this table's rows feed (<see cref="RollUpAttribute{TChild}" />), kept by every row write: the rows' contribution
///     subtracted before a write that changes or removes them, added after one that writes them, both read from the table by the rows' keys,
///     in the write's transaction (one of their own when none is open).
/// </summary>
public sealed partial class SqlWriter<T>
{
    private SqlRollUp[]? _rollUps;

    /// <summary>The roll-ups of this table, looked up on first use (the parents' maps register them at module start).</summary>
    private SqlRollUp[] RollUps => _rollUps ??= SqlRollUp.For(_table);

    /// <summary>True when the table feeds a roll-up: a write is then several statements, which the caller's transaction or one of its own holds together.</summary>
    private bool HasRollUps(SqlSession session) => RollUps.Length > 0 && session.Transaction is null;

    /// <summary>True when a save of these columns moves a roll-up: the foreign key or the summed column among them.</summary>
    private bool MovesRollUps(ulong mask)
    {
        foreach (var rollUp in RollUps)
        {
            for (var c = 0; c < _columns.Length; c++)
            {
                if ((mask & (1UL << c)) != 0 && (_columns[c] == rollUp.ForeignKey || _columns[c] == rollUp.Amount || _columns[c] == rollUp.LiveColumn))
                    return true;
            }
        }

        return false;
    }

    private Task RollUpAsync(SqlSession session, bool add, T row, CancellationToken ct) => RollUpAsync(session, add, [row], 0, 1, ct);

    /// <summary>The roll-ups moved by the contribution of <paramref name="count" /> rows from <paramref name="start" />, as the table holds them now: one statement per roll-up and lot of keys.</summary>
    private async Task RollUpAsync(SqlSession session, bool add, IReadOnlyList<T> rows, int start, int count, CancellationToken ct)
    {
        var rollUps = RollUps;
        var lot = Math.Min(LargestBatch, SqlServerParameterBudget / _keyIndexes.Length);
        for (var at = start; at < start + count; at += lot)
        {
            var size = Math.Min(lot, start + count - at);
            for (var u = 0; u < rollUps.Length; u++)
            {
                var plan = RollUpPlan(session.Dialect, u, add, size);
                var command = plan.Id >= 0 ? session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds) : session.FreshCommand(plan.Sql, plan.Kinds);
                try
                {
                    for (var i = 0; i < size; i++)
                        _bindKeys(rows[at + i], command.Parameters, i * _keyIndexes.Length, session.Dialect);
                    await session.RunAsync(command, "roll-up \"" + rollUps[u].ParentTable + "\"", ct).ConfigureAwait(false);
                }
                finally
                {
                    if (plan.Id < 0)
                        command.Dispose();
                }
            }
        }
    }

    /// <summary>The statement of one roll-up over <paramref name="rows" /> keys: prepared and kept for a single row, built for the call otherwise.</summary>
    private WritePlan RollUpPlan(SqlDialect dialect, int rollUp, bool add, int rows)
    {
        if (rows == 1)
            return Plan(dialect, (add ? "rollup+:" : "rollup-:") + rollUp.ToString(CultureInfo.InvariantCulture), d => RollUps[rollUp].Statement(d, add, KeysCondition(d, 1)), KeyKinds());
        return new WritePlan(-1, RollUps[rollUp].Statement(dialect, add, KeysCondition(dialect, rows)), Repeat(KeyKinds(), rows));
    }

    /// <summary>The rows by key, their keys bound from <c>@p0</c> row after row: <c>"Id" IN (@p0, @p1)</c>, or one conjunction per row for a composite key.</summary>
    private string KeysCondition(SqlDialect dialect, int rows)
    {
        var sql = new StringBuilder();
        if (_keyIndexes.Length == 1)
        {
            sql.Append(SqlDialects.Quote(dialect, _columns[_keyIndex])).Append(" IN (");
            for (var i = 0; i < rows; i++)
                sql.Append(i > 0 ? ", @p" : "@p").Append(i.ToString(CultureInfo.InvariantCulture));
            return sql.Append(')').ToString();
        }

        for (var r = 0; r < rows; r++)
        {
            sql.Append(r > 0 ? " OR (" : "(");
            for (var k = 0; k < _keyIndexes.Length; k++)
                sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append((r * _keyIndexes.Length + k).ToString(CultureInfo.InvariantCulture));
            sql.Append(')');
        }

        return sql.ToString();
    }

    /// <summary>The roll-up statements of one row in a batch: before its write (<paramref name="add" /> false) or after it.</summary>
    private List<SqlWriteItem> RollUpItems(T row, bool add, SqlDialect dialect)
    {
        var items = new List<SqlWriteItem>(RollUps.Length);
        for (var u = 0; u < RollUps.Length; u++)
        {
            var plan = RollUpPlan(dialect, u, add, 1);
            items.Add(new SqlWriteItem(RollUps[u].ParentTable, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => _bindKeys(row, p, o, d), expected: -1));
        }

        return items;
    }

    /// <summary>The roll-ups less the rows a condition selects (the orphans of a graph, a cascade's rows), before their DELETE, with the condition's own parameters.</summary>
    private List<SqlWriteItem> RollUpWhereItems(Func<SqlDialect, string> where, SqlParamKind[] kinds, Action<DbParameterCollection, int, SqlDialect> bind)
    {
        var items = new List<SqlWriteItem>(RollUps.Length);
        foreach (var rollUp in RollUps)
            items.Add(new SqlWriteItem(rollUp.ParentTable, -1, kinds, d => rollUp.Statement(d, add: false, where(d)), bind, expected: -1));
        return items;
    }

    IReadOnlyList<SqlWriteItem> ISqlWriter.RollUpItems(object row, bool add, SqlDialect dialect) => RollUps.Length == 0 ? [] : RollUpItems((T)row, add, dialect);

    bool ISqlWriter.SaveMovesRollUps(object row) => RollUps.Length > 0 && MovesRollUps(MaskOf(((IChangeTracking)row).ModifiedProperties));

    IReadOnlyList<SqlWriteItem> ISqlWriter.OrphansRollUps(string[] foreignKeyColumns, SqlParamKind[] parentKeyKinds, object?[] parentKey, IReadOnlyList<object?> keptKeys)
    {
        if (RollUps.Length == 0)
            return [];
        var (kinds, bind) = OrphansParameters(parentKeyKinds, parentKey, keptKeys);
        return RollUpWhereItems(d => OrphansWhere(d, foreignKeyColumns, keptKeys.Count), kinds, bind);
    }

    IReadOnlyList<SqlWriteItem> ISqlWriter.DeleteWhereRollUps(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey)
        => RollUps.Length == 0 ? [] : RollUpWhereItems(where, parentKeyKinds, BindParentKey(parentKey));
}
