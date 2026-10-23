using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     What the runtime translator needs to know about an entity, registered by the generator at module
///     start: the same table, columns, navigations and rules the compile-time plans were built from. No
///     reflection on the model at runtime: every reader here is generated code.
/// </summary>
public abstract class SqlEntityMap
{
    private static readonly ConcurrentDictionary<Type, SqlEntityMap> s_byType = new();

    protected SqlEntityMap(Type type, string table, string[] keyColumns, SqlColumn[] columns, SqlNavigation[] navigations, SqlCollection[] collections, Func<LambdaExpression>[] rules, Func<DbDataReader, SqlDialect, object> readBoxed, Func<object, object?> keyOf, string? fixedPredicate = null)
    {
        Type = type;
        Table = table;
        KeyColumns = keyColumns;
        KeyColumn = keyColumns.Length > 0 ? keyColumns[0] : "Id";
        Columns = columns;
        Navigations = navigations;
        Collections = collections;
        Rules = rules;
        ReadBoxed = readBoxed;
        KeyOf = keyOf;
        FixedPredicate = fixedPredicate;
    }

    /// <summary>A predicate every statement on this entity carries (a derived type's rows by discriminator), in the fragment form; null otherwise.</summary>
    public string? FixedPredicate { get; }

    /// <summary>A [SoftDelete] entity's flag column; null otherwise.</summary>
    public string? SoftDeleteColumn { get; init; }

    /// <summary>True when a graph delete of this entity marks the [SoftDelete] rows of its collections too.</summary>
    public bool SoftDeleteCascade { get; init; }

    /// <summary>
    ///     True for a Pragmatic entity: which of its rows a statement reads depends on the caller (its filter families, lifted per execution),
    ///     which only a generated plan carries (<see cref="SqlPlanFilters" />).
    /// </summary>
    public bool CallerFiltered { get; init; }

    /// <summary>
    ///     What every read of this entity's rows carries on the runtime path: the derived type's rows, and the live ones of a [SoftDelete] entity;
    ///     null when nothing. A Pragmatic entity is refused: its families are not on this path yet, and its rows unfiltered are not the caller's.
    /// </summary>
    public string? RowPredicate
        => CallerFiltered
            ? throw new NotSupportedException($"{Type.Name} is a Pragmatic entity, filtered per caller (tenant, owner, scopes): the runtime IQueryable path does not apply those filters yet. Write the chain on session.Query<{Type.Name}>(), whose plan does.") // INVESTIGATE: the families on the runtime path (phase 4)
            : SoftDeleteColumn is null ? FixedPredicate : FixedPredicate is null ? $"t.\"{SoftDeleteColumn}\" = {{bt:0}}" : $"{FixedPredicate} AND t.\"{SoftDeleteColumn}\" = {{bt:0}}";

    public Type Type { get; }

    public string Table { get; }

    /// <summary>The key columns; more than one for a composite key, which joins, includes and identity do not take.</summary>
    public string[] KeyColumns { get; }

    /// <summary>The key column of a single-column key: the first key column.</summary>
    public string KeyColumn { get; }

    public bool HasCompositeKey => KeyColumns.Length > 1;

    /// <summary>The kind of the single-column key, for binding it (a graph's deletes, a link's columns).</summary>
    internal SqlParamKind KeyKind
    {
        get
        {
            if (HasCompositeKey)
                throw new NotSupportedException($"\"{Table}\" has a composite key: a graph hangs off a single-column key.");
            foreach (var column in Columns)
            {
                if (column.Column == KeyColumn)
                    return column.Kind;
            }

            throw new InvalidOperationException($"\"{Table}\" has no column \"{KeyColumn}\".");
        }
    }

    /// <summary>The reader of the single-column key, for grouping rows by a key column read at an ordinal (a collection include through a join table).</summary>
    internal SqlColumn KeyReader => Array.Find(Columns, c => c.Column == KeyColumn) ?? throw new InvalidOperationException($"\"{Table}\" has no column \"{KeyColumn}\".");

