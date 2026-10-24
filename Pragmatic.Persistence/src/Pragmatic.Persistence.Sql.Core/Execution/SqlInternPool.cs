using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>
///     The distinct values of an <c>[Interned]</c> column, one instance each (see <see cref="InternedAttribute" />): one pool per
///     column, shared by every assembly that reads it. The provider materializes the string (measured: reading through
///     <c>GetChars</c> into a buffer saved nothing on PostgreSQL and SQLite and cost more on SQL Server), the pool hands back the
///     instance it already holds, so the rows retain one string per distinct value. Bounded: past <see cref="Capacity" /> distinct
///     values the pool stops growing and returns the value as read, so a column that turns out high-cardinality costs a lookup,
///     never unbounded memory.
/// </summary>
public sealed class SqlInternPool
{
    public const int Capacity = 4096;

    private static readonly ConcurrentDictionary<string, SqlInternPool> s_byColumn = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The one pool of a column (<c>Namespace.Entity.Property</c>), shared by every assembly that reads it: the entity's own map, a consumer's plans and projections hand out the same instances.</summary>
    public static SqlInternPool For(string column) => s_byColumn.GetOrAdd(column, static _ => new SqlInternPool());

    /// <summary>How many distinct values the pool holds.</summary>
    public int Count => _values.Count;

    /// <summary>The column's value at the ordinal, as the pooled instance when the pool has it.</summary>
    public string Read(DbDataReader reader, int ordinal, SqlDialect dialect) => Intern(reader.GetString(ordinal));

    /// <summary>The pooled instance equal to <paramref name="value" />, adding it while there is room.</summary>
    public string Intern(string value)
    {
        if (_values.TryGetValue(value, out var pooled))
            return pooled;
        return _values.Count < Capacity ? _values.GetOrAdd(value, value) : value;
    }
}
