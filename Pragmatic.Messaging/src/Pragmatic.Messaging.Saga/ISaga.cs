namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Marker interface for saga orchestrations.
///     A saga coordinates a sequence of DomainActions triggered by events,
///     with state tracked as an enum and compensation on failure.
/// </summary>
/// <typeparam name="TState">The enum representing saga states.</typeparam>
public interface ISaga<TState> where TState : struct, Enum
{
    /// <summary>Unique saga instance identifier.</summary>
    Guid Id { get; set; }

    /// <summary>Current state of the saga.</summary>
    TState State { get; set; }

    /// <summary>Correlation ID linking related messages.</summary>
    string CorrelationId { get; set; }

    /// <summary>When the saga started.</summary>
    DateTimeOffset StartedAt { get; set; }

    /// <summary>When the saga completed (null if still active).</summary>
    DateTimeOffset? CompletedAt { get; set; }
}
