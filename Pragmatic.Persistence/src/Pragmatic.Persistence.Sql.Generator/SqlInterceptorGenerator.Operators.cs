using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>The operators between the root and the terminal: Where stores its predicate, OrderBy and Include are validated and passed through, Select and Project retype the chain.</summary>
public sealed partial class SqlInterceptorGenerator
{
    // --- operators: store the predicate, validate the lambda, retype the chain ---

    /// <summary>The GROUP BY key of the chain before a call: the columns its GroupBy lambda names.</summary>
    internal static GroupKey? ReadGroupKey(CallSite call, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities)
    {
        var step = call.Before.LastOrDefault(s => s.Name == "GroupBy");
        return step?.Lambda is null ? null : GroupKey.TryRead(call.Model, step.Lambda, step.Method, entity, entities, new List<string>(), out _);
    }

    /// <summary>True for an anonymous type, or a type with one among its type arguments (a grouping on an anonymous key): no interceptor can name it, so the interceptor is generic in the element.</summary>
    private static bool ContainsAnonymous(ITypeSymbol type)
        => type.IsAnonymousType || type is INamedTypeSymbol named && named.TypeArguments.Any(ContainsAnonymous);

    /// <summary>PRAG3012 for every catalog function a lambda uses whose rendering on a dialect is an emulation: the dialect, the function, the note.</summary>
    internal static void ReportEmulations(LambdaTranslator translator, Location where, List<Diagnostic> diagnostics)
    {
        foreach (var (function, dialect, note) in translator.Emulations)
            diagnostics.Add(Diagnostic.Create(Diagnostics.Emulated, where, function, dialect, note));
    }

