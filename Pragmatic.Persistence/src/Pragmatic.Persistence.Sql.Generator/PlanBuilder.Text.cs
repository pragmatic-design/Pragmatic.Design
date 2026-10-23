using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The text of the plan class, and the helpers that turn fragments into the statement of a dialect.</summary>
internal sealed partial class PlanBuilder
{
    /// <summary>
    ///     Writes the plan class into <paramref name="plans" />: the command text per dialect (companion statements included), the bind, the
    ///     reader, the loader. <paramref name="origin" /> names the call site the plan belongs to (file, line, the chain as written), as the
    ///     class's documentation: the plan's name is a counter, the comment is how a reader of the generated file finds its way back.
    /// </summary>
    public void WriteClass(StringBuilder plans, string className, bool fileScoped = true, string? origin = null, string? site = null)
    {
        var reader = ReaderExpression();
        var load = HasLoads && !IsScalar ? Loader() : "null";
        var kindList = _kinds.Count == 0 ? "global::System.Array.Empty<global::Pragmatic.Persistence.Sql.SqlParamKind>()" : "new global::Pragmatic.Persistence.Sql.SqlParamKind[] { " + string.Join(", ", _kinds.Select(k => "global::Pragmatic.Persistence.Sql.SqlParamKind." + k)) + " }";
        // The statements, then the filter families of Pragmatic's entities numbered in them (null: the plan reads none).
        var texts = new[] { Sql(SqlDialect.Sqlite), Sql(SqlDialect.PostgreSql), Sql(SqlDialect.SqlServer), _returning is not null || _rawFrom is not null ? null : Sql(SqlDialect.MySql), _returning is not null ? null : Sql(SqlDialect.Oracle) };
        var filters = NumberFamilies(texts);

        if (origin is not null)
            plans.AppendLine($"    /// <summary>{origin}</summary>");
        plans.AppendLine($"    {(fileScoped ? "file" : "internal")} static class {PlanType(className)}");
        plans.AppendLine("    {");
        foreach (var field in _fields)
            plans.AppendLine($"        {field}");
        plans.AppendLine($"        internal static readonly SqlPlan<{_elementType}> Instance = new(");
        plans.AppendLine($"            {SqlInterceptorGenerator.Literal(texts[0]!)},");
        plans.AppendLine($"            {SqlInterceptorGenerator.Literal(texts[1]!)},");
        plans.AppendLine($"            {SqlInterceptorGenerator.Literal(texts[2]!)},");
        plans.AppendLine($"            {kindList},");
        plans.AppendLine($"            static (in SqlQuery<{_elementType}> q, global::System.Data.Common.DbParameterCollection p, SqlDialect d) =>");
        plans.AppendLine("            {");
        foreach (var bind in _binds)
            plans.AppendLine($"                {bind}");
        plans.AppendLine("            },");
        plans.AppendLine($"            {reader},");
        plans.AppendLine($"            {load},");
        plans.AppendLine($"            {SqlInterceptorGenerator.Literal((site ?? className).Replace("*/", "* /") + " " + className)}{(Reversed ? ", reversed: true" : "")}{(ShapeColumnsLiteral is { } shaped ? ", shapeColumns: " + shaped : "")},"); // the origin: the tag of the plan's metrics, the comment on its statements; Before: the rows turned back once read; ShapeAsync: the members the client may name
        plans.AppendLine($"            mySql: {(texts[3] is null ? "null" : SqlInterceptorGenerator.Literal(texts[3]!))},"); // null: no statement on MySQL (RETURNING, a table-valued function), the plan says so when asked
        var tail = new List<string>();
        if (filters is not null)
            tail.Add($"filters: {filters}"); // Pragmatic's filter families: the statement per caller
        if (PredicateSlotsArgument is { } predicates)
            tail.Add($"predicates: {predicates}"); // the slots of the predicates composed at runtime (Matching)
        plans.AppendLine($"            oracle: {(texts[4] is null ? "null" : SqlInterceptorGenerator.Literal(texts[4]!))}{(tail.Count == 0 ? ");" : ",")}"); // null: no RETURNING rows on Oracle
        for (var i = 0; i < tail.Count; i++)
            plans.AppendLine($"            {tail[i]}{(i == tail.Count - 1 ? ");" : ",")}");
        plans.AppendLine("    }");
        plans.AppendLine();
    }

    private static readonly Regex LeftJoinAlias = new("^LEFT JOIN \"[^\"]*\" (\\S+) ON ", RegexOptions.Compiled);

    /// <summary>An optional navigation's LEFT JOIN as INNER when a predicate of the chain needs the joined row (its alias is in <see cref="_innerAliases" />).</summary>
    private string Inner(string join)
    {
        var match = LeftJoinAlias.Match(join);
        return match.Success && _innerAliases.Contains(match.Groups[1].Value) ? "INNER" + join.Substring("LEFT".Length) : join;
    }

    private static readonly Regex PieceToken = new(@"\{d(\d+)\}", RegexOptions.Compiled);

    /// <summary>A fragment's local placeholders become the plan's parameters: <c>@pN</c>, or <c>{N}</c> for an IN list the runtime expands.</summary>
    private static string Renumber(string fragment, int offset, IReadOnlyList<string> kinds)
        => Placeholder.Replace(fragment, m =>
        {
            var local = int.Parse(m.Groups[1].Value);
            var global = offset + local;
            return kinds[local] == "List" ? "{" + global + "}" : "@p" + global;
        });

    /// <summary>
    ///     A translator's dialect pieces appended to the plan's, their texts renumbered like the fragment (the parameters from
    ///     <paramref name="parameterOffset" />, the tokens of pieces nested in pieces), and the fragment's <c>{dN}</c> tokens renumbered to the plan's.
    /// </summary>
    private string Absorb(LambdaTranslator translator, string fragment, int parameterOffset)
    {
        if (translator.DialectPieces.Count == 0)
            return fragment;
        var offset = _dialectPieces.Count;
        string Shift(string text) => PieceToken.Replace(Renumber(text, parameterOffset, translator.ParameterKinds), m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
        foreach (var piece in translator.DialectPieces)
            _dialectPieces.Add(new DialectPiece(Shift(piece.Sqlite), Shift(piece.PostgreSql), Shift(piece.SqlServer), Shift(piece.MySql), Shift(piece.Oracle)));
        return PieceToken.Replace(fragment, m => "{d" + (offset + int.Parse(m.Groups[1].Value)) + "}");
    }

    /// <summary>The <c>{dN}</c> tokens of a statement replaced by the dialect's rendering (a piece may hold another: resolved until none is left), before the quoting.</summary>
    private string Resolve(string sql, SqlDialect dialect)
    {
        for (var pass = 0; pass < 8 && _dialectPieces.Count > 0 && sql.IndexOf("{d", StringComparison.Ordinal) >= 0; pass++)
            sql = PieceToken.Replace(sql, m => _dialectPieces[int.Parse(m.Groups[1].Value)].For(dialect));
        return sql;
    }

    /// <summary>The dialect's quoting for a statement built outside a plan (the hierarchy walks).</summary>
    internal static string ForDialectText(string sql, SqlDialect dialect) => ForDialect(sql, dialect);

    /// <summary>Fragments quote identifiers with double quotes; SQL Server gets brackets. Values never appear in fragments, so every quote is an identifier's.</summary>
    private static string ForDialect(string sql, SqlDialect dialect)
    {
        sql = ResolveScopes(sql, dialect); // a Pragmatic entity's scopes against the caller's, before the quoting
        sql = SqlDialects.ResolveVectorCasts(sql, dialect); // the vector columns read as text, before the quoting
        sql = SqlDialects.ResolveRowVersions(sql, dialect); // a [ConcurrencyAware] row's version: xmin, rowversion or the column, as a bigint
        sql = SqlDialects.ResolveBooleanLiterals(sql, dialect); // a projection's literal booleans (a child's rules in an aggregate)
        sql = SqlDialects.InlineWindows(sql); // a window no statement named (a companion's): written in place
        if (dialect != SqlDialect.SqlServer || sql.IndexOf('"') < 0)
            return sql;
        var text = new StringBuilder(sql.Length);
        var open = true;
        foreach (var c in sql)
        {
            if (c == '"')
            {
                text.Append(open ? '[' : ']');
                open = !open;
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    internal static void Merge(List<string> joins, IReadOnlyList<string> more)
    {
        foreach (var join in more)
        {
            if (!joins.Contains(join))
                joins.Add(join);
        }
    }
}
