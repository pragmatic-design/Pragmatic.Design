using Pragmatic.Persistence.Sql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A common table expression as the root of the plan (<c>session.With</c>): the query written in place becomes the named query the
///     statement reads (<c>FROM cte t</c>); with a step it is recursive, <c>tree</c>, the step joining the table to the rows already
///     found. The anchor is built as a set member (its own predicates, rules and slots); the rest of the chain works on the rows.
/// </summary>
internal sealed partial class PlanBuilder
{
    private string CteName => _cteLabel ?? (_cteStep is null ? "cte" : "tree");

    /// <summary>A With of a join's inner chain: its own name (<c>tree_j1</c>), the statement's WITH list holding it.</summary>
    private string? _cteLabel;

    /// <summary>The joined Withs of this statement, defined in its WITH list after its own.</summary>
    private List<PlanBuilder>? _hoisted;

    /// <summary>A With that is a set operation's member: defined in the outer statement's WITH list (named there), not in its own statement.</summary>
    private bool _cteHoisted;

    /// <summary>
    ///     A member of a set operation that starts at a With, or holds joined Withs: their definitions go to this statement's WITH list (SQL Server
    ///     takes no WITH inside a derived table, and the compound is one); the member reads its own by the name the list gives it.
    /// </summary>
    private void HoistMember(PlanBuilder member)
    {
        if (member._cte is not null)
        {
            member._cteHoisted = true;
            (_hoisted ??= []).Add(member);
        }

        if (member._hoisted is not null)
        {
            (_hoisted ??= []).AddRange(member._hoisted);
            member._hoisted = null;
        }
    }

    /// <summary>A recursive With's bound (<c>maxDepth</c>): the steps taken from the anchor's rows; -1 when unbounded.</summary>
    private int _cteDepth = -1;

    /// <summary>The tree's depth column (0 for the anchor's rows), named apart from any entity column.</summary>
    private const string DepthColumn = "\"__depth\"";

    /// <summary>The tree's path of keys from the anchor's row (<c>/k1/k2/</c>): what stops a cycle and orders depth-first.</summary>
    private const string PathColumn = "\"__path\"";

    /// <summary>The walk (<c>SqlTreeWalk</c> flags: 1 stop at cycles, 2 depth-first, 4 breadth-first); 0 for none.</summary>
    private int _cteWalk;

    /// <summary>The joins of the table row's navigations the step reads (all INNER), before the tree's join; null when it reads none.</summary>
    private string? _cteRowJoins;

    /// <summary>The joins of the tree row's navigations the step reads (all INNER), after the tree's join; null when it reads none.</summary>
    private string? _cteTreeJoins;

    /// <summary>The inner chain of a join starts at a With: named after the join's alias, defined in this statement's WITH list; the join reads it by name.</summary>
    private string HoistWith(PlanBuilder inner, string alias)
    {
        inner._cteLabel = (inner._cteStep is null ? "cte_" : "tree_") + alias;
        (_hoisted ??= []).Add(inner);
        return inner.CteName;
    }

