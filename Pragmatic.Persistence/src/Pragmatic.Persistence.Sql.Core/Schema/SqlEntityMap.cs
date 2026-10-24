using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Schema;

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
