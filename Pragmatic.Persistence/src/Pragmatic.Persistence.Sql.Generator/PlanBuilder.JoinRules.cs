using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     The target's rules on a to-one navigation's join, as EF Core applies its query filters there: a derived type's rows, a [SoftDelete]
///     target's live ones and its [VisibleWhen] rules go in the join's ON clause, so a required navigation to a row the caller cannot see drops
///     the row reading it, an optional one reads it as null. A translator marks the join where it adds it (<c>{jf:alias}</c>); the marks are
///     resolved once the chain is complete, when <c>WithoutFilters()</c> and <c>WithoutFilter&lt;TRule&gt;()</c> are known: the rules' values
///     are bound after the chain's own (the parameters go by name, wherever the text puts them).
/// </summary>
internal sealed partial class PlanBuilder
{
    private static readonly Regex JoinMark = new(@"\{jf:([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

    private readonly List<PlanBuilder> _all; // every builder of the command (members, the anchor, an inner chain, the companions): their joins are resolved together
    private readonly Dictionary<string, EntityModel> _joinTargets = new(System.StringComparer.Ordinal); // a marked join's alias → the navigation's target

    /// <summary>The mark a navigation's join carries when its target filters its rows; empty when there is nothing to filter.</summary>
    private string MarkJoin(EntityModel target, string alias)
    {
        if (target.RowPredicate is null && target.Rules.Count == 0)
            return "";
        _joinTargets[alias] = target;
        return "{jf:" + alias + "}";
    }

    /// <summary>
    ///     The joins a declared DTO's flattened members added (one hop: <c>entity.Join(navigation, target)</c>, under the navigation's alias), marked
    ///     like a translator's: the text is the projection's, shared with the runtime registration, so the mark goes on this plan's copy.
    /// </summary>
    private void MarkDeclaredJoins(EntityModel owner)
    {
        foreach (var navigation in owner.Navigations)
        {
            if (_entities(navigation.Target) is not { } target)
                continue;
            var join = owner.Join(navigation, target);
            var at = _joins.IndexOf(join);
            if (at >= 0)
                _joins[at] = join + MarkJoin(target, navigation.Alias);
        }
    }

    /// <summary>The chain complete: the marks of every builder's joins resolved into their ON clauses. False with a diagnostic when a target's rule does not translate.</summary>
    public bool TryFinish(string terminal, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, Location where, List<Diagnostic> diagnostics)
    {
        if (!TryFinishPlan(terminal, method, arguments, model, where, diagnostics))
            return false;
        foreach (var builder in _all.ToList())
        {
            if (!builder.ResolveJoinMarks(where, diagnostics))
                return false;
        }

        return true;
    }

    private bool ResolveJoinMarks(Location where, List<Diagnostic> diagnostics)
    {
        // The statement's joins, and the texts a subquery's own joins live in: EXISTS (SELECT 1 … JOIN … {jf:alias} …) in a predicate, a
        // HAVING, an ordering, the select list.
        foreach (var texts in new[] { _joins, _predicates, _having, _orderings })
        {
            if (!ResolveMarks(texts, where, diagnostics))
                return false;
        }

        var select = new List<string> { _selectList };
        if (!ResolveMarks(select, where, diagnostics))
            return false;
        _selectList = select[0];
        return true;
    }

    private bool ResolveMarks(List<string> texts, Location where, List<Diagnostic> diagnostics)
    {
        for (var i = 0; i < texts.Count; i++) // a rule's own navigations add joins, marked in turn
        {
            var mark = JoinMark.Match(texts[i]);
            if (!mark.Success)
                continue;
            var alias = mark.Groups[1].Value;
            if (alias.Count(c => c == '_') > 8)
                return RuleProblem(where, diagnostics, alias, "the rules of the navigations' targets reach each other in a cycle: the joins would not end");
            var filter = JoinFilter(_joinTargets[alias], alias, where, diagnostics, out var failed);
            if (failed)
                return false;
            texts[i] = texts[i].Replace(mark.Value, filter is null ? "" : " AND " + filter);
            i--; // the same text again: another mark may follow
        }

        return true;
    }

    /// <summary>The target's filter under the join's alias: the derived type's rows always, the live rows and the rules unless the chain reads past them.</summary>
    private string? JoinFilter(EntityModel target, string alias, Location where, List<Diagnostic> diagnostics, out bool failed)
    {
        failed = false;
        var parts = new List<string>();
        if (target.FixedPredicate is { } fixedPredicate)
            parts.Add(fixedPredicate.Replace("t.\"", alias + ".\""));
        if (!_withoutFilters)
        {
            if (target.SoftDeletePredicate is { } live)
                parts.Add(live.Replace("t.\"", alias + ".\""));
            if (target.FamilyPredicate is { } family)
                parts.Add(family.Replace("t.\"", alias + ".\"")); // a Pragmatic entity's tenant, owner and scopes: on its join too, where EF's provider filters do not reach
            foreach (var rule in target.Rules)
            {
                if (_withoutRules.Contains(rule))
                    continue; // WithoutFilter<TRule>(): left out wherever the rule applies
                if (target.IsPragmatic && ReadsTheMoment(rule, target, where, diagnostics))
                {
                    failed = true;
                    return null;
                }
                var lambda = SqlInterceptorGenerator.RuleLambda(rule, _compilation, out var model);
                if (lambda is null || model is null || !SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter))
                {
                    failed = !RuleProblem(where, diagnostics, rule.Name, "the rule's ToExpression must be an expression-bodied lambda in this compilation (a rule from a referenced assembly has no source to translate)");
                    return null;
                }

                var field = RuleField(rule, target);
                var translator = Bound(new LambdaTranslator(model, _entities, new EntityRow(target, alias), parameter));
                var predicate = translator.Predicate(SqlInterceptorGenerator.Body(lambda));
                if (predicate is null)
                {
                    failed = !RuleProblem(where, diagnostics, lambda.ToString(), $"the rule {rule.Name} of {target.Type.Name}, on a navigation's join, is outside the translated subset{(translator.Error is null ? "" : $": '{translator.ErrorExpression}' {translator.Error}")}");
                    return null;
                }

                var offset = _kinds.Count;
                for (var i = 0; i < translator.ParameterPaths.Count; i++)
                {
                    _binds.Add($"p[{offset + i}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {Value(translator, i, field)});");
                    _kinds.Add(translator.ParameterKinds[i]);
                }

                var absorbed = Absorb(translator, Renumber(predicate, offset, translator.ParameterKinds), offset);
                parts.Add(target.IsPragmatic ? target.MarkRule(rule, absorbed) : absorbed);
                Merge(_joins, translator.Joins);
            }
        }

        return parts.Count == 0 ? null : string.Join(" AND ", parts.Select(p => $"({p})"));
    }

    /// <summary>The static field holding a rule's tree, built once in the plan; its entity is the rule's own (VisibilityRule&lt;T&gt;'s T: the root's, for a rule a derived type inherits).</summary>
    private string RuleField(INamedTypeSymbol rule, EntityModel entity)
    {
        var field = $"Rule_{_fields.Count}";
        var ruleEntity = entity.FullName;
        for (var baseType = rule.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType is { Name: "VisibilityRule", TypeArguments.Length: 1 })
            {
                ruleEntity = baseType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                break;
            }
        }

        _fields.Add($"private static readonly Expression<Func<{ruleEntity}, bool>> {field} = new {rule.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}().ToExpression();");
        return field;
    }
}
