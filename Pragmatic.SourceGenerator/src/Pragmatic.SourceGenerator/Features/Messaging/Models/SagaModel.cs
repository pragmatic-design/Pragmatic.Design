using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Model for a class decorated with [Saga&lt;TState&gt;].
///     Captures the complete state transition graph deduced from handler methods.
/// </summary>
internal sealed record SagaModel : GeneratorModel
{
    /// <summary>
    ///     The assembly the saga is declared in — the module that listens, and so the name its
    ///     transport subscriptions are known by at the broker.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Read off the assembly rather than out of the namespace: a saga in
    ///     <c>App.Sagas.Something</c> would otherwise be a consumer group called <c>"sagas"</c>, which
    ///     identifies nothing and collides with every other module that keeps its sagas in a folder by
    ///     that name.
    /// </remarks>
    public string AssemblyName { get; init; } = "";

    /// <summary>FQN of the state enum type.</summary>
    public required string StateTypeFqn { get; init; }

    /// <summary>Short name of the state enum.</summary>
    public required string StateTypeShortName { get; init; }

    /// <summary>Whether the saga class is declared partial.</summary>
    public bool IsPartial { get; init; }

    /// <summary>
    ///     Whether the saga declares <c>: ISaga&lt;TState&gt;</c>. EF Core-backed persistence
    ///     (<c>EfCoreSagaRepository&lt;TSaga, TState&gt;</c>) constrains <c>TSaga : ISaga&lt;TState&gt;, new()</c>,
    ///     so the EF registration branch is only emitted for sagas that satisfy it.
    /// </summary>
    public bool ImplementsISaga { get; init; }

    /// <summary>
    ///     Fully-qualified (global::) name of the DbContext that persists this saga, when the saga's
    ///     enclosing <c>[Boundary]</c> is marked <c>[EnableSagaPersistence]</c> — else null. Drives the
    ///     compile-time EF Core repository registration (the DbContext is resolved lazily at runtime,
    ///     so registration order does not matter).
    /// </summary>
    public string? PersistenceDbContextFqn { get; init; }

    /// <summary>All handler methods (steps) in the saga.</summary>
    public required EquatableArray<SagaStepModel> Steps { get; init; }

    /// <summary>The start step (method with [SagaStart]).</summary>
    public SagaStepModel? StartStep { get; init; }

    /// <summary>All enum values of TState.</summary>
    public required EquatableArray<string> StateValues { get; init; }

    /// <summary>Diagnostic location, value-equatable for incremental caching.</summary>
    public Pragmatic.SourceGenerator.Core.LocationInfo? LocationInfo { get; init; }

    /// <summary>The diagnostic location reconstructed from <see cref="LocationInfo"/>.</summary>
    public Microsoft.CodeAnalysis.Location? Location => LocationInfo?.ToLocation();
}

/// <summary>
///     Model for an individual saga handler method.
/// </summary>
internal sealed record SagaStepModel
{
    /// <summary>Method name.</summary>
    public required string MethodName { get; init; }

    /// <summary>FQN of the event type this method handles.</summary>
    public required string EventTypeFqn { get; init; }

    /// <summary>Short name of the event type.</summary>
    public required string EventTypeShortName { get; init; }

    /// <summary>True if this is the [SagaStart] method.</summary>
    public bool IsStart { get; init; }

    /// <summary>State(s) this method is valid in (from [InState]).</summary>
    public EquatableArray<string> ValidStates { get; init; } = EquatableArray<string>.Empty;

    /// <summary>State to transition to after success (from [InState].NextState).</summary>
    public string? NextState { get; init; }

    /// <summary>FQN of compensation action (from [CompensateWith&lt;T&gt;]).</summary>
    public string? CompensationActionFqn { get; init; }

    /// <summary>Timeout duration string (from [SagaTimeout]).</summary>
    public string? TimeoutDuration { get; init; }

    /// <summary>FQN of the published result type (DomainAction to dispatch). For async steps this is the unwrapped Task&lt;T&gt;/ValueTask&lt;T&gt; argument; null when nothing is published.</summary>
    public string? ReturnTypeFqn { get; init; }

    /// <summary>True when the step method returns Task/Task&lt;T&gt;/ValueTask/ValueTask&lt;T&gt; — the orchestrator must await it.</summary>
    public bool IsAsync { get; init; }

    /// <summary>
    ///     C# expression (relative to <c>message.</c>) that yields the saga correlation id:
    ///     <c>"CorrelationId"</c> for ICorrelatedMessage events, or the [CorrelationKey]
    ///     property access (ToString()/coalesced as needed). Null = neither present (PRAG0820).
    /// </summary>
    public string? CorrelationAccessor { get; init; }

    /// <summary>True when the event declares more than one [CorrelationKey] (PRAG0821).</summary>
    public bool HasMultipleCorrelationKeys { get; init; }
}
