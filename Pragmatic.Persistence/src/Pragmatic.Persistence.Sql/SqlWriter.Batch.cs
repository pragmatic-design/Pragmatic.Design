using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>The writer's statements as items of a <see cref="SqlWriteBatch" />: the same prepared plans, one row each, finished when the batch succeeds.</summary>
public sealed partial class SqlWriter<T> : ISqlWriter
{
    internal SqlWriteItem InsertItem(T row, SqlDialect dialect, bool stamp = true)
    {
        if (stamp)
            StampInsert(row);
        var native = Native(dialect); // the database's version, read back from the statement's own row
        var plan = Plan(dialect, "insert:1", d => Insert(d, 1), InsertKinds(dialect));
        var bind = InsertBinder(dialect);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => bind(row, p, o, d), expected: 1)
        {
            ReturnsRows = _identity is not null || native,
            Row = native ? (r, d) => _readVersion!(row, r, d) : _identity is null ? null : (r, d) => _identity.ReadKey(row, r, d),
            Completed = row is IChangeTracking tracking ? () => Saved(tracking) : null,
            ReadBackBy = _generated is null ? null : this,
            ReadBackRow = row,
        };
    }

    /// <summary>The row's modified columns, at its version; null when nothing is modified.</summary>
    internal SqlWriteItem? SaveItem<TTracked>(TTracked row, SqlDialect dialect) where TTracked : class, T, IChangeTracking
    {
        RefuseNative(dialect);
        EnsureSaves();
        var mask = MaskOf(row.ModifiedProperties);
        if (mask == 0)
            return null;
        StampUpdate(row);
        var plan = SavePlan(dialect, mask, 1, out _);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => BindSave(row, mask, p, o, d), expected: 1)
        {
            Completed = () =>
            {
                _bumpVersion?.Invoke(row);
                Saved(row);
            },
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(row), _version!(row)) : null,
            ReadBackBy = _generated?.AfterUpdate == true ? this : null,
            ReadBackRow = row,
        };
    }

    internal SqlWriteItem UpdateItem(T row, SqlDialect dialect)
    {
        RefuseNative(dialect);
        StampUpdate(row);
        var plan = Plan(dialect, "update", Update, _kinds);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => _bindRow(row, p, o, d), expected: 1)
        {
            Completed = _bumpVersion is null ? null : () => _bumpVersion(row),
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(row), _version!(row)) : null,
            ReadBackBy = _generated?.AfterUpdate == true ? this : null,
            ReadBackRow = row,
        };
    }

    internal SqlWriteItem DeleteItem(T row, SqlDialect dialect)
    {
        if (SoftDeletes)
            return SoftDeleteItem(row, dialect); // [SoftDelete]: the row stays, marked
        return HardDeleteItem(row, dialect);
    }

    /// <summary>The row's DELETE by key (and version), whatever its soft delete.</summary>
    private SqlWriteItem HardDeleteItem(T row, SqlDialect dialect)
    {
        var plan = Plan(dialect, "delete", Delete, DeleteKinds());
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) =>
        {
            _bindKeys(row, p, o, d);
            if (_versionIndex >= 0)
                BindVersion(row, p, o + _keyIndexes.Length, d);
        }, expected: 1)
        {
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(row), _version!(row)) : null,
        };
    }

    SqlWriteItem ISqlWriter.InsertItem(object row, SqlDialect dialect) => InsertItem((T)row, dialect);

    SqlWriteItem? ISqlWriter.SaveItem(object row, SqlDialect dialect)
    {
        // The row is a T that tracks its changes: the typed save, through a boxed path the graph walker takes (it knows the type as a Type).
        RefuseNative(dialect);
        EnsureSaves();
        var tracking = (IChangeTracking)row;
        var mask = MaskOf(tracking.ModifiedProperties);
        if (mask == 0)
            return null;
        var typed = (T)row;
        StampUpdate(typed);
        var plan = SavePlan(dialect, mask, 1, out _);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => BindSave(typed, mask, p, o, d), expected: 1)
        {
            Completed = () =>
            {
                _bumpVersion?.Invoke(typed);
                Saved(tracking);
            },
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(typed), _version!(typed)) : null,
            ReadBackBy = _generated?.AfterUpdate == true ? this : null,
            ReadBackRow = typed,
        };
    }

    SqlWriteItem ISqlWriter.UpdateItem(object row, SqlDialect dialect) => UpdateItem((T)row, dialect);

    SqlWriteItem ISqlWriter.DeleteItem(object row, SqlDialect dialect) => DeleteItem((T)row, dialect);

    SqlWriteItem ISqlWriter.HardDeleteItem(object row, SqlDialect dialect) => HardDeleteItem((T)row, dialect);

    Task ISqlWriter.UpsertAsync(SqlSession session, object row, CancellationToken ct) => UpsertAsync(session, (T)row, ct);

    Task ISqlWriter.ReadGeneratedAsync(SqlSession session, IReadOnlyList<object> rows, CancellationToken ct)
    {
        var typed = new T[rows.Count];
        for (var i = 0; i < typed.Length; i++)
            typed[i] = (T)rows[i];
        return ReadGeneratedAsync(session, typed, 0, typed.Length, ct);
    }

    SqlWriteItem ISqlWriter.OrphansItem(string[] foreignKeyColumns, SqlParamKind[] parentKeyKinds, object?[] parentKey, IReadOnlyList<object?> keptKeys)
    {
        var (kinds, bind) = OrphansParameters(parentKeyKinds, parentKey, keptKeys);
        if (SoftDeletes)
            return SoftDeleteWhere(dialect => OrphansWhere(dialect, foreignKeyColumns, keptKeys.Count), kinds, bind); // [SoftDelete]: the orphans stay, marked
        return new SqlWriteItem(_table, -1, kinds, dialect => "DELETE FROM " + SqlDialects.Quote(dialect, _table) + " WHERE " + OrphansWhere(dialect, foreignKeyColumns, keptKeys.Count), bind, expected: -1);
    }

    /// <summary>The rows under the parent (its key from <c>@p0</c>, a column per key column) whose key is not among the kept ones (after it).</summary>
    private string OrphansWhere(SqlDialect dialect, string[] foreignKeyColumns, int kept)
    {
        var sql = new StringBuilder();
        for (var k = 0; k < foreignKeyColumns.Length; k++)
            sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, foreignKeyColumns[k])).Append(" = @p").Append(k.ToString(CultureInfo.InvariantCulture));
        if (kept > 0)
        {
            sql.Append(" AND ").Append(SqlDialects.Quote(dialect, _columns[_keyIndex])).Append(" NOT IN (");
            for (var i = 0; i < kept; i++)
                sql.Append(i > 0 ? ", @p" : "@p").Append((foreignKeyColumns.Length + i).ToString(CultureInfo.InvariantCulture));
            sql.Append(')');
        }

        return sql.ToString();
    }

    /// <summary>The parent's key (its columns), then the kept children's keys: the parameters of an orphans' condition.</summary>
    private (SqlParamKind[] Kinds, Action<DbParameterCollection, int, SqlDialect> Bind) OrphansParameters(SqlParamKind[] parentKeyKinds, object?[] parentKey, IReadOnlyList<object?> keptKeys)
    {
        if (_keyIndexes.Length != 1)
            throw new NotSupportedException($"\"{_table}\" has a composite key: the children of a saved graph are told apart by a single-column key.");
        var keyKind = _kinds[_keyIndex];
        var width = parentKeyKinds.Length;
        var kinds = new SqlParamKind[width + keptKeys.Count];
        parentKeyKinds.CopyTo(kinds, 0);
        for (var i = 0; i < keptKeys.Count; i++)
            kinds[width + i] = keyKind;
        return (kinds, (p, o, d) =>
        {
            for (var k = 0; k < width; k++)
                p[o + k].Value = SqlValue.ToDatabase(d, parentKey[k]);
            for (var i = 0; i < keptKeys.Count; i++)
                p[o + width + i].Value = SqlValue.ToDatabase(d, keptKeys[i]);
        });
    }

    /// <summary>The parent's key bound from an offset: one parameter per key column.</summary>
    internal static Action<DbParameterCollection, int, SqlDialect> BindParentKey(object?[] parentKey) => (p, o, d) =>
    {
        for (var k = 0; k < parentKey.Length; k++)
            p[o + k].Value = SqlValue.ToDatabase(d, parentKey[k]);
    };

    int ISqlWriter.Warm(SqlSession session)
    {
        var warmed = 0;
        foreach (var plan in new[] { Plan(session.Dialect, "insert:1", d => Insert(d, 1), _insertKinds), Plan(session.Dialect, "update", Update, _kinds), Plan(session.Dialect, "delete", Delete, DeleteKinds()) })
        {
            if (session.CommandFor(plan.Id) is not null)
                continue;
            session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
            warmed++;
        }

        return warmed;
    }

    SqlWriteItem ISqlWriter.DeleteWhereItem(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey)
        => new(_table, -1, parentKeyKinds, dialect => $"DELETE FROM {SqlDialects.Quote(dialect, _table)} WHERE {where(dialect)}", BindParentKey(parentKey), expected: -1);
}
