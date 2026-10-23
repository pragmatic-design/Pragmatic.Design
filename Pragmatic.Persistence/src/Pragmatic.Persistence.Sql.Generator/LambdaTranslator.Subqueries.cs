using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     A chain written inside a lambda, ending in <c>Any</c>, <c>Count</c>, <c>Min/Max/Sum/Average</c> or <c>Contains</c>: rendered
///     as a subquery of the enclosing statement, under its own alias (<c>s1</c>, <c>s2</c>… by depth), its predicates translated by
///     nested translators that read the enclosing rows too (correlation) and share the enclosing lambda's parameters. The
///     chain starts at <c>session.Query&lt;T&gt;()</c> or at a declared chain (its parameters bound to the values written at the call,
///     through the enclosing lambda's rules: constants, the argument form); inside it, <c>Where</c>, <c>Select</c> (one column, for
///     IN) and <c>WithoutFilters</c>. Rules of visibility are not applied inside a subquery (ASSUMPTION: only a derived type's
///     fixed predicate); the caller filters as the chain says. A Pragmatic entity is filtered as everywhere else: its families and its
///     rules (fixed predicates), unless the inner chain says <c>WithoutFilters()</c>.
/// </summary>
internal sealed partial class LambdaTranslator
{
    private string? Subquery(InvocationExpressionSyntax invocation, IMethodSymbol method, string path)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return Fail(invocation, "a subquery is a chain written here, ending in Any, Count, Min, Max, Sum, Average or Contains");
        var steps = new List<SqlInterceptorGenerator.Step>();
        var rooted = SqlInterceptorGenerator.CallSite.Walk(access.Expression, _model, default, steps, 0);
        var entity = rooted is null ? null : _entities(rooted);
        if (entity is null)
            return Fail(invocation, "a subquery starts at session.Query<T>() of an [Entity], or at a declared chain, written here");
        var alias = "s" + (_depth + 1);
        var predicates = new List<string>();
        var joins = new List<string>();
        string? selected = null;
        if (entity.FixedPredicate is { } fixedPredicate)
            predicates.Add(fixedPredicate.Replace("t.\"", alias + ".\""));
        var live = entity.SoftDeletePredicate; // [SoftDelete]: the live rows, unless the inner chain reads past them
        var family = entity.FamilyPredicate; // a Pragmatic entity's tenant, owner and scopes, unless the inner chain says WithoutFilters()
        var rulesApply = entity.IsPragmatic; // and its rules (another entity's are not applied inside a subquery: the ASSUMPTION above)

