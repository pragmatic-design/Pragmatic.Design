using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A bulk write of <c>IBulkRepository&lt;T&gt;</c>, planned without a chain written on <c>SqlQuery&lt;T&gt;</c>: the rows a specification selects
///     (a predicate composed at runtime, as <c>Matching</c>'s) and, for an update, the SET list of the setters' lambda written at the call,
///     its values recorded from the delegate when it runs (<c>BulkSetterValues</c>). Slots only (<c>valuesInSlots</c>).
/// </summary>
internal sealed partial class PlanBuilder
{
    private bool _recordedValues;

    /// <summary>The rows a predicate composed at runtime selects, read from the slot <paramref name="slotText" /> fills: <c>Matching</c> without a step.</summary>
    public void AddRuntimePredicate(string slotText)
    {
        ValueSlots.Add(slotText);
        var slot = ValueSlots.Count - 1;
        _predicates.Add("{dyn:" + slot + "}");
        _predicateSlots.Add(slot);
    }

    /// <summary>
    ///     A <c>[SoftDelete]</c> entity's bulk delete as the marking the unit of work writes for one row: the flag set, the instant and the caller
    ///     from the slots filled by <c>__values[0]</c> and <c>__values[1]</c> (<c>SqlBulk.DeleteAsync</c>), each column only when the entity has it.
    /// </summary>
    public void AddSoftDeleteMark()
    {
        var stamps = _entity.Stamps()!;
        _sets.Add($"\"{stamps.IsDeleted!.Column}\" = {{bt:1}}");
        if (stamps.DeletedAt is { } at)
            AddRecordedValue(at.Column, at.IsDateTime ? "((global::System.DateTimeOffset)__values[0]!).UtcDateTime" : "__values[0]", at.IsDateTime ? "DateTime" : "DateTimeOffset");
        if (stamps.DeletedBy is { } by)
            AddRecordedValue(by.Column, "__values[1]", "String");
    }

    private void AddRecordedValue(string column, string slotText, string kind)
    {
        var index = _kinds.Count;
        ValueSlots.Add(slotText);
        _binds.Add($"p[{index}].Value = global::Pragmatic.Persistence.Sql.Planning.SqlValue.ToDatabase(d, q.Slot({ValueSlots.Count - 1}));");
        _kinds.Add(kind);
        _sets.Add($"\"{column}\" = @p{index}");
    }

    /// <summary>The SET list of a bulk update's setters: as <see cref="AddSet" />, every value a slot filled from the values the delegate records.</summary>
    public bool AddRecordedSet(SemanticModel model, LambdaExpressionSyntax lambda, Location where, List<Diagnostic> diagnostics)
    {
        _recordedValues = true;
        try
        {
            return AddSet(model, lambda, where, diagnostics);
        }
        finally
        {
            _recordedValues = false;
        }
    }
}
