using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     The filter families of Pragmatic's entities in a plan (<see cref="EntityModel.FamilyPredicate" />): the slots the statements read,
///     numbered, their marks rewritten to the runtime's (<c>{f:k}</c>…<c>{/f:k}</c>), the values (<c>{ft}</c> tenant, <c>{fu}</c> user,
///     <c>{fs}</c> scopes, <c>{fn}</c> the instant) made the parameters after the plan's own, and the <c>SqlPlanFilters</c> the plan declares.
/// </summary>
internal sealed partial class PlanBuilder
{
    private static readonly Regex FamilyMark = new(@"\{fam:([^}]*)\}", RegexOptions.Compiled);

    /// <summary>The slots holding a predicate composed at runtime (<c>Matching</c>): the plan's <c>predicates:</c>, the runtime renders each in its mark.</summary>
    private readonly List<int> _predicateSlots = [];

    private static readonly Regex ScopesToken = new(@"\{scopes:([^}]*)\}", RegexOptions.Compiled);

    /// <summary>
    ///     The overlap of a row's scopes (an array column) with the caller's (<c>{fs}</c>, one parameter: a native array on PostgreSQL, the JSON
    ///     text elsewhere), per dialect: what <c>entity.AccessScopes.Any(s =&gt; userScopes.Contains(s))</c> is on EF. Before the quoting.
    /// </summary>
    internal static string ResolveScopes(string sql, SqlDialect dialect)
        => sql.IndexOf("{scopes:", StringComparison.Ordinal) < 0
            ? sql
            : ScopesToken.Replace(sql, m => dialect switch
            {
                SqlDialect.Sqlite => $"EXISTS (SELECT 1 FROM json_each({m.Groups[1].Value}) WHERE value IN (SELECT value FROM json_each({{fs}})))",
                SqlDialect.PostgreSql => $"({m.Groups[1].Value} && {{fs}})",
                SqlDialect.MySql => $"JSON_OVERLAPS({m.Groups[1].Value}, CAST({{fs}} AS JSON))",
                SqlDialect.Oracle => $"EXISTS (SELECT 1 FROM JSON_TABLE({m.Groups[1].Value}, '$[*]' COLUMNS (\"value\" VARCHAR2(4000) PATH '$')) je WHERE je.\"value\" IN (SELECT ju.\"value\" FROM JSON_TABLE({{fs}}, '$[*]' COLUMNS (\"value\" VARCHAR2(4000) PATH '$')) ju))",
                _ => $"EXISTS (SELECT 1 FROM OPENJSON({m.Groups[1].Value}) WHERE value IN (SELECT value FROM OPENJSON({{fs}})))",
            });

    /// <summary>
    ///     The statements with their slots numbered (in the ordinal order of their keys, the same in every dialect) and their values on the
    ///     parameters after the plan's own; the plan's filters, or null when no statement reads a family.
    /// </summary>
    private PlanFiltersModel? NumberFamilies(string?[] texts)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            if (text is not null)
                foreach (Match match in FamilyMark.Matches(text))
                    keys.Add(match.Groups[1].Value);
        }

        if (keys.Count == 0)
            return null;
        // INVESTIGATE: a statement over more than 32 filtered slots (two bits each in the mask) has no form yet; none of the known shapes comes close.
        if (keys.Count > 32)
            throw new InvalidOperationException($"A statement reads {keys.Count} filter slots of Pragmatic's entities; a plan holds at most 32.");
        var values = _kinds.Count;
        var slots = keys.ToList();
        for (var t = 0; t < texts.Length; t++)
        {
            if (texts[t] is not { } text)
                continue;
            for (var i = 0; i < slots.Count; i++)
                text = text.Replace("{fam:" + slots[i] + "}", "{f:" + Index(i) + "}").Replace("{/fam:" + slots[i] + "}", "{/f:" + Index(i) + "}");
            texts[t] = text.Replace("{ft}", "@p" + Index(values)).Replace("{fu}", "@p" + Index(values + 1)).Replace("{fs}", "@p" + Index(values + 2)).Replace("{fn}", "@p" + Index(values + 3));
        }

        return new PlanFiltersModel(slots.Select(Slot).ToEquatableArray(), values);
    }

    /// <summary>The statement as the plan declares it, what PRAG3099 shows: its filter slots numbered, their values on their parameters.</summary>
    public string DeclaredSql(SqlDialect dialect)
    {
        var texts = new string?[] { Sql(dialect) };
        NumberFamilies(texts);
        return texts[0]!;
    }

    /// <summary>A slot from its key: <c>family|entity|filter class|name|bypass</c>.</summary>
    private static FilterSlotModel Slot(string key)
    {
        var part = key.Split('|');
        return new FilterSlotModel(part[0], part[1], part[2], part[3], part[4]);
    }

    private static string Index(int value) => value.ToString(CultureInfo.InvariantCulture);
}
