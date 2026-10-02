using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for a state machine declared on an entity via <c>[StateMachine&lt;TEnum&gt;]</c>.
///     Captures the entity info, enum states, transitions, and events.
/// </summary>
internal sealed record StateMachineModel
{
    /// <summary>Entity type name (e.g. "Reservation").</summary>
    public required string EntityTypeName { get; init; }

    /// <summary>Fully qualified entity type name (e.g. "Showcase.Booking.Entities.Reservation").</summary>
    public required string EntityFullTypeName { get; init; }

    /// <summary>Entity namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Entity accessibility (e.g. "public").</summary>
    public required string Accessibility { get; init; }

    /// <summary>Fully qualified enum type name (e.g. "Showcase.Booking.Entities.ReservationStatus").</summary>
    public required string EnumFullTypeName { get; init; }

    /// <summary>Property name on the entity that holds the state (e.g. "Status").</summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     Whether <see cref="PropertyName" /> is actually declared on the entity.
    /// </summary>
    /// <remarks>
    ///     Carried so PRAG0623 can be reported instead of the generator emitting <c>Status</c> against an
    ///     entity that has no such member — which fails as several <c>CS0103</c>s inside a generated file,
    ///     naming a property the author never wrote.
    /// </remarks>
    public bool PropertyExists { get; init; }

    /// <summary>Whether the entity extends DomainEventSource (supports RaiseEvent).</summary>
    public bool HasDomainEvents { get; init; }

    /// <summary>All states defined in the enum.</summary>
    public EquatableArray<StateMachineStateModel> States { get; init; } = EquatableArray<StateMachineStateModel>.Empty;

    /// <summary>The initial state value name (marked with [InitialState]).</summary>
    public string? InitialStateName { get; init; }

    /// <summary>
    ///     States that have user-defined guard methods (CanEnter{State}() returning bool).
    ///     When present, the generated TransitionTo() calls the guard before applying the transition.
    /// </summary>
    public ImmutableHashSet<string> GuardedStates { get; init; } = ImmutableHashSet<string>.Empty;

    public bool IsValid { get; init; }
}
