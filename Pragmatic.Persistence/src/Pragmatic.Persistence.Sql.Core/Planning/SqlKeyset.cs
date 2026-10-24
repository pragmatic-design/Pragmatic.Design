using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     The predicate of keyset pagination, the runtime twin of the generator's: the rows after a cursor in an
///     ordering. A row-value comparison where the dialect has it (SQLite, PostgreSQL) and every key runs the
///     same way; the expanded form (<c>k1 &gt; a OR (k1 = a AND k2 &gt; b) OR …</c>) on SQL Server, and wherever
///     the directions differ (a row value compares one way only).
/// </summary>
public static class SqlKeyset
{
    public static string Predicate(SqlDialect dialect, IReadOnlyList<string> keys, IReadOnlyList<bool> descending, IReadOnlyList<string> parameters)
    {
        if (keys.Count == 1)
            return $"{keys[0]} {Operator(descending[0])} {parameters[0]}";
        var uniform = true;
        for (var i = 1; i < keys.Count; i++)
            uniform &= descending[i] == descending[0];
        if (uniform && dialect is not (SqlDialect.SqlServer or SqlDialect.Oracle)) // neither compares row values with < or >: the expanded form there
            return $"({string.Join(", ", keys)}) {Operator(descending[0])} ({string.Join(", ", parameters)})";
        var terms = new List<string>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var comparison = $"{keys[i]} {Operator(descending[i])} {parameters[i]}";
            if (i == 0)
            {
                terms.Add(comparison);
                continue;
            }

            var equal = new List<string>(i);
            for (var j = 0; j < i; j++)
                equal.Add($"{keys[j]} = {parameters[j]}");
            terms.Add($"({string.Join(" AND ", equal)} AND {comparison})");
        }

        return string.Join(" OR ", terms);
    }

    private static string Operator(bool descending) => descending ? "<" : ">";

    /// <summary>The i-th value of a cursor: the cursor itself for one key, the tuple's item for several; the shape of the cursor must be the keys'.</summary>
    public static object? Item(object? cursor, int index, int count)
    {
        if (count == 1)
            return cursor;
        if (cursor is not ITuple tuple || tuple.Length != count)
            throw new NotSupportedException($"The cursor has {(cursor is ITuple t ? t.Length : 1)} value(s) and the ordering {count} keys: pass a tuple with the last row's values of the ordering's keys, in order.");
        return tuple[index];
    }
}
