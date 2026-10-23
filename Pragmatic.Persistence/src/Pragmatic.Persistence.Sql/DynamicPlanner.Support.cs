using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>The planner's support: the plan cache by text, the bind of a runtime chain's values, the projections, the final text per dialect.</summary>
internal static partial class DynamicPlanner
{
    /// <summary>The plan of a statement, one per (dialect, text, row type): two chains with the same SQL share its prepared command.</summary>
    internal static SqlPlan<TResult> TextPlan<TResult>(SqlDialect dialect, string text, string cacheKey, List<SqlParamKind> kinds, Func<DbDataReader, SqlDialect, TResult>? reader, SqlLoad<TResult>? load, bool reversed = false)
    {
        var key = (dialect, reversed ? cacheKey + "\nreversed" : cacheKey, typeof(TResult)); // a Before and an After with the opposite directions share the text, not the plan
        if (!s_plans.TryGetValue(key, out var cached))
        {
            if (s_plans.Count >= CacheLimit)
                s_plans.Clear();
            cached = s_plans.GetOrAdd(key, _ => new SqlPlan<TResult>(text, text, text, kinds.ToArray(), BindValues, reader, load, reversed: reversed, mySql: text, oracle: text));
        }

        return (SqlPlan<TResult>)cached;
    }

    private static void BindValues<TResult>(in SqlQuery<TResult> query, DbParameterCollection parameters, SqlDialect dialect)
    {
        var values = (object?[])query.Slot(0)!;
        for (var i = 0; i < values.Length; i++)
            parameters[i].Value = SqlValue.ToDatabase(dialect, values[i]);
    }

    private static LambdaExpression Lambda(Expression argument)
        => argument is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted } ? quoted : argument as LambdaExpression ?? throw new NotSupportedException($"'{argument}' is not a lambda.");

    private static SqlColumn ColumnFor(SqlEntityMap map, LambdaExpression selector)
    {
        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;
        return body is MemberExpression member && map.Column(member.Member.Name) is { } column ? column : throw new NotSupportedException($"'{selector}' must select a column of {map.Type.Name}.");
    }

    /// <summary>A Select at runtime: a column, or a DTO registered with [MapFrom] (its member assignments are not read: the registered reader shapes the row).</summary>
    private static (string Columns, Func<DbDataReader, SqlDialect, TResult> Read, SqlProjectionMap? Declared) Projection<TResult>(SqlEntityMap map, LambdaExpression lambda, SqlDialect dialect, List<object?> values, List<SqlParamKind> kinds, List<string> joins, SqlCapture capture)
    {
        var body = lambda.Body;
        if (body is MemberExpression member && lambda.Parameters[0] == Root(member) && map.Column(member.Member.Name) is { } column)
        {
            var translator = new ExpressionTranslator(map, lambda.Parameters[0], dialect, values, kinds, joins, capture);
            var text = translator.Column(member);
            return (text, (r, d) => (TResult)column.Read(r, 0, d)!, null);
        }

        return Declared<TResult>(map, dialect, values, kinds, joins, capture);
    }

    /// <summary>The projection registered for (entity, DTO) by the generator, from the DTO's [MapFrom]: its columns (aggregates included), its joins, its reader.</summary>
    private static (string Columns, Func<DbDataReader, SqlDialect, TResult> Read, SqlProjectionMap? Declared) Declared<TResult>(SqlEntityMap map, SqlDialect dialect, List<object?> values, List<SqlParamKind> kinds, List<string> joins, SqlCapture capture)
    {
        var declared = SqlProjectionMap.For(map.Type, typeof(TResult))
            ?? throw new NotSupportedException($"A runtime projection into {typeof(TResult).Name} needs it declared: mark the DTO [MapFrom<{map.Type.Name}>] so the generator registers its columns and reader.");
        foreach (var join in declared.Joins)
        {
            if (joins.Exists(j => j == join || j.StartsWith(join + " AND ", StringComparison.Ordinal)))
                continue;
            var at = joins.Count;
            joins.Add(join);
            joins[at] = ExpressionTranslator.DeclaredJoin(map, join, dialect, values, kinds, joins, capture); // a flattened member's navigation: its target's rules in the join
        }

        return (string.Join(", ", declared.Columns), (r, d) => (TResult)declared.ReadBoxed(r, d)!, declared);
    }

    private static Expression Root(MemberExpression member)
    {
        Expression node = member;
        while (node is MemberExpression inner)
            node = inner.Expression!;
        return node;
    }

    /// <summary>Placeholders become <c>@pN</c> (lists keep <c>{N}</c> for the runtime expansion); SQL Server gets brackets.</summary>
    internal static string Finish(string sql, SqlDialect dialect, List<SqlParamKind> kinds)
    {
        sql = SqlDialects.ResolveVectorCasts(sql, dialect); // the vector columns read as text, as the generator's statements do
        sql = SqlDialects.ResolveBooleanLiterals(sql, dialect); // a declared projection's literal booleans, as the generator's statements resolve them
        sql = SqlDialects.InlineWindows(sql); // a named window written in place on the runtime path
        var text = new StringBuilder(sql.Length + 16);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '{' && i + 1 < sql.Length && char.IsDigit(sql[i + 1]))
            {
                var end = sql.IndexOf('}', i);
                var index = int.Parse(sql.AsSpan(i + 1, end - i - 1), CultureInfo.InvariantCulture);
                text.Append(kinds[index] == SqlParamKind.List ? "{" + index + "}" : "@p" + index);
                i = end;
            }
            else if (c == '"' && dialect == SqlDialect.SqlServer)
            {
                // identifiers only: values never appear in the text
                text.Append(IsOpeningQuote(sql, i) ? '[' : ']');
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    private static bool IsOpeningQuote(string sql, int index)
    {
        var quotes = 0;
        for (var i = 0; i < index; i++)
        {
            if (sql[i] == '"')
                quotes++;
        }

        return quotes % 2 == 0;
    }
}