        bool Apply(SqlInterceptorGenerator.Step step, Dictionary<IParameterSymbol, string>? bound)
        {
            switch (step.Name)
            {
                case "Where":
                {
                    if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(step.Model ?? _model, step.Lambda, out var parameter))
                        return Fail(step.Invocation, "inside a subquery a predicate takes the inner row only; the outer row and the argument are read directly, no second parameter") is not null;
                    var nested = Nested(entity, parameter, alias, step.Model, bound);
                    var predicate = nested.Translate(SqlInterceptorGenerator.Body(step.Lambda), $"Captured.Subquery({path})", asPredicate: true);
                    if (predicate is null)
                        return Fail(step.Lambda, nested.Error ?? "the subquery's predicate is outside the translated subset") is not null;
                    predicates.Add(predicate);
                    PlanBuilder.Merge(joins, nested._joins);
                    return true;
                }

                case "Select":
                {
                    if (step.Lambda is null || !SqlInterceptorGenerator.TryParameter(step.Model ?? _model, step.Lambda, out var parameter))
                        return Fail(step.Invocation, "inside a subquery a Select names one column (for IN)") is not null;
                    var nested = Nested(entity, parameter, alias, step.Model, bound);
                    selected = nested.ColumnOf(SqlInterceptorGenerator.Body(step.Lambda));
                    if (selected is null)
                        return Fail(step.Lambda, nested.Error ?? "the subquery's Select must name one column") is not null;
                    PlanBuilder.Merge(joins, nested._joins);
                    return true;
                }

                case "WithoutFilters":
                    live = null;
                    family = null;
                    rulesApply = false;
                    return true;

                case "IncludeDeleted":
                    live = null;
                    return true;

                case "AsNoTracking":
                    return true;

                case "Fragment":
                {
                    // A declared chain: its parameters stand for the values written at the call, as this lambda reads them (a constant, the argument's member).
                    if (bound is not null)
                        return Fail(step.Invocation, "a declared chain does not call another declared chain") is not null;
                    var arguments = step.Invocation.ArgumentList.Arguments;
                    var map = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
                    for (var j = 1; j < step.Method.Parameters.Length && j < arguments.Count; j++)
                    {
                        var text = ValueText(arguments[j].Expression, out _);
                        if (text is null)
                            return false;
                        map[step.Method.Parameters[j]] = $"({text})";
                    }

                    foreach (var inner in step.Inner ?? [])
                    {
                        if (!Apply(inner, map))
                            return false;
                    }

                    return true;
                }

                default:
                    return Fail(step.Invocation, $"'{step.Name}' is not translated inside a subquery (Where, Select for IN, WithoutFilters, a declared chain of those)") is not null;
            }
        }

        foreach (var step in steps)
        {
            if (!Apply(step, null))
                return null;
        }

        if (live is not null)
            predicates.Add(live.Replace("t.\"", alias + ".\""));
        if (family is not null)
            predicates.Add(family.Replace("t.\"", alias + ".\""));
        if (rulesApply && entity.Rules.Count > 0)
        {
            // A Pragmatic entity's rules: fixed predicates on its row (PRAG3030 refuses the others), so they need no parameter here.
            if (SqlInterceptorGenerator.RulesOn(entity, _entities, _model.Compilation, alias, out var ruleProblem) is not { } rules)
                return Fail(invocation, ruleProblem ?? "the visibility rules of the subquery's entity are outside the translated subset");
            predicates.Add(rules);
        }

        // The terminal: its own lambda, if any, translated like a step's.
        var arguments = invocation.ArgumentList.Arguments;
        string? inner = null;
        if (arguments.Count == 1 && arguments[0].Expression is LambdaExpressionSyntax lambda)
        {
            if (!SqlInterceptorGenerator.TryParameter(_model, lambda, out var parameter))
                return Fail(lambda, "the subquery's terminal takes a lambda over the inner row");
            var nested = Nested(entity, parameter, alias, null, null);
            inner = method.Name is "Any" or "Count" ? nested.Translate(SqlInterceptorGenerator.Body(lambda), $"Captured.Subquery({path})", asPredicate: true) : nested.ColumnOf(SqlInterceptorGenerator.Body(lambda));
            if (inner is null)
                return Fail(lambda, nested.Error ?? "the subquery's terminal lambda is outside the translated subset");
            PlanBuilder.Merge(joins, nested._joins);
        }

        LastColumn = null;
        LastColumnOptional = false;
        var from = $"FROM \"{entity.Table}\" {alias}{string.Concat(joins.Select(j => " " + j))}";
        string Where(string? extra)
        {
            var all = extra is null ? predicates : predicates.Concat([extra]).ToList();
            return all.Count == 0 ? "" : " WHERE " + string.Join(" AND ", all.Select(p => $"({p})"));
        }

        switch (method.Name)
        {
            case "Any":
                return $"EXISTS (SELECT 1 {from}{Where(inner)})";
            case "Count":
                return $"(SELECT COUNT(*) {from}{Where(inner)})";
            case "Min" or "Max" or "Sum" or "Average":
                if (inner is null)
                    return Fail(invocation, $"{method.Name} takes a lambda selecting the value");
                return $"(SELECT {method.Name.ToUpperInvariant().Replace("AVERAGE", "AVG")}({inner}) {from}{Where(null)})";
            default:
            {
                // Contains: the value, translated here (a column of this row, a value), IN the selected column.
                if (selected is null)
                    return Fail(invocation, "Contains on a subquery follows a Select of one column: chain.Select(x => x.Key).Contains(value)");
                if (arguments.Count != 1)
                    return Fail(invocation, "Contains takes the value to look for");
                var value = Translate(arguments[0].Expression, $"Captured.Argument({path}, 0)", asPredicate: false);
                return value is null ? null : $"{value} IN (SELECT {selected} {from}{Where(null)})";
            }
        }
    }

    /// <summary>True once a translation read a member of an enclosing row (a correlation); the caller resets it between the parts it tells apart.</summary>
    public bool ReadsOuter { get; set; }

    /// <summary>True when the expression is a member path rooted in <paramref name="parameter" />.</summary>
    public bool IsRootedIn(ExpressionSyntax expression, IParameterSymbol parameter)
        => Unwrap(expression) is MemberAccessExpressionSyntax member && Segments(member) is not null && SymbolEqualityComparer.Default.Equals(parameter, _parameter);

    /// <summary>True when the expression is a member path rooted in an enclosing lambda's row.</summary>
    public bool ReadsOuterRow(ExpressionSyntax expression)
        => Unwrap(expression) is MemberAccessExpressionSyntax member && !IsOnParameter(member) && !IsOnJoined(member) && _outer is not null && _outer.OwnsMember(member);

    /// <summary>Casts, parentheses and the null-forgiving <c>!</c> removed (the <see cref="Strip" /> of the translator, for callers outside it).</summary>
    public static ExpressionSyntax Unwrap(ExpressionSyntax expression) => Strip(expression);

    /// <summary>A translator over the subquery's row, reading the enclosing rows through this one; a declared chain's body binds in its own model with its parameters bound.</summary>
    private LambdaTranslator Nested(EntityModel entity, IParameterSymbol parameter, string alias, SemanticModel? model, Dictionary<IParameterSymbol, string>? bound)
    {
        var nested = new LambdaTranslator(this, entity, parameter, alias) { _outer = this };
        if (model is not null)
            nested._model = model;
        if (bound is not null)
        {
            foreach (var pair in bound)
                nested.BindParameter(pair.Key, pair.Value);
        }

        return nested;
    }
}
