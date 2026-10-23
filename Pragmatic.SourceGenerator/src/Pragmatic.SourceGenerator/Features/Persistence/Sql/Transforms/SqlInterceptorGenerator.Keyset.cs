using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>One key of a chain's ordering: its column (null for an aggregate after GroupBy), its SQL and its direction.</summary>
internal sealed class OrderKey
{
    public OrderKey(ColumnModel? column, string sql, bool descending)
    {
        Column = column;
        Sql = sql;
        Descending = descending;
    }

    public ColumnModel? Column { get; }

    public string Sql { get; }

    public bool Descending { get; }
}

/// <summary>Keyset pagination at a call site: <c>After(cursor)</c> and <c>Before(cursor)</c> validated against the ordering before them, the cursor stored in a slot.</summary>
internal static partial class SqlInterceptorGenerator
{
    /// <summary>The ordering's keys before a call, root first (an OrderBy resets, a ThenBy appends); null when a key could not be translated.</summary>
    internal static List<OrderKey>? OrderKeys(IEnumerable<Step> steps, SemanticModel model, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities)
    {
        var keys = new List<OrderKey>();
        foreach (var step in steps)
        {
            if (step.Name is not ("OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending"))
                continue;
            if (step.Lambda is null || !TryParameter(model, step.Lambda, out var parameter))
                return null;
            var translator = new LambdaTranslator(model, entities, entity, parameter);
            var column = translator.ColumnOf(Body(step.Lambda));
            if (column is null || translator.ParameterPaths.Count > 0)
                return null;
            if (!step.Name.StartsWith("Then", StringComparison.Ordinal))
                keys.Clear();
            keys.Add(new OrderKey(translator.LastColumn, column, step.Name.EndsWith("Descending", StringComparison.Ordinal)));
        }

        return keys;
    }

    /// <summary>
    ///     The cursor's shape against the keys: one key takes the value, several a tuple with one item per key, each of the key's type.
    ///     Null when they agree; the reason otherwise.
    /// </summary>
    internal static string? CursorProblem(ITypeSymbol cursorType, List<OrderKey> keys)
    {
        var items = cursorType is INamedTypeSymbol { IsTupleType: true } tuple ? tuple.TupleElements.Select(e => e.Type).ToList() : [cursorType];
        if (items.Count != keys.Count)
            return $"the cursor has {items.Count} value{(items.Count == 1 ? "" : "s")} and the ordering {keys.Count} key{(keys.Count == 1 ? "" : "s")}: pass {(keys.Count == 1 ? "the last row's value of the key" : "a tuple with the last row's values of the keys, in order")}";
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i].Column;
            if (key is null)
                return "the ordering is on an aggregate: a cursor continues an ordering on columns";
            if (!SymbolEqualityComparer.Default.Equals(EntityModel.Unwrap(items[i]), EntityModel.Unwrap(key.Type)))
                return $"value {i + 1} of the cursor is {items[i].ToDisplayString()}, key {i + 1} ({key.Property}) is {key.Type.ToDisplayString()}";
        }

        return null;
    }

    private static bool EmitCursor(List<InterceptorModel> source,CallSite call, string name, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics, bool grouped)
    {
        var where = call.Invocation.GetLocation();
        var elementType = call.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (grouped)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "a cursor continues an ordering on columns; after a GroupBy the ordering is on the key and the aggregates"));
            return false;
        }

        var keys = OrderKeys(call.Before, call.Model, entity, entities);
        if (keys is null)
            return false; // the ordering was reported at its own call site
        if (keys.Count == 0)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, call.Invocation.ArgumentList.ToString(), "cursor", $"{name} continues an ordering: put OrderBy/ThenBy before it, and pass {(name == "After" ? "the last" : "the first")} row's values of its keys"));
            return false;
        }

        var cursorType = call.Method.TypeArguments.Length == 1 ? call.Method.TypeArguments[0] : null;
        var problem = cursorType is null ? "the cursor's type could not be read" : CursorProblem(cursorType, keys);
        if (cursorType is null || problem is not null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, call.Invocation.ArgumentList.ToString(), "cursor", problem));
            return false;
        }

        // A total ordering ends with the entity's key: otherwise two rows equal on every key are in no fixed order, and a cursor between them loses one.
        var last = keys[keys.Count - 1].Column!;
        if (entity.Keys.Count != 1 || entity.Keys[0] != last)
            diagnostics.Add(Diagnostic.Create(Diagnostics.KeysetNotTotal, where, last.Property, entity.Type.Name, entity.KeyProperty));

        var slot = call.Before.Sum(s => s.SlotCount);
        if (slot >= MaxSlots)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
            return false;
        }

        source.Add(new(attribute, $"SqlQuery<{elementType}>", $"{name}_{index}", "", [On(elementType), $"{cursorType.ToDisplayString(Annotated)} cursor"], $"q.WithSlot({slot}, cursor)"));
        diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, string.Join(", ", keys.Select(k => k.Sql + (k.Descending ? " DESC" : "")))));
        return true;
    }
}
