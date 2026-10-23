using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     <c>Distinct</c> and the set operations (<c>Union</c>, <c>Concat</c>, <c>Except</c>, <c>Intersect</c>): the operator's interceptor
///     validates the chain up to it with a throwaway plan (the members' shape, their ordering and paging, their projections) and
///     stores nothing but the other chain's slots, copied after this chain's. The plan at the terminal renders the members.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static bool EmitSetOperator(List<InterceptorModel> source,CallSite call, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics, bool grouped)
    {
        var name = call.Name;
        var where = call.Invocation.GetLocation();
        var elementType = call.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (grouped)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "a grouped chain ends in a Select of g.Key and the aggregates; Distinct and the set operations apply to rows or projections"));
            return false;
        }

        // The chain up to and including this call, as the plan will read it: the members' problems are reported here, where the operation is written.
        var steps = new List<Step>(call.Before) { new(name, call.Invocation, call.Method) };
        if (IsSetOperation(name))
        {
            var inner = new List<Step>();
            var innerEntity = call.Invocation.ArgumentList.Arguments.Count == 1 ? CallSite.Walk(call.Invocation.ArgumentList.Arguments[0].Expression, call.Model, default, inner, 0) : null;
            if (innerEntity is null || !SymbolEqualityComparer.Default.Equals(innerEntity, call.Entity))
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"the other chain must start at session.Query<{entity.Type.Name}>() and use the translated operators, written in place (a chain handed over by a method is opaque here)"));
                return false;
            }

            steps[steps.Count - 1].Inner = inner;
        }

        var probe = new PlanBuilder(entity, entities, call.Model.Compilation, valuesInSlots: false);
        probe.Prepare(steps, "");
        foreach (var step in steps)
        {
            if (probe.AddStep(step, call.Model))
                continue;
            if (probe.StepError is { } why)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, why));
            return false; // otherwise reported at the member's own call site
        }

        var anonymous = ContainsAnonymous(call.Element); // a joined row or an anonymous projection: the interceptor is generic in the element
        var element = anonymous ? "TElement" : elementType;
        var typeParameters = anonymous ? "<TElement>" : "";
        if (name == "Distinct")
        {
            source.Add(new(attribute, $"SqlQuery<{element}>", $"Distinct_{index}", typeParameters, [On(element)], "q"));
        }
        else
        {
            var offset = call.Before.Sum(s => s.SlotCount);
            var count = steps[steps.Count - 1].SlotCount;
            if (offset + count > MaxSlots)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument, the members' included; merge the predicates or the arguments"));
                return false;
            }

            source.Add(new(attribute, $"SqlQuery<{element}>", $"{name}_{index}", typeParameters, [On(element), $"SqlQuery<{element}> other"], count == 0 ? "q" : $"q.WithSlots(other, {offset}, {count})"));
        }

        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, probe.MembersSummary));
        return true;
    }
}