    public SqlColumn[] Columns { get; }

    public SqlNavigation[] Navigations { get; }

    /// <summary>The one-to-many sides, for an Include on the runtime path.</summary>
    public SqlCollection[] Collections { get; }

    /// <summary>The entity's visibility rules, as the trees their <c>ToExpression()</c> builds (built once, on first use).</summary>
    public Func<LambdaExpression>[] Rules { get; }

    /// <summary>The whole-row reader, boxed: the runtime path's child plans know the entity only as a <see cref="Type" />.</summary>
    public Func<DbDataReader, SqlDialect, object> ReadBoxed { get; }

    /// <summary>The key of an entity, boxed: what a collection include groups the children by.</summary>
    public Func<object, object?> KeyOf { get; }

    /// <summary>A composite key's values as a <see cref="SqlCompositeKey" />; null for a single-column key (<see cref="KeyOf" /> gives it).</summary>
    public Func<object, object?>? CompositeKeyOf { get; init; }

    /// <summary>The whole key of a row, boxed: the single column's value, or a <see cref="SqlCompositeKey" /> of every key column.</summary>
    internal object? WholeKeyOf(object row) => CompositeKeyOf is null ? KeyOf(row) : CompositeKeyOf(row);

    public SqlCollection? Collection(string property)
    {
        foreach (var collection in Collections)
        {
            if (collection.Property == property)
                return collection;
        }

        return null;
    }

    public SqlColumn? Column(string property)
    {
        foreach (var column in Columns)
        {
            if (column.Property == property)
                return column;
        }

        return null;
    }

    public SqlNavigation? Navigation(string property)
    {
        foreach (var navigation in Navigations)
        {
            if (navigation.Property == property)
                return navigation;
        }

        return null;
    }

    /// <summary>The entity's generated writes, seen without the row type (a graph save reaches the children's writers by their <see cref="Type" />); null when the assembly was not translated.</summary>
    internal abstract ISqlWriter? WriterBoxed { get; }

    /// <summary>True for a view (<c>[Table(ReadOnly = true)]</c>): read like a table, never written.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>True for the root of a table-per-concrete-type hierarchy: read through its view, written through the row's concrete type.</summary>
    public bool Dispatch { get; init; }

    /// <summary>The map of an entity type, or null when the generator registered none (the type is not an [Entity] of a translated assembly).</summary>
    public static SqlEntityMap? For(Type type) => s_byType.TryGetValue(type, out var map) ? map : null;

    /// <summary>Every registered map, in no particular order (a warmup prepares their writers).</summary>
    public static IEnumerable<SqlEntityMap> All => s_byType.Values;

    /// <summary>This map, or the row's own type's when this one dispatches (the root of a table-per-concrete-type hierarchy: the row is of a derived type with its own table).</summary>
    internal SqlEntityMap Concrete(object row)
        => !Dispatch || row.GetType() == Type ? this : For(row.GetType()) ?? throw new InvalidOperationException($"No map is registered for {row.GetType().Name}: it is not an [Entity] of a translated assembly.");

    /// <summary>The writer a row of this entity goes through: its own, or its concrete type's when this map dispatches; the reason when there is none.</summary>
    internal ISqlWriter WriterFor(object row)
    {
        var map = Concrete(row);
        return map.WriterBoxed ?? throw new InvalidOperationException(map.ReadOnly
            ? $"{map.Type.Name} is read-only ([Table(ReadOnly = true)], a view): it is read, never written."
            : map.Dispatch
                ? $"{map.Type.Name} is the root of a table-per-concrete-type hierarchy, a view over the types' tables: a write through it takes a row of a derived type."
                : $"No writer is registered for {map.Type.Name}: the generator did not run on the assembly that declares it.");
    }

    protected static void Add(SqlEntityMap map) => s_byType[map.Type] = map;
}

