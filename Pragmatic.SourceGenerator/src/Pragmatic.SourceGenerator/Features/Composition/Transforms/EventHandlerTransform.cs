using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms [EventHandler] attribute declarations into EventHandlerModel.
/// </summary>
internal static class EventHandlerTransform
{
    private const string DomainEventHandlerInterfaceName = "Pragmatic.Events.IDomainEventHandler";

    /// <summary>
    ///     Transforms a class with [EventHandler] attribute into an EventHandlerModel.
    /// </summary>
    public static EventHandlerModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Every IDomainEventHandler<TEvent> the class implements. A [EventHandler] on a class that
        // implements NONE returns an INVALID model (not null) so CompositionFeature reports PRAG1670
        // instead of silently dropping it — an unregistered handler means the domain event it was
        // meant to handle is silently never delivered.
        //
        // ⚠️ All of them, not the first: a class handling three events is ordinary C# and the DRY shape
        // for three moves that differ by a name. Reading one left the other two undelivered, with
        // nothing to see anywhere.
        var events = HandledEvents(symbol);
        if (events.Length == 0)
            return new EventHandlerModel
            {
                Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : symbol.ContainingNamespace.ToDisplayString(),
                TypeName = symbol.Name,
                FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EventTypeFullNames = EquatableArray<string>.Empty,
                LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
                InvalidReason = InvalidReason.NotEventHandler
            };

        return new EventHandlerModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EventTypeFullNames = events,
            LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     Every event the class handles, in declaration order, deduplicated.
    /// </summary>
    /// <remarks>
    ///     Ordered and deduplicated so the generated registration is stable: the incremental pipeline
    ///     caches on this model, and a set that reorders between runs would re-emit the file on every
    ///     keystroke.
    /// </remarks>
    private static ImmutableArray<string> HandledEvents(INamedTypeSymbol symbol)
    {
        var events = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType || iface.TypeArguments.Length != 1)
                continue;
            if (iface.OriginalDefinition.ToDisplayString() != $"{DomainEventHandlerInterfaceName}<TEvent>")
                continue;

            var name = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(name))
                events.Add(name);
        }

        return events.ToImmutable();
    }
}
