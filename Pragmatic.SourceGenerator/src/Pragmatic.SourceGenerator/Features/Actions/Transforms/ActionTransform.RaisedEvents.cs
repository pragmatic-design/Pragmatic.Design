using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    private const string RaisesAttributeName = "RaisesAttribute";
    private const string RaisesAttributeNamespace = "Pragmatic.Authoring";

    /// <summary>
    ///     Reads <c>[Raises&lt;TEvent&gt;]</c> off the action and resolves each event's constructor by matching
    ///     parameter names to what the invoker holds when it builds the event: the action's inputs (<c>action.{Name}</c>), the value its body
    ///     returned (<c>result.{Name}</c>) and each entity it loaded (<c>action.{field}.{Name}</c>, and <c>{Entity}Id</c> for its key). The
    ///     generated invoker then constructs and dispatches these after a successful commit — behavior lives in the operation, not the
    ///     entity (#4 / anemic-operations model).
    /// </summary>
    /// <remarks>
    ///     Overlaid from the least to the most specific, so a name in two places takes the later: the loaded entity (the record acted on),
    ///     the result (what the body computed), the input (what the caller asked), as a mutation's inputs win over its entity. Before #737
    ///     the inputs were the only source, and a member only the entity or the result held was <c>default</c> with <c>PRAG0433</c>.
    ///     ⚠️ Only a required single load is a source: an optional one may be null, a <c>[LoadEntities]</c> is a list and a
    ///     <c>[RequireExists]</c> reads no row.
    /// </remarks>
    private static ImmutableArray<RaisedEventModel> ParseRaisedEvents(
        INamedTypeSymbol symbol,
        ImmutableArray<ActionPropertyModel> inputProperties,
        ImmutableArray<LoadEntityModel> loadEntities,
        ITypeSymbol? returnType,
        Compilation compilation)
    {
        var members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var load in loadEntities)
        {
            if (load.IsOptional || load.IsMany || load.ExistsOnly
                || compilation.GetTypeByMetadataName(load.EntityTypeFullName.Replace("global::", "")) is not { } entityType)
                continue;
            members[entityType.Name + "Id"] = $"action.{load.FieldName}.Id";
            foreach (var p in PublicInstanceProperties(entityType))
                members[p] = $"action.{load.FieldName}.{p}";
        }

        if (returnType is INamedTypeSymbol { SpecialType: SpecialType.None, TypeKind: TypeKind.Class or TypeKind.Struct } result)
            foreach (var p in PublicInstanceProperties(result))
                members[p] = $"result.{p}";

        foreach (var p in inputProperties)
            members[p.Name] = $"action.{p.Name}";

        var builder = ImmutableArray.CreateBuilder<RaisedEventModel>();
        foreach (var attr in symbol.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null ||
                ac.Name != RaisesAttributeName ||
                !ac.IsGenericType ||
                ac.TypeArguments.Length != 1 ||
                ac.ContainingNamespace?.ToDisplayString() != RaisesAttributeNamespace)
                continue;

            if (ac.TypeArguments[0] is not INamedTypeSymbol eventType)
                continue;

            var resolved = EventConstructorMatcher.ResolveArguments(eventType, members);

            builder.Add(new RaisedEventModel
            {
                EventFullName = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ConstructorArguments = resolved.Arguments,
                UnmatchedParameters = resolved.Unmatched
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>The readable instance properties of <paramref name="type" /> and its bases.</summary>
    private static IEnumerable<string> PublicInstanceProperties(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            foreach (var p in current.GetMembers().OfType<IPropertySymbol>())
                if (!p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public && p.GetMethod is not null)
                    yield return p.Name;
    }
}