/// <summary>The typed map: its reader shapes a whole row into the entity.</summary>
public sealed class SqlEntityMap<T> : SqlEntityMap
{
    private static SqlEntityMap<T>? s_current;

    public SqlEntityMap(string table, string[] keyColumns, SqlColumn[] columns, SqlNavigation[] navigations, SqlCollection[] collections, Func<LambdaExpression>[] rules, Func<DbDataReader, SqlDialect, T> read, Func<T, object?> key, string? fixedPredicate = null)
        : base(typeof(T), table, keyColumns, columns, navigations, collections, rules, (r, d) => read(r, d)!, e => key((T)e), fixedPredicate)
    {
        Read = read;
    }

    public Func<DbDataReader, SqlDialect, T> Read { get; }

    /// <summary>The entity's generated writes; null when the entity's assembly was not translated.</summary>
    public SqlWriter<T>? Writer { get; init; }

    internal override ISqlWriter? WriterBoxed => Writer;

    public static SqlEntityMap<T> Current => s_current ?? throw new InvalidOperationException($"No map is registered for {typeof(T).Name}: it is not an [Entity], or the generator did not run on the assembly that declares it.");

    public static void Register(SqlEntityMap<T> map)
    {
        s_current = map;
        Add(map);
    }
}

/// <summary>A mapped column: the property it fills (a path for an owned type's member), its name in the table, the kind it is stored as, and how a value is read at an ordinal.</summary>
public sealed class SqlColumn
{
    public SqlColumn(string property, string column, SqlParamKind kind, Func<DbDataReader, int, SqlDialect, object?> read, Func<object?, object?>? toStore = null)
    {
        Property = property;
        Column = column;
        Kind = kind;
        Read = read;
        ToStore = toStore;
    }

    public string Property { get; }

    public string Column { get; }

    /// <summary>The kind the column is stored as (the converter's store type, when the property has one).</summary>
    public SqlParamKind Kind { get; }

    /// <summary>The column in a select list under <paramref name="alias" />: the reference itself, or for a vector the token the dialect resolves into a read as text (see <see cref="SqlDialects.ResolveVectorCasts" />).</summary>
    public string Select(string alias) => Kind == SqlParamKind.Vector ? SqlDialects.VectorSelect($"{alias}.\"{Column}\"") : $"{alias}.\"{Column}\"";

    /// <summary>Reads the value at an ordinal, boxed and converted to the property's type: the runtime path's scalar projections and dynamic DTOs go through it.</summary>
    public Func<DbDataReader, int, SqlDialect, object?> Read { get; }

    /// <summary>The property's converter towards the column, boxed; null when the column stores the property's type. A value compared with the column binds through it.</summary>
    public Func<object?, object?>? ToStore { get; }

    /// <summary>True for a column holding the property as JSON (<c>SqlJson</c>): a member path below it is a JSON path in the statement.</summary>
    public bool IsJson { get; init; }

    /// <summary>
    ///     On a JSON column, the members below it whose JSON name is not their own (<c>[JsonPropertyName]</c>), keyed by their path of property
    ///     names from the column (<c>Author.Name</c>), a collection crossed to its element; null when none is renamed. Written by the generator,
    ///     which reads the attributes at compile time, so the runtime path does not read them with reflection.
    /// </summary>
    public IReadOnlyDictionary<string, string>? JsonNames { get; init; }

    /// <summary>The JSON name of the member at <paramref name="path" /> below this column: its renamed name, else <paramref name="name" />.</summary>
    internal string JsonName(string path, string name)
        => JsonNames is not null && JsonNames.TryGetValue(path, out var renamed) ? renamed : name;

    /// <summary>
    ///     The element of the JSON array at <paramref name="path" /> below this column, as a column of its own named <paramref name="column" />: the
    ///     names below the array, re-rooted at the element.
    /// </summary>
    internal SqlColumn ElementAt(string path, string column)
    {
        Dictionary<string, string>? below = null;
        if (JsonNames is not null)
        {
            var prefix = path + ".";
            foreach (var (key, value) in JsonNames)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    (below ??= new Dictionary<string, string>(StringComparer.Ordinal))[key[prefix.Length..]] = value;
            }
        }

