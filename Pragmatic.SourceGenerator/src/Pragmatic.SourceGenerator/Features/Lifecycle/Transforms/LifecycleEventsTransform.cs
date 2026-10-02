using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Lifecycle.Models;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Transforms;

/// <summary>
///     Reads <c>[Raises&lt;TEvent&gt;(on: ...)]</c> off an entity, resolves each event's primary
///     constructor, and matches its parameters to entity members by name (with <c>Id</c>/<c>PersistenceId</c>
///     assumed SG-generated and <c>OccurredAt</c> filled from the clock). Produces a cache-friendly model.
/// </summary>
internal static class LifecycleEventsTransform
{
    public static LifecycleEventsModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol entity)
            return null;

        // [Raises<T>] on a Mutation/DomainAction is an operation-level raise (handled by the action/mutation
        // invoker), not an entity lifecycle declaration — skip it so the two features don't both react.
        if (DerivesFromOperationBase(entity))
            return null;

        // Declared instance properties (own + inherited): case-insensitive lookup → the property's ACTUAL name.
        // V4.2: we must emit the entity property (PascalCase), not the event parameter name (often camelCase).
        var memberNames = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var t = entity; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
            foreach (var p in t.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic))
                memberNames[p.Name] = p.Name;

        var raises = ImmutableArray.CreateBuilder<LifecycleRaise>();
        var unmatched = ImmutableArray.CreateBuilder<string>();

        // The members other generators will add — trait members and the foreign keys of [Relation.*]
        // — are not on the symbol during this pass. Matched by source members alone, an event
        // parameter named after a generated key was reported unmatched and passed `default`.
        foreach (var generated in Core.TraitPropertyResolver.GetGeneratedProperties(entity))
            if (!memberNames.ContainsKey(generated.Name))
                memberNames[generated.Name] = generated.Name;

        foreach (var attr in context.Attributes)
        {
            if (attr.AttributeClass is not { TypeArguments.Length: 1 } ac ||
                ac.TypeArguments[0] is not INamedTypeSymbol eventType)
                continue;

            var lifecycle = ReadLifecycle(attr);
            var eventFqn = eventType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Primary constructor = most parameters, excluding the record copy-constructor.
            var ctor = eventType.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, eventType)))
                .OrderByDescending(c => c.Parameters.Length)
                .FirstOrDefault();

            var args = ImmutableArray.CreateBuilder<string>();
            foreach (var p in ctor?.Parameters ?? ImmutableArray<IParameterSymbol>.Empty)
            {
                if (p.Name.Equals("OccurredAt", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("global::System.DateTimeOffset.UtcNow");
                }
                else if (p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Equals("PersistenceId", StringComparison.OrdinalIgnoreCase)
                         || p.Name.Equals(entity.Name + "Id", StringComparison.OrdinalIgnoreCase))
                {
                    // The PK: SG-generated Id, or the common {Entity}Id event-parameter convention.
                    args.Add("Id");
                }
                else if (memberNames.TryGetValue(p.Name, out var actualName))
                {
                    args.Add(actualName);
                }
                else
                {
                    unmatched.Add($"{eventType.Name}.{p.Name}");
                    args.Add($"default /* unmatched: {p.Name} */");
                }
            }

            raises.Add(new LifecycleRaise(lifecycle, eventFqn, args.ToImmutable()));
        }

        if (raises.Count == 0)
            return null;

        return new LifecycleEventsModel
        {
            TypeName = entity.Name,
            Namespace = entity.ContainingNamespace.IsGlobalNamespace ? "" : entity.ContainingNamespace.ToDisplayString(),
            Accessibility = entity.DeclaredAccessibility.ToString().ToLowerInvariant(),
            TypeKind = "class",
            Raises = raises.ToImmutable(),
            IsDomainEventSource = IsDomainEventSource(entity),
            UnmatchedParameters = unmatched.ToImmutable(),
            LocationInfo = LocationInfo.From(entity.Locations.FirstOrDefault())
        };
    }

    private static string ReadLifecycle(AttributeData attr)
    {
        var v = attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int i ? i : 0;
        return v switch { 1 => "Updated", 2 => "Deleted", _ => "Created" };
    }

    /// <summary>True when the type derives from a Mutation/DomainAction base — an operation, not an entity.</summary>
    private static bool DerivesFromOperationBase(INamedTypeSymbol type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (t.Name is "Mutation" or "DomainAction" or "VoidDomainAction")
                return true;
        return false;
    }

    /// <summary>
    ///     True when the entity derives from <c>Pragmatic.Events.DomainEventSource</c> — the base that
    ///     actually supplies the <c>RaiseEvent</c> the template emits. Matched on the full name: a simple
    ///     name match would accept an unrelated application class called <c>DomainEventSource</c>,
    ///     suppress PRAG2750, and turn the promised error into a compile error in the generated file.
    /// </summary>
    private static bool IsDomainEventSource(INamedTypeSymbol entity)
    {
        for (var t = entity.BaseType; t is not null; t = t.BaseType)
            if (t.Name == "DomainEventSource" && t.ContainingNamespace?.ToDisplayString() == "Pragmatic.Events")
                return true;
        return false;
    }
}
