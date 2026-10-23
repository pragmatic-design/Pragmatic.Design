using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The visibility rules in a plan: a derived type's fixed predicate and each [VisibleWhen] rule's tree, translated into the predicates once.</summary>
internal sealed partial class PlanBuilder
{
    /// <summary>The entity's visibility rules, unless the chain reads past them: each rule's tree is built once in the plan and read like a slot.</summary>
    public bool AddRules(Location where, List<Diagnostic> diagnostics)
    {
        if (_members is not null)
        {
            // The rules filter rows: each member's, never the wrapper's result.
            foreach (var (_, member) in _members)
            {
                if (!member.AddRules(where, diagnostics))
                    return false;
            }

            return true;
        }

        return ApplyRules(where, diagnostics);
    }

    private bool _rulesApplied; // the rules went in earlier (inside a DistinctOn's derived table): the terminal adds them no more

    /// <summary>The fixed predicate and the rules into the predicates, once; a failure goes to <paramref name="diagnostics" />, or to <see cref="StepError" /> when a step applies them.</summary>
    private bool ApplyRules(Location where, List<Diagnostic>? diagnostics)
    {
        if (_rulesApplied)
            return true;
        _rulesApplied = true;
        if (_entity.FixedPredicate is { } fixedText && OnAlias(fixedText) is var fixedPredicate && !_predicates.Contains(fixedPredicate))
            _predicates.Add(fixedPredicate); // a derived type's rows: not a filter the chain can read past
        if (_withoutFilters)
            return true;
        if (!_includeDeleted && _entity.SoftDeletePredicate is { } liveText && OnAlias(liveText) is var live && !_predicates.Contains(live))
            _predicates.Add(live); // [SoftDelete]: the live rows, unless IncludeDeleted()
        if (_entity.FamilyPredicate is { } familyText && OnAlias(familyText) is var family && !_predicates.Contains(family))
            _predicates.Add(family); // a Pragmatic entity's tenant, owner and scopes, each lifted per execution
        foreach (var rule in _entity.Rules)
        {
            if (_withoutRules.Contains(rule))
                continue; // WithoutFilter<TRule>()
            var lambda = SqlInterceptorGenerator.RuleLambda(rule, _compilation, out var model);
            if (lambda is null || model is null)
                return RuleProblem(where, diagnostics, rule.Name, "the rule's ToExpression must be an expression-bodied lambda in this compilation (a rule from a referenced assembly has no source to translate)");

            if (_entity.IsPragmatic && ReadsTheMoment(rule, _entity, where, diagnostics))
                return false;
            var field = RuleField(rule, _entity);
            var predicates = _predicates.Count;
            if (!Predicate(model, lambda, field, row: new EntityRow(_entity, _alias))) // the root's rows, whatever shape the joins made of the chain's row
                return RuleProblem(where, diagnostics, lambda.ToString(), $"the rule {rule.Name} is outside the translated subset{(LastError is null ? "" : $": '{LastErrorExpression}' {LastError}")}");
            for (var i = predicates; _entity.IsPragmatic && i < _predicates.Count; i++)
                _predicates[i] = _entity.MarkRule(rule, _predicates[i]); // its own slot: lifted by its name, or by Raw
        }

        return true;
    }

    /// <summary>
    ///     True, with PRAG3030, when a Pragmatic entity's rule reads a value of the moment (the caller, the clock): Pragmatic builds a rule once
    ///     into the EF model, a fixed predicate on the row (PRAG0718), and the caller's filters are the families. Probed by a translation of
    ///     its own: a value that is not a compile-time constant is one read when the statement runs.
    /// </summary>
    private bool ReadsTheMoment(INamedTypeSymbol rule, EntityModel entity, Location where, List<Diagnostic>? diagnostics)
    {
        var lambda = SqlInterceptorGenerator.RuleLambda(rule, _compilation, out var model);
        if (lambda is null || model is null || !SqlInterceptorGenerator.TryParameter(model, lambda, out var parameter))
            return false; // reported where the rule is translated
        var probe = new LambdaTranslator(model, _entities, new EntityRow(entity, "t"), parameter);
        if (probe.Predicate(SqlInterceptorGenerator.Body(lambda)) is null || probe.ParameterIsConstant.All(constant => constant))
            return false;
        if (diagnostics is null)
            StepError = $"visibility rule {rule.Name} of {entity.Type.Name}: a rule of a Pragmatic entity is a fixed predicate on the row, and this one reads a value of the moment (PRAG3030)";
        else
            diagnostics.Add(Diagnostic.Create(Diagnostics.RuleReadsTheMoment, where, rule.Name, entity.Type.Name));
        return true;
    }

    /// <summary>A predicate of the entity's model, written on <c>t</c>, on this builder's alias (a join's inner chain reads the table as <c>jN</c>).</summary>
    private string OnAlias(string predicate) => _alias == "t" ? predicate : predicate.Replace("t.\"", _alias + ".\"");

    private bool RuleProblem(Location where, List<Diagnostic>? diagnostics, string expression, string why)
    {
        if (diagnostics is null)
            StepError = $"visibility rule {expression}: {why}";
        else
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, expression, "visibility rule", why));
        return false;
    }
}
