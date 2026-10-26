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

    /// <summary>
    ///     A link of a many-to-many side between two rows seen as objects, the side given (a unit of work's, which tracks rows of many types):
    ///     a row of the join table, as <see cref="Link{TParent, TChild}" /> writes it.
    /// </summary>
    public SqlWriteBatch LinkRows(object parent, SqlCollection link, object child)
    {
        var (parentKinds, parentKey, childKinds, childKey) = LinkKeys(parent, link, child);
        _items.Add(SqlLinkItems.Insert(link, parentKinds, parentKey, childKinds, childKey));
        return this;
    }

    /// <summary>The link between two rows seen as objects removed, as <see cref="Unlink{TParent, TChild}" /> removes it.</summary>
    public SqlWriteBatch UnlinkRows(object parent, SqlCollection link, object child)
    {
        var (parentKinds, parentKey, childKinds, childKey) = LinkKeys(parent, link, child);
        _items.Add(SqlLinkItems.Delete(link, parentKinds, parentKey, childKinds, childKey));
        return this;
    }

    private static (SqlParamKind[] ParentKinds, object?[] ParentKey, SqlParamKind[] ChildKinds, object?[] ChildKey) LinkKeys(object parent, SqlCollection link, object child)
    {
        if (!link.IsLink)
            throw new ArgumentException($"{parent.GetType().Name}.{link.Property} is a one-to-many: its rows carry the parent's key; links are for a [ManyToMany] side.", nameof(link));
        var map = SqlEntityMap.For(parent.GetType()) ?? throw new ArgumentException($"{parent.GetType().Name} has no map: it is not an entity of this engine.", nameof(parent));
        var childMap = SqlEntityMap.For(link.Child) ?? throw new ArgumentException($"{link.Child.Name} has no map: it is not an entity of this engine.", nameof(child));
        var (parentKinds, parentKey) = SqlSession.KeyValues(map, parent);
        var (childKinds, childKey) = SqlSession.KeyValues(childMap, child);
        return (parentKinds, parentKey, childKinds, childKey);
    }
}
