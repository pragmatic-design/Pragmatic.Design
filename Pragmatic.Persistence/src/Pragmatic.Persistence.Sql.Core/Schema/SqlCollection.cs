using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Schema;

/// <summary>A one-to-many side: the property, the child entity, the child's foreign key column, the child's key back to the parent, and how the loaded children are assigned.</summary>
public sealed class SqlCollection
{
    public SqlCollection(string property, Type child, string foreignKeyColumn, Func<object, object?> childKey, Func<object, object?> get, Action<object, List<object>> assign)
    {
        Property = property;
        Child = child;
        ForeignKeyColumn = foreignKeyColumn;
        ForeignKeyColumns = [foreignKeyColumn];
        ChildKey = childKey;
        Get = get;
        Assign = assign;
    }

    public string Property { get; }

    public Type Child { get; }

    /// <summary>The child's column holding the parent's key; through a join table (<see cref="IsLink" />), the join table's column holding it.</summary>
    public string ForeignKeyColumn { get; }

    /// <summary>A composite parent's: the child's columns holding its key, one per key column in its order (<see cref="ChildKey" /> then gives a <see cref="SqlCompositeKey" />).</summary>
    public string[] ForeignKeyColumns { get; init; }

    public bool IsComposite => ForeignKeyColumns.Length > 1;

    /// <summary>The parent key a child row carries, boxed; through a join table, the child's own key (the links are matched on it).</summary>
    public Func<object, object?> ChildKey { get; }

    /// <summary>The join table of a many-to-many side (<c>[ManyToMany]</c>); null for a one-to-many.</summary>
    public string? LinkTable { get; init; }

    /// <summary>The join table's column holding the child's key.</summary>
    public string? LinkChildColumn { get; init; }

    /// <summary>A composite child's: the join table's columns holding its key, in its key order; null for a single-column key.</summary>
    public string[]? LinkChildColumns { get; init; }

    /// <summary>The join table's columns holding the child's key: <see cref="LinkChildColumns" />, or <see cref="LinkChildColumn" /> alone.</summary>
    public string[] ChildColumns => LinkChildColumns ?? (LinkChildColumn is null ? [] : [LinkChildColumn]);

    public bool IsLink => LinkTable is not null;

    /// <summary>The INNER JOIN of the join table to the child rows under <paramref name="childAlias" />, in the fragment form (the link under <paramref name="linkAlias" />).</summary>
    public string LinkJoin(string childAlias, string linkAlias)
    {
        var child = SqlEntityMap.For(Child) ?? throw new InvalidOperationException($"No map is registered for {Child.Name}.");
        var columns = ChildColumns;
        if (columns.Length != child.KeyColumns.Length)
            throw new NotSupportedException($"{Child.Name}'s key has {child.KeyColumns.Length} columns, the join table \"{LinkTable}\" holds {columns.Length} for it: [ManyToMany] names one per key column, comma-separated.");
        return $"INNER JOIN \"{LinkTable}\" {linkAlias} ON " + string.Join(" AND ", columns.Select((c, i) => $"{linkAlias}.\"{c}\" = {childAlias}.\"{child.KeyColumns[i]}\""));
    }

    public Func<object, object?> Get { get; }

    /// <summary>
    ///     Sets a child's foreign key to its parent's key (boxed): what EF Core's relationship fixup does for a child added to the collection,
    ///     for a unit of work that saves the graph. Null for a many-to-many side and under a composite parent.
    /// </summary>
    public Action<object, object?>? SetChildKey { get; init; }

    /// <summary>Sets the property to the children (boxed rows of <see cref="Child" />).</summary>
    public Action<object, List<object>> Assign { get; }
}