    private static bool EmitOperator(StringBuilder source, CallSite call, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, int index, string attribute, List<Diagnostic> diagnostics)
    {
        var name = call.Name;
        var where = call.Invocation.GetLocation();
        var elementType = call.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var arguments = call.Invocation.ArgumentList.Arguments;

        // Everything after a Select works on the projection: only Skip, Take and the terminals apply there. After a GroupBy the
        // element is the grouping: a Where on it is HAVING, an OrderBy orders by an aggregate, and the Select reads the key and the aggregates.
        var grouped = call.Element is INamedTypeSymbol { Name: "IGrouping", TypeArguments.Length: 2 };
        var setLike = name == "Distinct" || IsSetOperation(name);
        var open = IsOpenRow(call.Before); // after a join: the operator works on the joined row, validated by replaying the chain
        if ((open && !setLike && !IsJoin(name)) || name == "SelectMany")
            return EmitOnRow(source, call, entity, entities, index, attribute, diagnostics);
        if (!open && !SymbolEqualityComparer.Default.Equals(call.Element, call.Entity) && !(grouped && name is "Select" or "Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending") && !(setLike && !grouped))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "after a Select only Skip, Take, Distinct, the set operations and the terminal operators are translated; put the filter and the ordering before the projection"));
            return false;
        }

        if (setLike)
            return EmitSetOperator(source, call, entity, entities, index, attribute, diagnostics, grouped);
        if (IsJoin(name))
            return EmitJoin(source, call, entity, entities, index, attribute, diagnostics);

        if (name == "Project")
        {
            var dto = call.Method.TypeArguments.Length == 1 ? call.Method.TypeArguments[0] as INamedTypeSymbol : null;
            var declared = dto is null ? null : Projection.TryDeclared(dto, entity, entities, new List<string>(), diagnostics, where, call.Model.Compilation);
            if (declared is null)
                return false;
            source.AppendLine($"        {attribute}");
            source.AppendLine($"        internal static SqlQuery<{declared.ResultName}> Project_{index}(this in SqlQuery<{elementType}> q) => q.As<{declared.ResultName}>();");
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, declared.ColumnList));
            return true;
        }

        if (name is "After" or "Before")
            return EmitCursor(source, call, name, entity, entities, index, attribute, diagnostics, grouped);

        if (name == "AsOf")
        {
            // The instant in the next slot; the plan binds it as the FOR SYSTEM_TIME parameter on SQL Server, and notes the other dialects read the current rows.
            var slot = call.Before.Sum(s => s.SlotCount);
            if (slot >= MaxSlots)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
                return false;
            }

            source.AppendLine($"        {attribute}");
            source.AppendLine($"        internal static SqlQuery<{elementType}> AsOf_{index}(this in SqlQuery<{elementType}> q, global::System.DateTime utcInstant) => q.WithSlot({slot}, utcInstant);");
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, $"FOR SYSTEM_TIME AS OF @p (SQL Server); the current rows elsewhere"));
            return true;
        }

        if (name is "Matching" or "OrderedBy")
        {
            // A predicate or an ordering composed at runtime from compiled parts, in the next slot: the plan marks where it goes, the runtime renders it.
            var slot = call.Before.Sum(s => s.SlotCount);
            if (slot >= MaxSlots)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
                return false;
            }

            source.AppendLine($"        {attribute}");
            var composed = name == "Matching" ? "SqlPredicate" : "SqlOrdering";
            source.AppendLine($"        internal static SqlQuery<{elementType}> {name}_{index}(this in SqlQuery<{elementType}> q, global::Pragmatic.Persistence.Sql.{composed}<{elementType}> composed) => q.WithSlot({slot}, composed);");
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, name == "Matching" ? "the predicate composed at runtime from compiled leaves" : "the ordering composed at runtime from compiled keys"));
            return true;
        }

        if (name == "OfType")
        {
            // The chain narrows to one derived type of the hierarchy: from here on it is that type's (the call site's Walk roots the later calls in it), its predicate on the discriminator.
            var narrowed = call.Method.TypeArguments.Length == 1 ? call.Method.TypeArguments[0] as INamedTypeSymbol : null;
            var derived = narrowed is null || !SymbolEqualityComparer.Default.Equals(narrowed.BaseType, entity.Type) ? null : entities(narrowed);
            if (derived?.DiscriminatorPredicate is null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"OfType<T>() narrows the root of a hierarchy in one table to one of its derived types ({narrowed?.Name ?? "?"} is not a derived type of {entity.Type.Name})"));
                return false;
            }

            source.AppendLine($"        {attribute}");
            source.AppendLine($"        internal static SqlQuery<{derived.FullName}> OfType_{index}(this in SqlQuery<{elementType}> q) => q.As<{derived.FullName}>();");
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, derived.FixedPredicate ?? $"FROM \"{derived.Table}\""));
            return true;
        }

        if (name == "Include")
        {
            var hops = IncludeHops(call.Invocation, call.Model, entity, entities, out var error);
            if (hops is null)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, call.Invocation.ArgumentList.ToString(), "include", error ?? "Include takes a navigation path of the entity"));
                return false;
            }

            // The call's own parameter types, substituted and with their nullable annotations (the interceptor must match them): not generic, whatever the overload's arity.
            var annotated = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
            var parameters = string.Join(", ", call.Method.Parameters.Select((p, i) => $"{p.Type.ToDisplayString(annotated)} {p.Name}"));
            source.AppendLine($"        {attribute}");
            source.AppendLine($"        internal static SqlQuery<{elementType}> Include_{index}(this in SqlQuery<{elementType}> q, {parameters}) => q;");
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, string.Join(".", hops.Select(h => h.Property))));
            return true;
        }

        if (name == "DistinctOn")
        {
            // Validated by replaying the chain with the step (its place, its two lambdas); the interceptor is generic in the lambdas' results and stores nothing.
            var probe = new PlanBuilder(entity, entities, call.Model.Compilation, valuesInSlots: false);
            var steps = new List<Step>(call.Before) { new(name, call.Invocation, call.Method) };
            probe.Prepare(steps, "");
            foreach (var step in steps)
            {
                if (probe.AddStep(step, call.Model))
                    continue;
                if (probe.StepError is { } why)
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, why));
                return false;
            }

            source.AppendLine($"        {attribute}");
            source.AppendLine($"        internal static SqlQuery<TSource> DistinctOn_{index}<TSource, TKey, TOrder>(this in SqlQuery<TSource> q, Func<TSource, TKey> key, Func<TSource, TOrder> order) => q;"); // the arity is the type's and the method's together, as the interceptors of Select
            source.AppendLine();
            diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, "DISTINCT ON (PostgreSQL) / ROW_NUMBER() OVER (PARTITION BY key ORDER BY order) = 1"));
            return true;
        }

        if (arguments.Count < 1 || arguments[0].Expression is not LambdaExpressionSyntax lambda || !TryParameters(call.Model, lambda, out var parameter, out var arg) || (arg is not null && name is not ("Where" or "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, arguments.Count > 0 ? arguments[0].ToString() : name, name == "Where" ? "predicate" : name == "Select" ? "projection" : "key", "only a lambda over the row is translated (a predicate may take the argument as its second parameter); a delegate passed in from outside is opaque at compile time"));
            return false;
        }

        var groupKey = grouped && name != "Select" ? ReadGroupKey(call, entity, entities) : null;
        if (grouped && name != "Select" && groupKey is null)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, lambda.ToString(), name == "Where" ? "having" : "key", "the GroupBy key before it could not be translated (see the diagnostics on it)"));
            return false;
        }

        // The operator's interceptor only validates and stores: the plan at the terminal translates the chain again, with the slot numbers.
        var translator = new LambdaTranslator(call.Model, entities, entity, parameter, groupKey, arg, arg is null ? null : "arg", inline: name == "Where" || arg is not null) { InlineConstants = name != "Where" };
        switch (name)
        {
            case "Where":
            {
                ReportConstantPredicate(call.Model, lambda, diagnostics); // before the translation: a predicate that reads no row is reported whether or not it translates
                var predicate = translator.Predicate(Body(lambda));
                if (predicate is null)
                {
                    diagnostics.Add(Diagnostics.Untranslated(where, translator.ErrorExpression ?? lambda.ToString(), "predicate", translator.Error ?? "the predicate could not be translated"));
                    return false;
                }

                ReportEmulations(translator, where, diagnostics);

                var slot = call.Before.Sum(s => s.SlotCount);
                if (arg is not null && slot >= MaxSlots)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
                    return false;
                }

                var parameters = string.Join(", ", call.Method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}"));
                source.AppendLine($"        {attribute}");
                if (ContainsAnonymous(call.Element))
                    source.AppendLine($"        internal static SqlQuery<TElement> Where_{index}<TElement{(arg is null ? "" : ", TArg")}>(this in SqlQuery<TElement> q, Func<TElement, {(arg is null ? "" : "TArg, ")}bool> predicate{(arg is null ? "" : ", TArg arg")}) => {(arg is null ? "q" : $"q.WithSlot({slot}, arg)")};"); // a grouping on an anonymous key: the element has no name
                else
                    source.AppendLine($"        internal static SqlQuery<{elementType}> Where_{index}(this in SqlQuery<{elementType}> q, {parameters}) => {(arg is null ? "q" : $"q.WithSlot({slot}, arg)")};");
                source.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, predicate));
                return true;
            }

            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
            {
                var column = translator.ColumnOf(Body(lambda));
                if (column is null || (arg is null && translator.ParameterPaths.Count > 0))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, translator.ErrorExpression ?? lambda.ToString(), "key", translator.Error ?? "an ORDER BY key must be a column or a catalog function of columns and constants; a value of the caller goes in the argument form, OrderBy((x, a) => …, a)"));
                    return false;
                }

                ReportEmulations(translator, where, diagnostics);
                var slot = call.Before.Sum(s => s.SlotCount);
                if (arg is not null && slot >= MaxSlots)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, $"a chain holds at most {MaxSlots} operators with an argument; merge the predicates or the arguments"));
                    return false;
                }

                var keyType = call.Method.TypeArguments.Length >= 1 ? call.Method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : "object";
                var receiver = arg is null ? "q" : $"q.WithSlot({slot}, arg)"; // the argument form stores the value in the chain's slot, as Where does
                source.AppendLine($"        {attribute}");
                if (ContainsAnonymous(call.Element))
                    source.AppendLine(arg is null
                        ? $"        internal static SqlQuery<TElement> {name}_{index}<TElement, TKey>(this in SqlQuery<TElement> q, Func<TElement, TKey> key) => q;"
                        : $"        internal static SqlQuery<TElement> {name}_{index}<TElement, TKey, TArg>(this in SqlQuery<TElement> q, Func<TElement, TArg, TKey> key, TArg arg) => {receiver};");
                else if (arg is null)
                    source.AppendLine($"        internal static SqlQuery<{elementType}> {name}_{index}(this in SqlQuery<{elementType}> q, Func<{elementType}, {keyType}> key) => q;");
                else
                    source.AppendLine($"        internal static SqlQuery<{elementType}> {name}_{index}(this in SqlQuery<{elementType}> q, {string.Join(", ", call.Method.Parameters.Select(p => $"{p.Type.ToDisplayString(Annotated)} {p.Name}"))}) => {receiver};");
                source.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, column));
                return true;
            }

            case "GroupBy":
            {
                var key = GroupKey.TryRead(call.Model, lambda, call.Method, entity, entities, new List<string>(), out var keyError);
                if (key is null)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, lambda.ToString(), "group key", keyError ?? "a GROUP BY key must be a column, or an anonymous object or a tuple of columns"));
                    return false;
                }

                source.AppendLine($"        {attribute}");
                if (key.Type.IsAnonymousType)
                {
                    source.AppendLine($"        internal static SqlQuery<global::System.Linq.IGrouping<TKey, TSource>> GroupBy_{index}<TSource, TKey>(this in SqlQuery<TSource> q, Func<TSource, TKey> key) => q.As<global::System.Linq.IGrouping<TKey, TSource>>();"); // an anonymous key has no name: generic, inferred at the call
                }
                else
                {
                    var keyType = key.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var grouping = $"global::System.Linq.IGrouping<{keyType}, {elementType}>";
                    source.AppendLine($"        internal static SqlQuery<{grouping}> GroupBy_{index}(this in SqlQuery<{elementType}> q, Func<{elementType}, {keyType}> key) => q.As<{grouping}>();");
                }

                source.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, key.GroupBy));
                return true;
            }

            case "Select" when grouped:
            {
                var key = ReadGroupKey(call, entity, entities);
                var projection = key is null ? null : Projection.TryGrouped(call.Model, lambda, call.Method, new EntityRow(entity, "t"), entities, key, diagnostics, where);
                if (projection is null)
                    return false;
                source.AppendLine($"        {attribute}");
                if (projection.IsAnonymous || ContainsAnonymous(call.Element))
                    source.AppendLine($"        internal static SqlQuery<TResult> Select_{index}<TSource, TResult>(this in SqlQuery<TSource> q, Func<TSource, TResult> projection) => q.As<TResult>();");
                else
                    source.AppendLine($"        internal static SqlQuery<{projection.ResultName}> Select_{index}(this in SqlQuery<{elementType}> q, Func<{elementType}, {projection.ResultName}> projection) => q.As<{projection.ResultName}>();");
                source.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, projection.ColumnList));
                return true;
            }

            case "Select":
            {
                var projection = Projection.TryRead(call.Model, lambda, call.Method, translator, diagnostics, where);
                if (projection is null)
                    return false;
                ReportEmulations(translator, where, diagnostics);
                source.AppendLine($"        {attribute}");
                if (projection.IsAnonymous)
                    source.AppendLine($"        internal static SqlQuery<TResult> Select_{index}<TSource, TResult>(this in SqlQuery<TSource> q, Func<TSource, TResult> projection) => q.As<TResult>();");
                else
                    source.AppendLine($"        internal static SqlQuery<{projection.ResultName}> Select_{index}(this in SqlQuery<{elementType}> q, Func<{elementType}, {projection.ResultName}> projection) => q.As<{projection.ResultName}>();");
                source.AppendLine();
                diagnostics.Add(Diagnostic.Create(Diagnostics.Translated, where, name, projection.ColumnList));
                return true;
            }

            default:
                diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedMethod, where, name, "not an operator of phase 1 (Where, OrderBy/ThenBy, Skip, Take, Select, and the async terminals)"));
                return false;
        }
    }
}
