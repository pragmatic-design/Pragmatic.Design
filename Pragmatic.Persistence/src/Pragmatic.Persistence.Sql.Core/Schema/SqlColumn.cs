using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Schema;

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
