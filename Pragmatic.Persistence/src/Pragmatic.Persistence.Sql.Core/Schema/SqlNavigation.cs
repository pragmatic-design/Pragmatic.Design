using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Schema;

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
