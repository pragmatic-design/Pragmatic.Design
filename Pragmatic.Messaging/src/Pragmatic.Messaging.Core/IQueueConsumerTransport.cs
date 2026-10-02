namespace Pragmatic.Messaging;

/// <summary>
///     Optional transport capability: consume a POINT-TO-POINT queue (the counterpart of
///     <see cref="IMessageTransport.SendAsync"/>). On transports where send and subscribe share
///     one address space (Channels, RabbitMQ, Kafka, SQL) this is redundant —
///     <see cref="IMessageTransport.SubscribeAsync"/> already reaches sent messages, and the
///     binders fall back to it. Azure Service Bus separates queues from topics, so its
///     request/reply consumption MUST go through this capability.
/// </summary>
public interface IQueueConsumerTransport
{
    /// <summary>Consumes a queue; the handler runs once per delivered message.</summary>
    Task<IAsyncDisposable> SubscribeQueueAsync(
        string queue,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default);
}
