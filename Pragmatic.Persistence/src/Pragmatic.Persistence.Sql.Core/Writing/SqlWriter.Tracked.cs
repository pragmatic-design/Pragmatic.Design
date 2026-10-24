namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     Writes driven by a snapshot instead of the entity's own tracking, for a unit of work that keeps what it read: the row's column values
///     as the database takes them (<see cref="Snapshot" />), compared with the row's values now (<see cref="ChangedColumns" />), so a property
///     changed through a public setter is seen too. The statements are the save's (the changed columns at the row's version) and the insert,
///     without the writer's own stamps: the unit of work stamps the row itself, in its phases' order.
/// </summary>
public sealed partial class SqlWriter<T>
{
    private readonly Func<T, int, SqlDialect, object?>? _columnValue;

    /// <summary>The row's column values as they are bound (converted, a JSON column as its text), in <see cref="Columns" />' order.</summary>
    public object?[] Snapshot(T row, SqlDialect dialect)
    {
        var columnValue = _columnValue ?? throw new InvalidOperationException($"The writer of \"{_table}\" has no column reader: the generator that registered it predates snapshots; rebuild the assembly that declares the entity.");
        var values = new object?[_columns.Length];
        for (var c = 0; c < values.Length; c++)
            values[c] = columnValue(row, c, dialect);
        return values;
    }

    /// <summary>The columns whose value differs from the snapshot, a bit each; the keys, the version and the insert-only columns never.</summary>
    public ulong ChangedColumns(T row, object?[] snapshot, SqlDialect dialect)
    {
        EnsureSaves();
        var now = Snapshot(row, dialect);
        ulong mask = 0;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c) || c == _versionIndex || IsInsertOnly(c))
                continue;
            if (!Same(snapshot[c], now[c]))
                mask |= 1UL << c;
        }

        return mask;
    }

    /// <summary>The same value as bound: equal, or two arrays of the same elements (bytes, PostgreSQL's text[] of a list, built anew on every bind).</summary>
    private static bool Same(object? a, object? b)
    {
        if (Equals(a, b))
            return true;
        if (a is byte[] leftBytes && b is byte[] rightBytes)
            return leftBytes.AsSpan().SequenceEqual(rightBytes);
        if (a is not Array left || b is not Array right || left.Length != right.Length || left.GetType() != right.GetType())
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!Equals(left.GetValue(i), right.GetValue(i)))
                return false;
        }

        return true;
    }

    /// <summary>The UPDATE of the masked columns at the row's version, unstamped, as an item of a write batch; the version advanced when it succeeds.</summary>
    private SqlWriteItem ColumnsItem(T row, ulong mask, SqlDialect dialect)
    {
        EnsureSaves();
        var native = Native(dialect); // the database advanced the version: read back from the statement's row, not advanced here
        var plan = SavePlan(dialect, mask, 1, out _);
        return new SqlWriteItem(_table, plan.Id, plan.Kinds, d => plan.Sql, (p, o, d) => BindSave(row, mask, p, o, d), expected: 1)
        {
            ReturnsRows = native,
            Row = native ? (r, d) => _readVersion!(row, r, d) : null,
            Completed = _bumpVersion is null || native ? null : () => _bumpVersion(row),
            Conflict = _versionIndex >= 0 ? _ => new SqlConcurrencyException(_table, _key(row), _version!(row)) : null,
            ReadBackBy = _generated?.AfterUpdate == true ? this : null,
            ReadBackRow = row,
        };
    }

    object?[] ISqlWriter.Snapshot(object row, SqlDialect dialect) => Snapshot((T)row, dialect);

    ulong ISqlWriter.ChangedColumns(object row, object?[] snapshot, SqlDialect dialect) => ChangedColumns((T)row, snapshot, dialect);

    SqlWriteItem ISqlWriter.InsertItemAsIs(object row, SqlDialect dialect) => InsertItem((T)row, dialect, stamp: false);

    SqlWriteItem ISqlWriter.ColumnsItem(object row, ulong mask, SqlDialect dialect) => ColumnsItem((T)row, mask, dialect);

    bool ISqlWriter.ColumnsMoveRollUps(ulong mask) => RollUps.Length > 0 && MovesRollUps(mask);
}