    private bool AddWith(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        if (step.Inner is null)
        {
            StepError = "the query of With is written in place, starting at session.Query<T>() on the same entity";
            return false;
        }

        if (_raw is not null || _cte is not null || _predicates.Count > 0 || _joins.Count > 0)
        {
            StepError = "With is the root of a chain";
            return false;
        }

        var anchor = new PlanBuilder(this);
        anchor.Prepare(step.Inner, "");
        foreach (var inner in step.Inner)
        {
            if (anchor.AddStep(inner, model))
                continue;
            StepError = anchor.StepError ?? (anchor.LastError is { } why ? $"{anchor.LastErrorExpression}: {why}" : "the query of With could not be translated");
            return false;
        }

        if (!anchor.ApplyRules(Location.None, null)) // the entity's rules pick the anchor's rows; the tree's rows are read as they are
        {
            StepError = anchor.StepError;
            return false;
        }

        _slot = anchor._slot;
        var arguments = step.Invocation.ArgumentList.Arguments;
        var stepLambda = arguments.Count > 1 ? arguments[1].Expression as LambdaExpressionSyntax : null;
        if (stepLambda is not null && !TranslateStep(stepLambda, model))
            return false;
        // The constants by the parameter they bind to (an enum's constant is its integer, as maxDepth's is).
        var method = model.GetSymbolInfo(step.Invocation).Symbol as IMethodSymbol;
        for (var a = 1; a < arguments.Count; a++)
        {
            var argument = arguments[a];
            if (argument.Expression is LambdaExpressionSyntax)
                continue;
            var name = argument.NameColon?.Name.Identifier.ValueText ?? (method is not null && a < method.Parameters.Length ? method.Parameters[a].Name : null);
            var constant = model.GetConstantValue(argument.Expression);
            switch (name)
            {
                case "maxDepth" when constant.HasValue && constant.Value is int depth && stepLambda is not null:
                    _cteDepth = depth < 0 ? -1 : depth; // below 0: unbounded (the walk overload's default)
                    continue;
                case "walk" when constant.HasValue && constant.Value is int walk && stepLambda is not null:
                    _cteWalk = walk;
                    continue;
                case "materialized" when constant.HasValue && constant.Value is bool materialized:
                    _cteMaterialized = materialized;
                    continue;
            }

            StepError = "materialized, maxDepth and walk are constants; maxDepth and walk go with a recursive With (a step)";
            return false;
        }

        if (_cteWalk != 0)
        {
            if (_entity.HasCompositeKey)
            {
                StepError = "a walk keeps the path of the rows' keys: a single-column key";
                return false;
            }

            if ((_cteWalk & 6) == 6)
            {
                StepError = "a walk is depth-first or breadth-first, not both";
                return false;
            }

            if ((_cteWalk & 4) != 0)
                _orderings.Add("t." + DepthColumn); // level by level, then by path: a later OrderBy replaces it, a ThenBy follows it
            if ((_cteWalk & 6) != 0)
                _orderings.Add("t." + PathColumn);
        }

        if (_cteMaterialized)
        {
            TerminalEmulations.Add(("With(materialized: true)", SqlDialect.SqlServer, "SQL Server has no MATERIALIZED: the hint is dropped, the optimizer decides"));
            TerminalEmulations.Add(("With(materialized: true)", SqlDialect.MySql, "MySQL has no MATERIALIZED: the hint is dropped, the optimizer decides"));
            TerminalEmulations.Add(("With(materialized: true)", SqlDialect.Oracle, "Oracle's MATERIALIZE is an undocumented hint: dropped, the optimizer decides"));
        }
        _cte = anchor;
        return true;
    }

