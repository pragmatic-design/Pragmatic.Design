using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Template for generating EventHandlerRegistrationExtensions class.
/// </summary>
internal sealed class EventHandlerRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<EventHandlerModel> _handlers;
    private readonly string _namespacePrefix;

    public EventHandlerRegistrationTemplate(
        ImmutableArray<EventHandlerModel> handlers,
        string namespacePrefix)
    {
        _handlers = handlers;
        _namespacePrefix = namespacePrefix;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Composition", "EventHandlerRegistration"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return !_handlers.IsDefaultOrEmpty;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Events");

        AppendNamespace(_namespacePrefix);
        AppendLine();

        RenderExtensionClass();
    }

    private void RenderExtensionClass()
    {
        XmlSummary("Extension methods for registering event handlers discovered by the source generator.");

        Class("EventHandlerRegistrationExtensions", RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true, IsStatic = true });
    }

    private void RenderMethods()
    {
        XmlSummary("Registers all event handlers marked with [EventHandler] attribute.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("IServiceCollection", "services") { IsExtension = true }
        };

        Method("AddPragmaticEventHandlers", RenderMethodBody, "IServiceCollection", parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderMethodBody()
    {
        // The dispatcher the handlers below need. Registering them without it puts every [EventHandler]
        // into DI and leaves nothing to call them: the entity raises, the invoker asks for an
        // IDomainEventDispatcher, gets null, and the event goes nowhere — with no error anywhere.
        // TryAdd, so an application that wants an outbox instead registers its own and wins.
        AppendLine(
            "global::Pragmatic.Events.Extensions.DomainEventsServiceCollectionExtensions"
            + ".AddInMemoryDomainEvents(services);");
        AppendLine();

        foreach (var handler in _handlers.OrderBy(h => h.FullTypeName))
        {
            foreach (var @event in handler.EventTypeFullNames)
                AppendLine(
                    $"services.AddScoped<global::Pragmatic.Events.IDomainEventHandler<{@event}>, "
                    + $"{handler.FullTypeName}>();");
        }

        // Contribute this assembly's AOT-safe typed dispatch table. Registered additively
        // (one per module): InMemoryEventDispatcher probes every registered table, so events
        // handled in any composed module are routed without the dynamic fallback.
        if (HasDispatchTable)
        {
            AppendLine();
            AppendLine(
                $"services.AddSingleton<global::Pragmatic.Events.ITypedEventDispatchTable, global::{_namespacePrefix}.Generated.GeneratedEventDispatchTable>();");
        }

        AppendLine();
        Return("services");
    }

    /// <summary>
    ///     Mirrors <c>EventDispatchTableEmitter</c>'s guard: the generated table type only
    ///     exists when there is a namespace and at least one valid handled event type.
    /// </summary>
    private bool HasDispatchTable =>
        !string.IsNullOrEmpty(_namespacePrefix)
        && _handlers.Any(h => h.IsValid && h.EventTypeFullNames.Count > 0);
}
