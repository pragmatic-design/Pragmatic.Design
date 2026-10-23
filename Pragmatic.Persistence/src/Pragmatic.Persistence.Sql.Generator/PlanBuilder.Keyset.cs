using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>
///     Keyset pagination in a plan: the ordering's keys are kept with their columns, and <c>After(cursor)</c> binds one
///     parameter per key from the cursor in its slot (the value, or the tuple's items) through the key column's
///     converter. The predicate is rendered per dialect after the chain's predicates and the rules, as the runtime
///     twin (<c>SqlKeyset</c>) does: a row-value comparison where the dialect has it and the directions agree, the
///     expanded form otherwise. <c>Before(cursor)</c> compares the other way and flips the ORDER BY, so the paging takes
///     the rows nearest before the cursor; the plan is marked reversed and turns the rows back once read.
/// </summary>
internal sealed partial class PlanBuilder
{
    private readonly List<OrderKey> _orderKeys = [];
    private string[]? _cursorParameters; // the @pN of the cursor's items, one per key; null without After/Before
    private bool _before; // Before: the keyset predicate and the ORDER BY in the ordering's reverse

    /// <summary>True for a Before plan: the rows come nearest-first and the runtime reverses them into the chain's order.</summary>
    public bool Reversed => _before;

    /// <summary>Records an ordering key with its column (<paramref name="column" /> null after GroupBy: an aggregate or g.Key).</summary>
    private void RecordOrdering(ColumnModel? column, string sql, bool descending, bool reset)
    {
        if (reset)
            _orderKeys.Clear();
        _orderKeys.Add(new OrderKey(column, sql, descending));
    }

    /// <summary>The After or Before step: the cursor in the next slot, its items bound as parameters in the keys' order. False when the call site was reported.</summary>
    private bool AddCursor(SqlInterceptorGenerator.Step step, bool before)
    {
        var cursorType = step.Method.TypeArguments.Length == 1 ? step.Method.TypeArguments[0] : null;
        if (cursorType is null || _orderKeys.Count == 0 || SqlInterceptorGenerator.CursorProblem(cursorType, _orderKeys) is not null || _groupKey is not null)
            return false;
        _before = before;
        if (before)
        {
            // The ORDER BY in reverse (_orderings runs parallel to _orderKeys: both are written by the ordering steps), so LIMIT takes the rows next to the cursor.
            for (var i = 0; i < _orderKeys.Count && i < _orderings.Count; i++)
                _orderings[i] = _orderKeys[i].Sql + (_orderKeys[i].Descending ? "" : " DESC");
        }

        int slot;
        if (_valuesInSlots)
        {
            ValueSlots.Add(step.Invocation.ArgumentList.Arguments[0].ToString()); // a [Query] definition: the cursor expression as written, stored by the generated method
            slot = ValueSlots.Count - 1;
        }
        else
        {
            slot = _slot++;
        }

        var variable = $"cursor{slot}";
        _binds.Add($"var {variable} = ({cursorType.ToDisplayString(SqlInterceptorGenerator.Annotated)})q.Slot({slot})!;");
        _cursorParameters = new string[_orderKeys.Count];
        for (var i = 0; i < _orderKeys.Count; i++)
        {
            var column = _orderKeys[i].Column!;
            var item = _orderKeys.Count == 1 ? variable : $"{variable}.Item{i + 1}";
            _cursorParameters[i] = "@p" + _kinds.Count;
            _binds.Add($"p[{_kinds.Count}].Value = global::Pragmatic.Persistence.Sql.SqlValue.ToDatabase(d, {column.Bind(item)});");
            _kinds.Add(LambdaTranslator.KindOf(column.Store));
        }

        return true;
    }

    /// <summary>The keyset predicate for a dialect; null without After/Before. Before compares the other way: each key's direction inverted.</summary>
    private string? KeysetPredicate(SqlDialect dialect)
        => _cursorParameters is null ? null : Keyset(dialect, _orderKeys.Select(k => k.Sql).ToList(), _orderKeys.Select(k => k.Descending != _before).ToList(), _cursorParameters);

    /// <summary>The generator's copy of <c>SqlKeyset.Predicate</c>: the two must emit the same text.</summary>
    internal static string Keyset(SqlDialect dialect, IReadOnlyList<string> keys, IReadOnlyList<bool> descending, IReadOnlyList<string> parameters)
    {
        static string Operator(bool desc) => desc ? "<" : ">";
        if (keys.Count == 1)
            return $"{keys[0]} {Operator(descending[0])} {parameters[0]}";
        var uniform = true;
        for (var i = 1; i < keys.Count; i++)
            uniform &= descending[i] == descending[0];
        if (uniform && dialect is not (SqlDialect.SqlServer or SqlDialect.Oracle)) // neither compares row values with < or >
            return $"({string.Join(", ", keys)}) {Operator(descending[0])} ({string.Join(", ", parameters)})";
        var terms = new List<string>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var comparison = $"{keys[i]} {Operator(descending[i])} {parameters[i]}";
            if (i == 0)
            {
                terms.Add(comparison);
                continue;
            }

            var equal = new List<string>(i);
            for (var j = 0; j < i; j++)
                equal.Add($"{keys[j]} = {parameters[j]}");
            terms.Add($"({string.Join(" AND ", equal)} AND {comparison})");
        }

        return string.Join(" OR ", terms);
    }
}
