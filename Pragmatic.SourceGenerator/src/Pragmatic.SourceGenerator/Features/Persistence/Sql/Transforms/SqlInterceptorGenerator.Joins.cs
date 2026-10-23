using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     <c>Join</c> and <c>LeftJoin</c>: the call site is validated with a throwaway plan (the inner chain, the keys, the result
///     selector), and its interceptor copies the inner chain's slots after this chain's and retypes the chain to the result.
///     Generic in every type, as a Select into an anonymous type is: the result usually has no name.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    private static bool EmitJoin(List<InterceptorModel> source,CallSite call, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var name = call.Name;
        var where = call.Invocation.GetLocation();
        var steps = new List<Step>(call.Before) { new(name, call.Invocation, call.Method) };
        var inner = new List<Step>();
        var innerEntity = call.Invocation.ArgumentList.Arguments.Count >= 1 ? CallSite.Walk(call.Invocation.ArgumentList.Arguments[0].Expression, call.Model, default, inner, 0) : null;
        if (innerEntity is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "the inner chain must start at session.Query<TInner>() and use the translated operators, written in place (a chain handed over by a method is opaque here)"));
            return false;
        }

        steps[steps.Count - 1].Inner = inner;
        var probe = new PlanBuilder(entity, entities, call.Model.Compilation, valuesInSlots: false);
        probe.Prepare(steps, "");
        foreach (var step in steps)
        {
            if (probe.AddStep(step, call.Model))
                continue;
            if (probe.StepError is { } why)
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, why));
            return false;
        }

        var offset = call.Before.Sum(s => s.SlotCount);
        var count = steps[steps.Count - 1].SlotCount;
        if (offset + count > MaxSlots)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument, the inner chain's included; merge the predicates or the arguments"));
            return false;
        }

        var receiver = count == 0 ? "q" : $"q.WithSlots(inner, {offset}, {count})";
        var selector = name == "GroupJoin" ? "Func<TSource, global::System.Collections.Generic.IEnumerable<TInner>, TResult>" : "Func<TSource, TInner, TResult>";
        source.Add(new(attribute, "SqlQuery<TResult>", $"{name}_{index}", "<TSource, TInner, TKey, TResult>", [On("TSource"), "SqlQuery<TInner> inner", "Func<TSource, TKey> outerKey", "Func<TInner, TKey> innerKey", $"{selector} result"], $"{receiver}.As<TResult>()"));
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, probe.JoinsSummary));
        return true;
    }
}
