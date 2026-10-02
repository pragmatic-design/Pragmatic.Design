using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Lifecycle.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the lifecycle-events generator ([Raises&lt;T&gt;], PRAG2750-2799).
/// </summary>
internal static class LifecycleEventsDiagnostics
{
    // PRAG2750: [Raises<T>] on an entity that cannot raise events.
    public static readonly DiagnosticDescriptor MustBeDomainEventSource = DiagnosticFactory.Error(
        "PRAG2750",
        "Entity with [Raises<T>] must derive from DomainEventSource",
        "Type '{0}' declares [Raises<T>(on: ...)] but does not derive from DomainEventSource, so the generated lifecycle events cannot be raised",
        "Make the entity derive from DomainEventSource (it gains RaiseEvent and event tracking).");

    // PRAG2751: An event constructor parameter could not be matched to an entity member.
    public static readonly DiagnosticDescriptor UnmatchedConstructorParameter = DiagnosticFactory.Warning(
        "PRAG2751",
        "Lifecycle event constructor parameter is unmatched",
        "Event constructor parameter '{0}' on entity '{1}' matched no entity member by name; it is passed 'default'. Rename the parameter to match an entity member, or raise this event from a domain method instead.",
        "Align the event constructor parameter name with an entity property, or use a domain method + RaiseEvent for events carrying non-entity data.");

    // PRAG2752: [EnableEventOutbox] on a boundary whose assembly does not reference Pragmatic.Events.EFCore.
    public static readonly DiagnosticDescriptor EnableEventOutboxWithoutEFCore = DiagnosticFactory.Warning(
        "PRAG2752",
        "[EnableEventOutbox] requires Pragmatic.Events.EFCore",
        "Boundary '{0}' is marked [EnableEventOutbox] but this project does not reference Pragmatic.Events.EFCore, so the outbox is not wired (the attribute is a no-op). Reference Pragmatic.Events.EFCore.",
        "Add a reference to Pragmatic.Events.EFCore to the boundary project so the generated DbContext can map __EventOutbox and wire the delivery service.");

    // PRAG2753: [Raises<T>] on an ENTITY's method. It generates nothing — and read as wired, because
    // the class-level form on the same entity is. A generator cannot add a statement to a body the
    // author wrote, so the declaration is refused rather than accepted and dropped.
    // Scope is deliberately the entity: on a type that is not one, the member-level declaration is
    // what the host's event graph reads to attribute a raise to its origin (PRAG0816).
    public static readonly DiagnosticDescriptor RaisesOnAnEntityMethodGeneratesNothing = DiagnosticFactory.Error(
        "PRAG2753",
        "[Raises<T>] on an entity's method generates nothing",
        "[Raises<{1}>] on '{0}' generates nothing: only the entity's class-level declaration is wired, and a source generator cannot add a raise to a method body you wrote. Declare it with [RaisesEvent<{1}>] on the state machine's target member, or call RaiseEvent(new {1}(...)) in the method.",
        "Move the declaration to the entity class (a lifecycle transition), to the state machine's target member via [RaisesEvent<T>], or raise the event by hand in the method body.");
}
