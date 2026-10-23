using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Operators on a joined row (after <c>Join</c>/<c>LeftJoin</c>, before a <c>Select</c> closes it): <c>Where</c>, the orderings,
///     <c>GroupBy</c>, <c>Select</c>, <c>After</c>, and the grouped forms. The call site is validated by replaying the chain into a
///     throwaway plan (the shape of the row is only known from the joins before it); the interceptor stores the slot and retypes,
///     generic in the element, which is usually an anonymous type.
/// </summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>True when the chain's row is a join's result: a Join after the last Select/Project (a GroupBy keeps it: its Select reads the row).</summary>
    internal static bool IsOpenRow(IReadOnlyList<Step> before)
    {
        var lastJoin = -1;
        var lastClose = -1;
        for (var i = 0; i < before.Count; i++)
        {
            if (OpensRow(before[i].Name))
                lastJoin = i;
            else if (before[i].Name is "Select" or "Project")
                lastClose = i;
        }

        return lastJoin > lastClose;
    }

    private static bool EmitOnRow(List<InterceptorModel> source,CallSite call, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var name = call.Name;
        var where = call.Invocation.GetLocation();
        if (name is "Include" or "Project")
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "Include and Project work on the entity's rows: put them before the join, or project the joined row's members with Select"));
            return false;
        }

        var steps = new List<Step>(call.Before) { new(name, call.Invocation, call.Method) };
        var probe = new PlanBuilder(entity, entities, call.Model.Compilation, valuesInSlots: false);
        probe.Prepare(steps, "");
        foreach (var step in steps)
        {
            if (probe.AddStep(step, call.Model))
                continue;
            if (ReferenceEquals(step, steps[steps.Count - 1]))
            {
                var why = probe.StepError ?? (probe.LastError is { } error ? $"'{probe.LastErrorExpression}' cannot be translated: {error}" : null);
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, why ?? "the lambda is outside the translated subset on a joined row"));
            }

            return false; // an earlier step: reported at its own call site
        }

        var arguments = call.Invocation.ArgumentList.Arguments;
        var takesArg = TakesArg(call.Method);
        var slot = call.Before.Sum(s => s.SlotCount);
        if ((takesArg || name is "After" or "Before") && slot >= MaxSlots)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
            return false;
        }

        const string grouping = "global::System.Linq.IGrouping<TKey, TSource>";
        InterceptorModel? interceptor = name switch
        {
            "Where" => takesArg
                ? new(attribute, "SqlQuery<TElement>", $"Where_{index}", "<TElement, TArg>", [On("TElement"), "Func<TElement, TArg, bool> predicate", "TArg arg"], $"q.WithSlot({slot}, arg)")
                : new(attribute, "SqlQuery<TElement>", $"Where_{index}", "<TElement>", [On("TElement"), "Func<TElement, bool> predicate"], "q"),
            "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" => new(attribute, "SqlQuery<TElement>", $"{name}_{index}", "<TElement, TKey>", [On("TElement"), "Func<TElement, TKey> key"], "q"),
            "GroupBy" => new(attribute, $"SqlQuery<{grouping}>", $"GroupBy_{index}", "<TSource, TKey>", [On("TSource"), "Func<TSource, TKey> key"], $"q.As<{grouping}>()"),
            "Select" => new(attribute, "SqlQuery<TResult>", $"Select_{index}", "<TSource, TResult>", [On("TSource"), "Func<TSource, TResult> projection"], "q.As<TResult>()"),
            "After" or "Before" => new(attribute, "SqlQuery<TElement>", $"{name}_{index}", "<TElement, TCursor>", [On("TElement"), "TCursor cursor"], $"q.WithSlot({slot}, cursor)"),
            "SelectMany" => new(attribute, "SqlQuery<TResult>", $"SelectMany_{index}", "<TSource, TCollection, TResult>", [On("TSource"), "Func<TSource, global::System.Collections.Generic.IEnumerable<TCollection>> collection", "Func<TSource, TCollection, TResult> result"], "q.As<TResult>()"),
            _ => null,
        };
        if (interceptor is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "not an operator of a joined row"));
            return false;
        }

        source.Add(interceptor);
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, arguments.Count > 0 ? arguments[0].ToString() : name));
        return true;
    }
}
