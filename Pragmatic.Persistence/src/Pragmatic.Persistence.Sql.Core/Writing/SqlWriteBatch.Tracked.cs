namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     The writes of a unit of work that stamps its rows itself and knows what changed from a snapshot (<see cref="SqlSnapshots" />): an
///     insert as the row stands, an update of the changed columns at the row's version, a delete. The rows are seen as objects (a unit of
///     work tracks rows of many types); the roll-ups they feed move with them.
/// </summary>
public sealed partial class SqlWriteBatch
{
    /// <summary>The row's insert, its stamps left as the caller set them.</summary>
    public SqlWriteBatch InsertAsIs(object row)
    {
        var writer = SqlSnapshots.WriterOf(row);
        AddWrite(writer, row, writer.InsertItemAsIs(row, Session.Dialect), before: false, after: true);
        Rows++;
        return this;
    }

    /// <summary>The UPDATE of the row's <paramref name="columns" /> (<see cref="SqlSnapshots.ChangedColumns" />) at its version, its stamps left as the caller set them; nothing when none.</summary>
    public SqlWriteBatch UpdateColumns(object row, ulong columns)
    {
        if (columns == 0)
            return this;
        var writer = SqlSnapshots.WriterOf(row);
        var moves = writer.ColumnsMoveRollUps(columns);
        AddWrite(writer, row, writer.ColumnsItem(row, columns, Session.Dialect), before: moves, after: moves);
        Rows++;
        return this;
    }

    /// <summary>The row's DELETE by key (and version). A <c>[SoftDelete]</c> row is refused: a unit of work marks it, an update.</summary>
    public SqlWriteBatch DeleteRow(object row)
    {
        var writer = SqlSnapshots.WriterOf(row);
        if (writer.SoftDeletes && !SoftDeleteAsIs)
            throw new InvalidOperationException($"{row.GetType().Name} is [SoftDelete]: a unit of work marks the row deleted (an update of its flags), it does not delete it.");
        AddWrite(writer, row, writer.HardDeleteItem(row, Session.Dialect), before: true, after: false);
        Rows++;
        return this;
    }

    /// <summary>True when <see cref="DeleteRow" /> may delete a <c>[SoftDelete]</c> row for good (Pragmatic's <c>SoftDeleteScope.Suspend()</c>, an erasure).</summary>
    public bool SoftDeleteAsIs { get; set; }
}