        return new SqlColumn(column, column, SqlParamKind.String, static (_, _, _) => null) { IsJson = true, JsonNames = below };
    }

    /// <summary>A <c>[FullText]</c> column's table and key column: <c>Sql.FullText</c> on it is the engine's own search (FTS5, CONTAINS); null otherwise.</summary>
    public string? FullTextTable { get; init; }

    public string? FullTextKey { get; init; }
}

/// <summary>
///     A to-one navigation: the property, the foreign key column on this table, the target entity, the alias the join
///     uses; and, for an Include on the runtime path, how the target is read from a row at an offset and how the
///     property is read and set on a boxed owner. All generated: no reflection on the model.
/// </summary>
public sealed class SqlNavigation
{
    public SqlNavigation(string property, string foreignKeyColumn, bool required, Type target, Func<DbDataReader, int, SqlDialect, object> readAt, Func<object, object?> get, Action<object, object?> set)
    {
        Property = property;
        ForeignKeyColumn = foreignKeyColumn;
        ForeignKeyColumns = [foreignKeyColumn];
        Required = required;
        Target = target;
        ReadAt = readAt;
        Get = get;
        Set = set;
    }

    public string Property { get; }

    public string ForeignKeyColumn { get; }

    /// <summary>Towards a composite key: one foreign key column per key column of the target, in its key order; <see cref="ForeignKeyColumn" /> alone otherwise.</summary>
    public string[] ForeignKeyColumns { get; init; }

    /// <summary>True when the foreign key cannot be null: the navigation always has its target and joins INNER; otherwise LEFT, unless the predicate needs the row.</summary>
    public bool Required { get; }

    public Type Target { get; }

    public string Alias => "j_" + Property;

    /// <summary>The join of this navigation from <paramref name="parentAlias" /> under <paramref name="alias" />, in the fragment form the generator emits.</summary>
    public string Join(SqlEntityMap target, string parentAlias, string alias)
    {
        if (ForeignKeyColumns.Length != target.KeyColumns.Length)
            throw new NotSupportedException($"{target.Type.Name} has a composite key ({string.Join(", ", target.KeyColumns)}): the navigation '{Property}' joins on one foreign key per key column.");
        var on = new System.Text.StringBuilder();
        for (var i = 0; i < ForeignKeyColumns.Length; i++)
            on.Append(i > 0 ? " AND " : "").Append(alias).Append(".\"").Append(target.KeyColumns[i]).Append("\" = ").Append(parentAlias).Append(".\"").Append(ForeignKeyColumns[i]).Append('"');
        return $"{(Required ? "INNER" : "LEFT")} JOIN \"{target.Table}\" {alias} ON {on}";
    }

    /// <summary>An optional navigation's LEFT JOIN as INNER when the alias is among those the predicates need a row for.</summary>
    public static string Inner(string join, HashSet<string> innerAliases)
    {
        if (innerAliases.Count == 0 || !join.StartsWith("LEFT JOIN \"", StringComparison.Ordinal))
            return join;
        var table = join.IndexOf('"', "LEFT JOIN \"".Length) + 2; // past the closing quote and the space
        var end = join.IndexOf(' ', table);
        return end > table && innerAliases.Contains(join.Substring(table, end - table)) ? "INNER" + join.Substring("LEFT".Length) : join;
    }

    /// <summary>Reads the target entity whose columns start at an ordinal.</summary>
    public Func<DbDataReader, int, SqlDialect, object> ReadAt { get; }

    public Func<object, object?> Get { get; }

    public Action<object, object?> Set { get; }
}

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

    /// <summary>Sets the property to the children (boxed rows of <see cref="Child" />).</summary>
    public Action<object, List<object>> Assign { get; }
}
