using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A declared projection the runtime path can materialise: the DTO's columns (fragments, with the joins
///     they need) and the generated reader. Registered by the generator for every DTO marked
///     <c>[MapFrom&lt;TEntity&gt;]</c>; a runtime <c>Select</c> into that DTO uses it instead of the tree's
///     member assignments, which it could not run without compiling.
/// </summary>
public abstract class SqlProjectionMap
{
    private static readonly ConcurrentDictionary<(Type Source, Type Result), SqlProjectionMap> s_byPair = new();

    protected SqlProjectionMap(Type source, Type result, string[] columns, string[] joins, Func<DbDataReader, SqlDialect, object?> readBoxed)
    {
        Source = source;
        Result = result;
        Columns = columns;
        Joins = joins;
        ReadBoxed = readBoxed;
    }

    /// <summary>The reader, boxed: the runtime path knows the result type only as a <see cref="Type" />.</summary>
    public Func<DbDataReader, SqlDialect, object?> ReadBoxed { get; }

    public Type Source { get; }

    public Type Result { get; }

    /// <summary>The column fragments, in reader order (<c>t."Id"</c>, <c>j_Customer."Name"</c>); an aggregate over a collection is a correlated subquery.</summary>
    public string[] Columns { get; }

    public string[] Joins { get; }

    /// <summary>The DTO's collection members (<c>List&lt;TChildDto&gt;</c>): each a companion statement in the same command, loaded on the parent key the DTO reads.</summary>
    public SqlProjectedCollection[] Collections { get; init; } = [];

    public static SqlProjectionMap? For(Type source, Type result) => s_byPair.TryGetValue((source, result), out var map) ? map : null;

    protected static void Add(SqlProjectionMap map) => s_byPair[(map.Source, map.Result)] = map;
}

public sealed class SqlProjectionMap<TSource, TResult> : SqlProjectionMap
{
    public SqlProjectionMap(string[] columns, string[] joins, Func<DbDataReader, SqlDialect, TResult> read)
        : base(typeof(TSource), typeof(TResult), columns, joins, (r, d) => read(r, d))
    {
        Read = read;
    }

    public Func<DbDataReader, SqlDialect, TResult> Read { get; }

    public static void Register(SqlProjectionMap<TSource, TResult> map) => Add(map);
}

/// <summary>
///     A collection member of a declared projection: the entity's collection it reads, the child DTO (its own
///     <c>[MapFrom]</c> gives the columns and the reader), the child's foreign key column, and the generated
///     accessors the loader uses to group the children on the parent DTO's key and assign them.
/// </summary>
public sealed class SqlProjectedCollection
{
    public SqlProjectedCollection(string property, Type child, Type childDto, string foreignKeyColumn, Func<object, object?> parentKey, Action<object, List<object>> assign)
    {
        Property = property;
        Child = child;
        ChildDto = childDto;
        ForeignKeyColumn = foreignKeyColumn;
        ForeignKeyColumns = [foreignKeyColumn];
        ParentKey = parentKey;
        Assign = assign;
    }

    public string Property { get; }

    /// <summary>The child entity.</summary>
    public Type Child { get; }

    /// <summary>The child DTO, registered with <c>[MapFrom&lt;Child&gt;]</c>.</summary>
    public Type ChildDto { get; }

    public string ForeignKeyColumn { get; }

    /// <summary>A composite parent's: the child's columns holding its key, in its key order (<see cref="ParentKey" /> then gives a <see cref="SqlCompositeKey" />).</summary>
    public string[] ForeignKeyColumns { get; init; }

    /// <summary>The parent entity's key as the parent DTO holds it, boxed.</summary>
    public Func<object, object?> ParentKey { get; }

    /// <summary>Sets the member to the children (boxed rows of <see cref="ChildDto" />).</summary>
    public Action<object, List<object>> Assign { get; }
}