    /// <summary>The recursive step, <c>(t, tree) => …</c>: a predicate over the table's row (<c>t</c>) and a row of the tree (<c>tree</c>, the same entity under that alias).</summary>
    private bool TranslateStep(LambdaExpressionSyntax lambda, SemanticModel model)
    {
        var parameters = lambda is ParenthesizedLambdaExpressionSyntax parenthesized ? parenthesized.ParameterList.Parameters : default;
        if (parameters.Count != 2 || model.GetDeclaredSymbol(parameters[0]) is not IParameterSymbol row || model.GetDeclaredSymbol(parameters[1]) is not IParameterSymbol tree)
        {
            StepError = "the step of a recursive With takes two rows: the table's and the tree's ((c, tree) => c.ParentId == tree.Id)";
            return false;
        }

        var translator = Bound(new LambdaTranslator(model, _entities, new EntityRow(_entity, "t"), row)); // the step's table is `t` inside the definition, whatever this chain's alias (a join's jN)
        translator.WithJoined(tree, _entity, "tree", optional: false, derived: false);
        var on = translator.Predicate(SqlInterceptorGenerator.Body(lambda));
        if (on is null)
        {
            StepError = translator.Error is { } why ? $"{translator.ErrorExpression}: {why}" : "the step of With could not be translated";
            return false;
        }

        // A navigation the predicate needs a row for (a comparison under ANDs) is an INNER JOIN, as in any chain; one it may read as missing would be
        // a LEFT JOIN, which SQL Server refuses in a recursive member: refused here for the five dialects alike.
        string Inner(string join) => LeftJoinAlias.Match(join) is { Success: true } m && translator.InnerAliases.Contains(m.Groups[1].Value) ? "INNER" + join.Substring("LEFT".Length) : join;
        var rowJoins = translator.Joins.Select(Inner).ToList();
        // The tree row's navigations: INNER when its translator needed their row and the step has no OR and no NOT (which it cannot see).
        var plain = !SqlInterceptorGenerator.Body(lambda).DescendantNodesAndSelf().Any(n => n.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression) || n.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalNotExpression));
        var treeJoins = translator.JoinedJoins.Select(j => plain && LeftJoinAlias.Match(j) is { Success: true } m && translator.JoinedInnerAliases.Contains(m.Groups[1].Value) ? "INNER" + j.Substring("LEFT".Length) : j).ToList();
        if (rowJoins.Concat(treeJoins).Any(j => j.StartsWith("LEFT JOIN", StringComparison.Ordinal)))
        {
            StepError = "a navigation in the step of a recursive With is read in a comparison under ANDs (its row required): SQL Server allows no outer join in a recursive member";
            return false;
        }

        if (rowJoins.Count > 0)
            _cteRowJoins = string.Join(" ", rowJoins);
        if (treeJoins.Count > 0)
            _cteTreeJoins = string.Join(" ", treeJoins);
        var offset = _kinds.Count; // the step's values (its constants) bound after the anchor's, as a Where's
        for (var i = 0; i < translator.ParameterPaths.Count; i++)
        {
            _binds.Add($"p[{offset + i}].Value = global::Pragmatic.Persistence.Sql.Planning.SqlValue.ToDatabase(d, {Value(translator, i, null)});");
            _kinds.Add(translator.ParameterKinds[i]);
        }

        _cteStep = Absorb(translator, Renumber(on, offset, translator.ParameterKinds), offset);
        return true;
    }

    /// <summary>
    ///     The WITH list before the statement: its own named query and the joined ones, RECURSIVE once for the list where the dialect wants it
    ///     when one of them is recursive (SQL Server takes no WITH inside a derived table: every one is defined here, at the head).
    /// </summary>
    private string WithClause(SqlDialect dialect)
    {
        var all = new List<PlanBuilder>();
        if (_cte is not null)
            all.Add(this);
        if (_hoisted is not null)
            all.AddRange(_hoisted);
        var memberIndex = 0;
        foreach (var member in all.Where(b => b._cteHoisted))
            member._cteLabel = (member._cteStep is null ? "cte_m" : "tree_m") + (++memberIndex).ToString(System.Globalization.CultureInfo.InvariantCulture); // named here, before the members' statements read the names
        var sql = new StringBuilder("WITH ");
        if (all.Any(b => b._cteStep is not null) && dialect is not (SqlDialect.SqlServer or SqlDialect.Oracle))
            sql.Append("RECURSIVE ");
        sql.Append(string.Join(", ", all.Select(b => b.CteDefinition(dialect))));
        return sql.Append(' ').ToString();
    }

    /// <summary>A key as text for the path (a uuid, a number, a text alike).</summary>
    private static string KeyText(SqlDialect dialect, string key) => dialect switch
    {
        SqlDialect.SqlServer => $"CAST({key} AS NVARCHAR(400))",
        SqlDialect.MySql => $"CAST({key} AS CHAR)",
        SqlDialect.Oracle => $"CAST({key} AS VARCHAR2(400))",
        _ => $"CAST({key} AS TEXT)",
    };

    /// <summary>The path's type, the same in the anchor and the step (SQL Server, MySQL and Oracle take the recursive column's type from the anchor).</summary>
    private static string PathText(SqlDialect dialect, string text) => dialect switch
    {
        SqlDialect.SqlServer => $"CAST({text} AS NVARCHAR(4000))",
        SqlDialect.MySql => $"CAST({text} AS CHAR(4000))",
        SqlDialect.Oracle => $"CAST({text} AS VARCHAR2(4000))",
        _ => text,
    };

    /// <summary>Text joined: <c>+</c> on SQL Server, <c>||</c> elsewhere (MySQL's session has PIPES_AS_CONCAT).</summary>
    private static string Concat(SqlDialect dialect, params string[] parts) => string.Join(dialect == SqlDialect.SqlServer ? " + " : " || ", parts);

    /// <summary>The position of <paramref name="needle" /> in <paramref name="text" /> (0: not there), with no LIKE wildcards to escape.</summary>
    private static string Find(SqlDialect dialect, string text, string needle) => dialect switch
    {
        SqlDialect.SqlServer => $"CHARINDEX({needle}, {text})",
        SqlDialect.PostgreSql => $"strpos({text}, {needle})",
        _ => $"instr({text}, {needle})",
    };

    /// <summary>One named query of the WITH list: the anchor's statement (and the recursive step) under the dialect's keywords.</summary>
    private string CteDefinition(SqlDialect dialect)
    {
        var columns = string.Join(", ", _entity.Columns.Select(c => c.Select("t")));
        var sql = new StringBuilder();
        sql.Append(CteName);
        var depth = _cteDepth >= 0 || (_cteWalk & 4) != 0;
        var path = _cteWalk != 0;
        if (dialect == SqlDialect.Oracle)
            sql.Append(" (").Append(string.Join(", ", _entity.Columns.Select(c => "\"" + c.Column + "\""))).Append(depth ? ", " + DepthColumn : "").Append(path ? ", " + PathColumn : "").Append(')'); // Oracle's recursive WITH wants its columns named
        sql.Append(" AS ");
        if (_cteMaterialized && dialect is not (SqlDialect.SqlServer or SqlDialect.MySql or SqlDialect.Oracle))
            sql.Append("MATERIALIZED ");
        var key = KeyText(dialect, "t.\"" + _entity.KeyColumn + "\"");
        var anchorColumns = columns + (depth ? ", 0 AS " + DepthColumn : "") + (path ? ", " + PathText(dialect, Concat(dialect, "'/'", key, "'/'")) + " AS " + PathColumn : "");
        sql.Append('(').Append(_cte!.Statement(dialect, anchorColumns));
        if (_cteStep is not null)
        {
            sql.Append(" UNION ALL SELECT ").Append(columns).Append(depth ? ", tree." + DepthColumn + " + 1" : "").Append(path ? ", " + PathText(dialect, Concat(dialect, "tree." + PathColumn, key, "'/'")) : "")
                .Append(" FROM \"").Append(_entity.Table).Append("\" t ").Append(_cteRowJoins is null ? "" : _cteRowJoins + " ").Append("INNER JOIN ").Append(CteName).Append(_cteLabel is null ? "" : " tree"); // a named one aliased as the step reads it
            var conditions = new List<string>();
            if (_cteTreeJoins is null)
            {
                sql.Append(" ON (").Append(_cteStep).Append(')'); // the table row's navigations are joined before: the step stays the tree's ON
            }
            else
            {
                sql.Append(" ON 1 = 1 ").Append(_cteTreeJoins).Append(" AND (").Append(_cteStep).Append(')'); // the tree row's navigations come after its join: the step's predicate in the last one's ON
            }

            if (_cteDepth >= 0)
                conditions.Add("tree." + DepthColumn + " < " + _cteDepth.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if ((_cteWalk & 1) != 0)
                conditions.Add(Find(dialect, "tree." + PathColumn, Concat(dialect, "'/'", key, "'/'")) + " = 0"); // not back to a row on the path
            if (conditions.Count > 0)
                sql.Append(" WHERE ").Append(string.Join(" AND ", conditions));
        }
        sql.Append(')');
        if (dialect == SqlDialect.Oracle && _cteTreeJoins is not null)
            sql.Append(" CYCLE \"").Append(_entity.KeyColumn).Append("\" SET \"__cycle\" TO 'Y' DEFAULT 'N'"); // Oracle reads a join after the tree's as a cycle unless told to check the key
        return sql.ToString();
    }
}
