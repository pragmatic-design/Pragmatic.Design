using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>The predicate over <typeparamref name="T" />'s rows: what a chain on <typeparamref name="T" /> takes with <c>Matching</c>.</summary>
public abstract class SqlPredicate<T> : SqlPredicate
{
    /// <summary>Every row.</summary>
    public static SqlPredicate<T> True { get; } = new TrueNode();

    /// <summary>A compiled leaf and its values, in token order.</summary>
    public static SqlPredicate<T> Leaf(SqlCompiledPredicate fragment, object?[] values) => new LeafNode(fragment, values);

    public static SqlPredicate<T> And(SqlPredicate<T> left, SqlPredicate<T> right) => new BinaryNode(" AND ", left, right);

    public static SqlPredicate<T> Or(SqlPredicate<T> left, SqlPredicate<T> right) => new BinaryNode(" OR ", left, right);

    public static SqlPredicate<T> Not(SqlPredicate<T> inner) => new NotNode(inner);

    public override void Render(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
        => Write(dialect, alias, text, values, firstParameter);

    private protected abstract void Write(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter);

    private sealed class TrueNode : SqlPredicate<T>
    {
        private protected override void Write(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
            => text.Append("(1 = 1)");
    }

    private sealed class BinaryNode(string connective, SqlPredicate<T> left, SqlPredicate<T> right) : SqlPredicate<T>
    {
        private protected override void Write(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
        {
            text.Append('(');
            left.Write(dialect, alias, text, values, firstParameter);
            text.Append(connective);
            right.Write(dialect, alias, text, values, firstParameter);
            text.Append(')');
        }
    }

    private sealed class NotNode(SqlPredicate<T> inner) : SqlPredicate<T>
    {
        private protected override void Write(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
        {
            text.Append("(NOT ");
            inner.Write(dialect, alias, text, values, firstParameter);
            text.Append(')');
        }
    }

    private sealed class LeafNode(SqlCompiledPredicate fragment, object?[] leafValues) : SqlPredicate<T>
    {
        private protected override void Write(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter)
        {
            var source = fragment.Text(dialect);
            if (source.Length == 0)
                throw new NotSupportedException($"A compiled predicate of {typeof(T).Name} has no form on {dialect}.");
            if (alias != "t")
                source = source.Replace("t.\"", alias + ".\"", StringComparison.Ordinal).Replace("t.[", alias + ".[", StringComparison.Ordinal);
            // Parenthesised like every node: a leaf is a whole predicate ("a AND b", "a OR b"), and under a NOT or beside an AND its own
            // connectives would otherwise bind to the neighbours.
            text.Append('(');
            var at = 0;
            for (var start = source.IndexOf("{v:", StringComparison.Ordinal); start >= 0; start = source.IndexOf("{v:", at, StringComparison.Ordinal))
            {
                var close = source.IndexOf('}', start);
                var index = int.Parse(source.AsSpan(start + 3, close - start - 3), CultureInfo.InvariantCulture);
                text.Append(source, at, start - at);
                Parameter(dialect, text, values, firstParameter, fragment.Kinds[index], leafValues[index]);
                at = close + 1;
            }

            text.Append(source, at, source.Length - at);
            text.Append(')');
        }

        /// <summary>One value as a parameter; an IN list as one parameter per item (NULL for an empty list, which matches nothing), one array on PostgreSQL.</summary>
        private static void Parameter(SqlDialect dialect, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter, SqlParamKind kind, object? value)
        {
            if (kind != SqlParamKind.List || dialect == SqlDialect.PostgreSql)
            {
                text.Append("@p").Append((firstParameter + values.Count).ToString(CultureInfo.InvariantCulture));
                values.Add((kind == SqlParamKind.List ? SqlParamKind.Unknown : kind, value));
                return;
            }

            var any = false;
            if (value is IEnumerable items and not string)
            {
                foreach (var item in items)
                {
                    text.Append(any ? ", @p" : "@p").Append((firstParameter + values.Count).ToString(CultureInfo.InvariantCulture));
                    values.Add((SqlParamKind.Unknown, item));
                    any = true;
                }
            }

            if (!any)
                text.Append("NULL");
        }
    }
}
