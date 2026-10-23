using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Operators on a joined row (after <c>Join</c>/<c>LeftJoin</c>, before a <c>Select</c> closes it): <c>Where</c>, the orderings,
///     <c>GroupBy</c>, <c>Select</c>, <c>After</c>, and the grouped forms. The call site is validated by replaying the chain into a
///     throwaway plan (the shape of the row is only known from the joins before it); the interceptor stores the slot and retypes,
///     generic in the element, which is usually an anonymous type.
/// </summary>
public sealed partial class SqlInterceptorGenerator
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

    private static bool EmitOnRow(StringBuilder source, CallSite call, EntityModel entity, System.Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics)
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

        source.AppendLine($"        {attribute}");
        switch (name)
        {
            case "Where":
                source.AppendLine(takesArg
                    ? $"        internal static SqlQuery<TElement> Where_{index}<TElement, TArg>(this in SqlQuery<TElement> q, Func<TElement, TArg, bool> predicate, TArg arg) => q.WithSlot({slot}, arg);"
                    : $"        internal static SqlQuery<TElement> Where_{index}<TElement>(this in SqlQuery<TElement> q, Func<TElement, bool> predicate) => q;");
                break;
            case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
                source.AppendLine($"        internal static SqlQuery<TElement> {name}_{index}<TElement, TKey>(this in SqlQuery<TElement> q, Func<TElement, TKey> key) => q;");
                break;
            case "GroupBy":
                source.AppendLine($"        internal static SqlQuery<global::System.Linq.IGrouping<TKey, TSource>> GroupBy_{index}<TSource, TKey>(this in SqlQuery<TSource> q, Func<TSource, TKey> key) => q.As<global::System.Linq.IGrouping<TKey, TSource>>();");
                break;
            case "Select":
                source.AppendLine($"        internal static SqlQuery<TResult> Select_{index}<TSource, TResult>(this in SqlQuery<TSource> q, Func<TSource, TResult> projection) => q.As<TResult>();");
                break;
            case "After" or "Before":
                source.AppendLine($"        internal static SqlQuery<TElement> {name}_{index}<TElement, TCursor>(this in SqlQuery<TElement> q, TCursor cursor) => q.WithSlot({slot}, cursor);");
                break;
            case "SelectMany":
                source.AppendLine($"        internal static SqlQuery<TResult> SelectMany_{index}<TSource, TCollection, TResult>(this in SqlQuery<TSource> q, Func<TSource, global::System.Collections.Generic.IEnumerable<TCollection>> collection, Func<TSource, TCollection, TResult> result) => q.As<TResult>();");
                break;
            default:
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "not an operator of a joined row"));
                return false;
        }

        source.AppendLine();
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, arguments.Count > 0 ? arguments[0].ToString() : name));
        return true;
    }
}
