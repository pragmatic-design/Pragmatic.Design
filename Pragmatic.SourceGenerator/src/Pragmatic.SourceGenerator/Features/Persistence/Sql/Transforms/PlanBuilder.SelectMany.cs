using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     <c>SelectMany(x => collection, (x, i) => result)</c>: a row per element of a collection of the row. The collection is a
///     GroupJoin's group (its pending join, LEFT with <c>DefaultIfEmpty()</c>), a collection navigation (a JOIN on the foreign key),
///     or a chain correlated to the row, written in place: <c>LATERAL</c> on PostgreSQL, <c>APPLY</c> on SQL Server, and on SQLite a
///     derived table joined on the correlation with <c>ROW_NUMBER()</c> for the <c>Take</c> (the correlation must be equalities
///     between the inner and the outer row, which is the shape "the first N per parent" has). The result selector is the row
///     from here on, as after a join.
/// </summary>
internal sealed partial class PlanBuilder
{
    private bool AddSelectMany(SqlInterceptorGenerator.Step step, SemanticModel model)
    {
        var arguments = step.Invocation.ArgumentList.Arguments;
        if (arguments.Count != 2 || step.Method.TypeArguments.Length != 2 || arguments[0].Expression is not LambdaExpressionSyntax collectionLambda || arguments[1].Expression is not LambdaExpressionSyntax resultLambda
            || !SqlInterceptorGenerator.TryParameter(model, collectionLambda, out var rowParameter) || !SqlInterceptorGenerator.TryParameters(model, resultLambda, out var resultOuter, out var resultInner) || resultInner is null)
            return Refuse("SelectMany takes the collection of the row (x => x.Group.DefaultIfEmpty(), o => o.Lines, o => a chain correlated to o) and the result selector over both rows");
        if ((_projection is not null && !_rowOpen) || _groupKey is not null || _raw is not null)
            return Refuse("SelectMany works on rows (the entity's, or a previous join's): it comes before a Select or a GroupBy");

        // The collection, with or without DefaultIfEmpty() (LEFT).
        var collection = LambdaTranslator.Unwrap(SqlInterceptorGenerator.Body(collectionLambda));
        var left = false;
        if (collection is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "DefaultIfEmpty" } defaulted } && model.GetSymbolInfo(collection).Symbol is IMethodSymbol { ContainingType.Name: "Enumerable" or "Queryable" })
        {
            left = true;
            collection = LambdaTranslator.Unwrap(defaulted.Expression);
        }

        var translator = Bound(new LambdaTranslator(model, _entities, Row, rowParameter));
        RowShape? joined;
        RowShape outerRow = Row;
        if (translator.ShapeOf(collection) is GroupRow group)
        {
            // A GroupJoin's group: the join prepared there, emitted now; the group leaves the row.
            _joins.Add(group.Join.Join(left));
            joined = new EntityRow(group.Join.Entity, group.Join.Alias, left, group.Join.Derived);
            outerRow = Row is CompositeRow composite ? composite.Without(group) : Row;
        }
        else if (translator.CollectionAt(collection) is { } located)
        {
            // A collection navigation: a join on the foreign key, under its own alias.
            var child = _entities(located.Collection.Child);
            if (child is null)
                return Refuse($"{located.Collection.Child.Name} is not an [Entity] the generator could read");
            if (located.Collection.LinkProblem(located.Owner, child) is { } problem) // a composite key on either side: one column per key column
                return Refuse(problem);
            var alias = "j" + (++_joinCount);
            var foreignKey = located.Collection.ForeignKeyColumn(child);
            Merge(_joins, translator.Joins);
            string on;
            if (located.Collection.IsLink)
            {
                // Through a join table: the link on the parent's key, then the child on the link's child columns (both LEFT when the row may have none).
                var link = alias + "_l";
                _joins.Add($"{(left ? "LEFT" : "INNER")} JOIN \"{located.Collection.LinkTable}\" {link} ON {located.Collection.Matches(located.Owner, located.Alias, child, link)}");
                on = string.Join(" AND ", located.Collection.LinkChildColumns.Select((c, i) => $"{alias}.\"{child.Keys[i].Column}\" = {link}.\"{c}\""));
            }
            else
                on = located.Collection.Matches(located.Owner, located.Alias, child, alias); // every key column of a composite parent
            if (child.RowPredicate is { } fixedPredicate)
                on += " AND " + fixedPredicate.Replace("t.\"", alias + ".\""); // a derived type's rows, a [SoftDelete] child's live ones, a Pragmatic child's families
            if (child.IsPragmatic && child.Rules.Count > 0)
            {
                // A Pragmatic child's rules too: fixed predicates on its row (PRAG3030 refuses the others).
                if (SqlInterceptorGenerator.RulesOn(child, _entities, _compilation, alias, out var ruleProblem) is not { } rules)
                    return Refuse(ruleProblem ?? "the child's visibility rules are outside the translated subset");
                on += " AND " + rules;
            }

            _joins.Add($"{(left ? "LEFT" : "INNER")} JOIN \"{child.Table}\" {alias} ON {on}");
            joined = new EntityRow(child, alias, left);
        }
        else
        {
            if (translator.Error is not null && collection is not InvocationExpressionSyntax)
                return Refuse($"the collection: '{translator.ErrorExpression ?? collection.ToString()}' {translator.Error}");
            joined = Lateral(collection, translator, model, left);
            if (joined is null)
                return false;
        }

        var result = Bound(new LambdaTranslator(model, _entities, outerRow, resultOuter) { InlineConstants = true });
        result.WithJoined(resultInner, joined, left);
        return FinishJoinedRow(result, resultLambda, step.Method.TypeArguments[1]);
    }

    /// <summary>
    ///     A chain correlated to the row, as a derived table per dialect: <c>CROSS JOIN LATERAL (…)</c> / <c>LEFT JOIN LATERAL (…) ON true</c>,
    ///     <c>CROSS APPLY (…)</c> / <c>OUTER APPLY (…)</c>, and on SQLite (no LATERAL) the inner-only predicates in the derived table, the
    ///     correlation equalities in the ON, and <c>ROW_NUMBER() OVER (PARTITION BY the inner keys ORDER BY …)</c> for the Take.
    /// </summary>
    private EntityRow? Lateral(ExpressionSyntax chain, LambdaTranslator outer, SemanticModel model, bool left)
    {
        var steps = new List<SqlInterceptorGenerator.Step>();
        var rooted = SqlInterceptorGenerator.CallSite.Walk(chain, model, default, steps, 0);
        var entity = rooted is null ? null : _entities(rooted);
        if (entity is null)
        {
            Refuse("the collection of a SelectMany is a GroupJoin's group, a collection navigation, or a chain written here starting at session.Query<T>()");
            return null;
        }

        var alias = "j" + (++_joinCount);
        var inner = "l" + _joinCount;
        var innerOnly = new List<string>();
        var partition = new List<string>(); // the inner side of each correlation equality (SQLite's PARTITION BY)
        var correlationOn = new List<string>(); // jN.cK = outer column (SQLite's ON)
        var full = new List<string>(); // every predicate, correlated (LATERAL / APPLY)
        var orderings = new List<string>();
        var joins = new List<string>();
        string? take = null;
        var mixed = false; // a conjunct reads both rows without being an equality: no ranked derived table can hold it
        var switches = new List<SqlInterceptorGenerator.Step>(); // WithoutFilters, WithoutFilter<TRule>, IncludeDeleted: for the entity's own filters below
        foreach (var step in steps)
        {
            switch (step.Name)
            {
                case "Where":
                {
                    if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(step.Model ?? model, step.Lambda, out var parameter))
                    {
                        Refuse("inside a correlated chain a predicate takes the inner row only");
                        return null;
                    }

                    var nested = outer.NestedOver(entity, parameter, inner, step.Model);
                    foreach (var conjunct in Conjuncts(SqlInterceptorGenerator.Body(step.Lambda)))
                    {
                        nested.ReadsOuter = false;
                        var text = nested.Predicate(conjunct);
                        if (text is null)
                        {
                            Refuse($"'{nested.ErrorExpression ?? conjunct.ToString()}' in the correlated chain: {nested.Error}");
                            return null;
                        }

                        full.Add(text);
                        if (Correlation(conjunct, nested, parameter, model) is { } equality)
                        {
                            partition.Add(equality.Inner);
                            correlationOn.Add((entity.Columns.FindIndex(c => $"{inner}.\"{c.Column}\"" == equality.Inner), equality.Outer) is var (index, outerSql) && index >= 0 ? $"{alias}.c{index} = {outerSql}" : "");
                            if (correlationOn[correlationOn.Count - 1].Length == 0)
                            {
                                Refuse("the correlation of a SelectMany's chain is an equality between a column of the inner row and the outer row (l.OrderId == o.Id)");
                                return null;
                            }
                        }
                        else if (nested.ReadsOuter)
                        {
                            mixed = true; // in the full predicate: SQLite matches the key in a correlated subquery, MySQL takes LATERAL
                        }
                        else
                        {
                            innerOnly.Add(text);
                        }
                    }

                    Merge(joins, nested.Joins);
                    break;
                }

                case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
                {
                    if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(step.Model ?? model, step.Lambda, out var parameter))
                        return null;
                    var nested = outer.NestedOver(entity, parameter, inner, step.Model);
                    nested.InlineConstants = true;
                    var column = nested.ColumnOf(SqlInterceptorGenerator.Body(step.Lambda));
                    if (column is null || nested.Joins.Count > 0)
                    {
                        Refuse($"the ordering of a correlated chain is a column of the inner row: {nested.Error}");
                        return null;
                    }

                    if (!step.Name.StartsWith("Then", System.StringComparison.Ordinal))
                        orderings.Clear();
                    orderings.Add(column + (step.Name.EndsWith("Descending", System.StringComparison.Ordinal) ? " DESC" : ""));
                    break;
                }

                case "Take":
                {
                    var constant = model.GetConstantValue(step.Invocation.ArgumentList.Arguments[0].Expression);
                    if (!constant.HasValue || constant.Value is not int n)
                    {
                        Refuse("the Take of a correlated chain is a constant (the first N per row)");
                        return null;
                    }

                    take = n.ToString();
                    break;
                }

                case "WithoutFilters" or "WithoutFilter" or "IncludeDeleted":
                    switches.Add(step);
                    break;

                case "AsNoTracking":
                    break;

                default:
                    Refuse($"'{step.Name}' is not translated in a correlated chain (Where, OrderBy/ThenBy, Take, WithoutFilters, WithoutFilter<TRule>, IncludeDeleted)");
                    return null;
            }
        }

        // The fixed predicate, the live rows and the visibility rules of the inner entity, as a join's inner chain has them: predicates
        // of the inner row alone, under its alias; the builder continues this plan's parameters and slots.
        var ruled = new PlanBuilder(this, entity) { _alias = inner };
        foreach (var step in switches)
        {
            if (!ruled.AddStep(step, step.Model ?? model))
            {
                Refuse($"'{step.Name}' in the correlated chain: {ruled.StepError}");
                return null;
            }
        }

        if (!ruled.ApplyRules(Location.None, null))
        {
            Refuse($"the correlated chain's {ruled.StepError}");
            return null;
        }

        if (ruled._joins.Count > 0)
        {
            Refuse($"a visibility rule of {entity.Type.Name} reads another table: not translated in a correlated chain");
            return null;
        }

        // The rules' predicates are the plan's already (its parameters, lists and pieces numbered): tokens here, put in after the chain's
        // own values are renumbered into the plan, which would renumber them twice.
        _slot = ruled._slot;
        var ruleTokens = ruled._predicates.Select((_, i) => "{rule:" + i + "}").ToList();
        innerOnly.AddRange(ruleTokens);
        full.AddRange(ruleTokens);

        if (take is not null && orderings.Count == 0)
        {
            Refuse("a Take in a correlated chain needs an OrderBy: which N rows per parent");
            return null;
        }

        var columns = string.Join(", ", entity.Columns.Select((c, i) => $"{inner}.\"{c.Column}\" AS c{i}"));
        var from = $"FROM \"{entity.Table}\" {inner}{string.Concat(joins.Select(j => " " + j))}";
        string Where(List<string> predicates) => predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates.Select(p => $"({p})"));
        var orderBy = orderings.Count == 0 ? "" : " ORDER BY " + string.Join(", ", orderings);
        var lateral = $"(SELECT {columns} {from}{Where(full)}{orderBy}";
        var postgres = $"{(left ? "LEFT JOIN LATERAL" : "CROSS JOIN LATERAL")} {lateral}{(take is null ? "" : " LIMIT " + take)}) {alias}{(left ? " ON true" : "")}";
        var sqlServer = $"{(left ? "OUTER APPLY" : "CROSS APPLY")} {lateral}{(take is null ? "" : $" OFFSET 0 ROWS FETCH NEXT {take} ROWS ONLY")}) {alias}";
        var rowNumber = take is null ? "" : $", ROW_NUMBER() OVER ({(partition.Count == 0 ? "" : "PARTITION BY " + string.Join(", ", partition) + " ")}ORDER BY {string.Join(", ", orderings)}) AS rn";
        var on = correlationOn.Concat(take is null ? [] : [$"{alias}.rn <= {take}"]).ToList();
        string sqlite;
        if (mixed)
        {
            // SQLite has no LATERAL: the inner rows whose key the correlated subquery picks (the whole predicate, the ordering, the Take as LIMIT).
            if (entity.HasCompositeKey)
            {
                Refuse("a correlated chain whose predicate mixes the two rows without an equality matches the inner row by its key on SQLite: a single-column key");
                return null;
            }

            var keyIndex = entity.Columns.FindIndex(c => c.Column == entity.KeyColumn);
            sqlite = $"{(left ? "LEFT" : "INNER")} JOIN (SELECT {columns} FROM \"{entity.Table}\" {inner}) {alias} ON {alias}.c{keyIndex} IN (SELECT {inner}.\"{entity.KeyColumn}\" {from}{Where(full)}{orderBy}{(take is null ? "" : " LIMIT " + take)})";
        }
        else
        {
            sqlite = $"{(left ? "LEFT" : "INNER")} JOIN (SELECT {columns}{rowNumber} {from}{Where(innerOnly)}) {alias}{(on.Count == 0 ? " ON 1 = 1" : " ON " + string.Join(" AND ", on))}";
        }

        var bound = BindValues(outer, sqlite, postgres, sqlServer); // the chain's values (constants, the argument's members) as the plan's parameters
        for (var b = 0; b < bound.Length; b++)
        {
            for (var i = 0; i < ruleTokens.Count; i++)
                bound[b] = bound[b].Replace(ruleTokens[i], ruled._predicates[i]);
        }

        // MySQL takes the ranked derived table (its window form needs no FROM-clause variant), or LATERAL for a mixed predicate (it allows no LIMIT in an
        // IN subquery); Oracle has CROSS/OUTER APPLY with FETCH, as SQL Server.
        _dialectPieces.Add(new DialectPiece(bound[0], bound[1], bound[2], mixed ? bound[1] : bound[0], bound[2]));
        Merge(_joins, outer.Joins);
        _joins.Add("{d" + (_dialectPieces.Count - 1) + "}");
        return new EntityRow(entity, alias, left, derived: true);
    }

    /// <summary>The conjuncts of a predicate: the operands of its top-level <c>&amp;&amp;</c>, parentheses and <c>!</c>-free.</summary>
    private static IEnumerable<ExpressionSyntax> Conjuncts(ExpressionSyntax body)
    {
        body = LambdaTranslator.Unwrap(body);
        if (body is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } and)
            return Conjuncts(and.Left).Concat(Conjuncts(and.Right));
        return [body];
    }

    /// <summary>An equality between a column of the inner row and a column of the outer row, rendered on each side; null for any other predicate.</summary>
    private static (string Inner, string Outer)? Correlation(ExpressionSyntax conjunct, LambdaTranslator nested, IParameterSymbol innerParameter, SemanticModel model)
    {
        if (LambdaTranslator.Unwrap(conjunct) is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } equality)
            return null;
        var (innerSide, outerSide) = nested.IsRootedIn(equality.Left, innerParameter) ? (equality.Left, equality.Right) : nested.IsRootedIn(equality.Right, innerParameter) ? (equality.Right, equality.Left) : (null, null);
        if (innerSide is null || outerSide is null || nested.IsRootedIn(outerSide, innerParameter) || !nested.ReadsOuterRow(outerSide))
            return null;
        var inner = nested.ColumnOf(innerSide);
        var outer = nested.ColumnOf(outerSide);
        return inner is null || outer is null ? null : (inner, outer);
    }
}
