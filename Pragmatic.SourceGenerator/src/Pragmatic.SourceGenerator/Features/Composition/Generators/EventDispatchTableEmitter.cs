using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Emits the per-assembly AOT-safe typed event dispatch table
///     (<c>GeneratedEventDispatchTable</c>) from the set of handled event types.
///     Shared by library mode and host mode so both composition paths wire the table.
/// </summary>
internal static class EventDispatchTableEmitter
{
    public static void Emit(
        SourceProductionContext context,
        ImmutableArray<EventHandlerModel> eventHandlers,
        string namespacePrefix)
    {
        // The switch needs a namespace to live in and at least one distinct event type.
        if (string.IsNullOrEmpty(namespacePrefix) || eventHandlers.IsDefaultOrEmpty)
            return;

        // Distinct is mandatory: two handlers for the same event would otherwise produce
        // duplicate case labels (a compile error). Invalid handlers are skipped.
        var eventTypeFqns = eventHandlers
            .Where(h => h.IsValid)
            .SelectMany(h => h.EventTypeFullNames)
            .Distinct()
            .OrderBy(fqn => fqn, System.StringComparer.Ordinal)
            .ToImmutableArray();

        if (eventTypeFqns.IsDefaultOrEmpty)
            return;

        var template = new EventDispatchTableTemplate(eventTypeFqns, namespacePrefix);
        var artifact = template.RenderOutput();
        context.AddSource(artifact);
    }
}
