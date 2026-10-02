using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a single state (enum value) in a state machine,
///     with its incoming transitions and events raised upon entry.
/// </summary>
internal sealed record StateMachineStateModel
{
    /// <summary>Enum value name (e.g. "Confirmed").</summary>
    public required string Name { get; init; }

    /// <summary>Whether this is the initial state ([InitialState]).</summary>
    public bool IsInitial { get; init; }

    /// <summary>
    ///     Names of states from which a transition TO this state is allowed.
    ///     Derived from <c>[TransitionFrom(...)]</c> attributes.
    /// </summary>
    public EquatableArray<string> TransitionFromStates { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Fully qualified event type names raised when transitioning to this state.
    ///     Derived from <c>[RaisesEvent&lt;T&gt;]</c> attributes.
    /// </summary>
    public EquatableArray<string> EventTypeNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Events raised on entry, with their constructor arguments resolved from the entity's members by name
    ///     so an event with required parameters is built correctly (not via a parameterless ctor).
    /// </summary>
    public EquatableArray<StateRaisedEvent> RaisedEvents { get; init; } = EquatableArray<StateRaisedEvent>.Empty;
}

/// <summary>An event raised on a transition, with its constructor arguments resolved from entity members.</summary>
internal sealed record StateRaisedEvent
{
    public required string TypeName { get; init; }
    public required EquatableArray<string> CtorArguments { get; init; }
}
