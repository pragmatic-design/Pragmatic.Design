using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>The text of the plan class, and the helpers that turn fragments into the statement of a dialect.</summary>
internal sealed partial class PlanBuilder
{
    /// <summary>
    ///     The plan class: the command text per dialect (companion statements included), the bind, the reader, the loader.
    ///     <paramref name="origin" /> names the call site the plan belongs to (file, line, the chain as written), as the class's documentation:
    ///     the plan's name is a counter, the comment is how a reader of the generated file finds its way back.
    /// </summary>
    public TranslatedPlanModel ToPlan(string className, bool fileScoped = true, string? origin = null, string? site = null)
    {
        // The statements, then the filter families of Pragmatic's entities numbered in them (null: the plan reads none).
        var texts = new[] { Sql(SqlDialect.Sqlite), Sql(SqlDialect.PostgreSql), Sql(SqlDialect.SqlServer), _returning is not null || _rawFrom is not null ? null : Sql(SqlDialect.MySql), _returning is not null ? null : Sql(SqlDialect.Oracle) };
        var filters = NumberFamilies(texts);
        return new TranslatedPlanModel(
            className,
            PlanType(className),
            fileScoped,
            origin,
            _fields.ToEquatableArray(),
            _elementType,
            texts[0]!,
            texts[1]!,
            texts[2]!,
            texts[3],
            texts[4],
            _kinds.ToEquatableArray(),
            _binds.ToEquatableArray(),
            Reader(),
            HasLoads && !IsScalar ? Loader() : null,
            (site ?? className).Replace("*/", "* /") + " " + className,
            Reversed,
            ShapeColumns is not null,
            (ShapeColumns ?? []).Select(c => new ShapeColumnModel(c.Name, c.Kind)).ToEquatableArray(),
            filters,
            _predicateSlots.ToEquatableArray(),
            Registered: !IsAnonymous);
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
