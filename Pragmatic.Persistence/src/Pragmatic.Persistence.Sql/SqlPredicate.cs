using System.Collections;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A predicate translated at compile time, for every dialect: the text on the row alias <c>t</c>, its values as tokens
///     <c>{v:i}</c> (an IN list's token expands to one parameter per item when the predicate is rendered, an array on PostgreSQL).
///     A leaf of <see cref="SqlPredicate{T}" />: the generator emits one per predicate written in source (a specification's
///     <c>Spec&lt;T&gt;.Where</c>, a declared query's filter), and the runtime composes them without reading any text from the caller.
/// </summary>
public sealed class SqlCompiledPredicate(string sqlite, string postgreSql, string sqlServer, string? mySql, string? oracle, SqlParamKind[] kinds)
{
    private readonly string[] _texts = [sqlite, postgreSql, sqlServer, mySql ?? "", oracle ?? ""];

    /// <summary>The kind of each value, by token index.</summary>
    public SqlParamKind[] Kinds { get; } = kinds;

    /// <summary>The text for a dialect; empty where the predicate has no form on it.</summary>
    public string Text(SqlDialect dialect) => _texts[(int)dialect];
}

/// <summary>
///     A predicate over an entity's rows composed at runtime from compiled leaves (<see cref="SqlCompiledPredicate" />): AND, OR, NOT
///     and TRUE. Rendered into a statement where the plan marks it (<c>Matching(predicate)</c> on a chain): the text is assembled from the
///     leaves' compiled texts, the values become parameters after the plan's own. The leaves are validated at compile time, the composition
///     is a shape the runtime renders (parenthesised, so precedence is the tree's).
/// </summary>
public abstract class SqlPredicate : SqlComposed
{
    private protected SqlPredicate()
    {
    }
}

/// <summary>
///     A piece of a statement composed at runtime from parts compiled at compile time (a predicate, an ordering): the plan marks where it
///     goes (<c>{dyn:k}</c> for the chain's slot <c>k</c>), the runtime renders it there when the statement runs.
/// </summary>
public abstract class SqlComposed
{
    private protected SqlComposed()
    {
    }

    /// <summary>
    ///     The text for <paramref name="dialect" /> on the row alias <paramref name="alias" />, its values appended to
    ///     <paramref name="values" /> as parameters named from <paramref name="firstParameter" /> on.
    /// </summary>
    public abstract void Render(SqlDialect dialect, string alias, StringBuilder text, List<(SqlParamKind Kind, object? Value)> values, int firstParameter);
}

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
