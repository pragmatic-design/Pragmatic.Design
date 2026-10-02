namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Type-erased snapshot of a saga instance for ops surfaces (dashboard, diagnostics)
///     that enumerate sagas across types without knowing <c>TState</c>.
/// </summary>
public sealed record SagaInstanceInfo(
    Guid Id,
    string CorrelationId,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);
