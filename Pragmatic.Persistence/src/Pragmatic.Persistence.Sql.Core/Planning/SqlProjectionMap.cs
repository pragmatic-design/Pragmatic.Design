using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Planning;

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
