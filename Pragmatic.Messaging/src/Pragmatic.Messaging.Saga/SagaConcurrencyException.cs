namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Thrown by a saga repository when a save loses to another writer: the state it was based on has been
///     moved since it was read.
/// </summary>
/// <remarks>
///     Not a fault of the step — someone moved the saga first. The generated orchestrator reads the saga
///     again and re-runs the step against what is there now, and does not mark the instance
///     <see cref="SagaStatus.Faulted" /> for it. Until then the orchestrator treated it as any
///     other exception: the saga was faulted, and on a transport that does not redeliver — RabbitMQ without a
///     dead-letter exchange — the message was lost.
/// </remarks>
public sealed class SagaConcurrencyException : Exception
{
    /// <summary>Creates the conflict for the saga that lost it.</summary>
    public SagaConcurrencyException(Guid sagaId, string correlationId, Exception? innerException = null)
        : base($"Saga {sagaId} (correlation: {correlationId}) was changed by another writer since it was read.",
            innerException)
    {
        SagaId = sagaId;
        CorrelationId = correlationId;
    }

    /// <summary>The saga instance whose save lost.</summary>
    public Guid SagaId { get; }

    /// <summary>The saga's correlation id.</summary>
    public string CorrelationId { get; }
}
