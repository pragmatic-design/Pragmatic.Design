namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>A writer seen without its row type: what a graph save needs of the children's writers.</summary>
internal interface ISqlWriter
{
    /// <summary>A table per type: the derived table's writer beside this one; null otherwise.</summary>
    ISqlWriter? Part { get; }

    SqlWriteItem InsertItem(object row, SqlDialect dialect);

    /// <summary>The modified columns of a tracked row; null when none.</summary>
    SqlWriteItem? SaveItem(object row, SqlDialect dialect);

    SqlWriteItem UpdateItem(object row, SqlDialect dialect);

    SqlWriteItem DeleteItem(object row, SqlDialect dialect);

    /// <summary>The row's DELETE by key (and version), a [SoftDelete] row's too.</summary>
    SqlWriteItem HardDeleteItem(object row, SqlDialect dialect);

    /// <summary>The row's upsert on this writer's table, run now (a versioned one reads the version back).</summary>
    Task UpsertAsync(SqlSession session, object row, CancellationToken ct);

    /// <summary>The database-filled columns of the rows read back into them, in lots.</summary>
    Task ReadGeneratedAsync(SqlSession session, IReadOnlyList<object> rows, CancellationToken ct);

    /// <summary>True for a [SoftDelete] table: its deletes mark the rows.</summary>
    bool SoftDeletes { get; }

    /// <summary>The soft delete of the rows a condition on this table selects, the root's key as its parameters (<c>@p0</c> on), marked at <paramref name="at" />.</summary>
    SqlWriteItem SoftDeleteWhereItem(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey, DateTimeOffset at);

    /// <summary>The soft delete of one row, marked at <paramref name="at" />: a graph's rows share their instant.</summary>
    SqlWriteItem SoftDeleteItemAt(object row, SqlDialect dialect, DateTimeOffset at);

    /// <summary>The roll-up statements of one row's write (<see cref="RollUpAttribute{TChild}" />): before it (<paramref name="add" /> false) or after it; none when the table feeds none.</summary>
    IReadOnlyList<SqlWriteItem> RollUpItems(object row, bool add, SqlDialect dialect);

    /// <summary>True when a save of the tracked row's modified columns moves a roll-up (its foreign key or the summed column among them).</summary>
    bool SaveMovesRollUps(object row);

    /// <summary>The roll-ups less the orphans <see cref="OrphansItem" /> deletes, before it.</summary>
    IReadOnlyList<SqlWriteItem> OrphansRollUps(string[] foreignKeyColumns, SqlParamKind[] parentKeyKinds, object?[] parentKey, IReadOnlyList<object?> keptKeys);

    /// <summary>The roll-ups less the rows <see cref="DeleteWhereItem" /> deletes, before it.</summary>
    IReadOnlyList<SqlWriteItem> DeleteWhereRollUps(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey);

    /// <summary>DELETE of the rows under a parent whose key is not among <paramref name="keptKeys" /> (every row under it when the list is empty).</summary>
    SqlWriteItem OrphansItem(string[] foreignKeyColumns, SqlParamKind[] parentKeyKinds, object?[] parentKey, IReadOnlyList<object?> keptKeys);

    /// <summary>DELETE of the rows a condition on this table selects, with the parent's key as its one parameter (<c>@p0</c>).</summary>
    SqlWriteItem DeleteWhereItem(Func<SqlDialect, string> where, SqlParamKind[] parentKeyKinds, object?[] parentKey);

    /// <summary>The single-row insert, the update and the delete prepared on the session ahead of their first use; how many were.</summary>
    int Warm(SqlSession session);

    /// <summary>The row's column values as they are bound: a unit of work's snapshot.</summary>
    object?[] Snapshot(object row, SqlDialect dialect);

    /// <summary>The columns whose value differs from the snapshot, a bit each.</summary>
    ulong ChangedColumns(object row, object?[] snapshot, SqlDialect dialect);

    /// <summary>The row's insert, its stamps as the caller set them.</summary>
    SqlWriteItem InsertItemAsIs(object row, SqlDialect dialect);

    /// <summary>The UPDATE of the masked columns at the row's version, unstamped.</summary>
    SqlWriteItem ColumnsItem(object row, ulong mask, SqlDialect dialect);

    /// <summary>True when an update of these columns moves a roll-up this table feeds.</summary>
    bool ColumnsMoveRollUps(ulong mask);
}
