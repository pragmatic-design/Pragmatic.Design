namespace Pragmatic.Messaging;

/// <summary>
///     Abstraction over message transport infrastructure.
///     Implementations: ChannelTransport (in-process), RabbitMqTransport (distributed), KafkaTransport (streaming).
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    /// <summary>Transport name for diagnostics and multi-bus resolution.</summary>
    string Name { get; }

    /// <summary>Current connection status.</summary>
    TransportStatus Status { get; }

    /// <summary>
    ///     Fan-out publish: all subscribers on the topic receive the message.
    /// </summary>
    Task PublishAsync(ReadOnlyMemory<byte> payload, string topic, MessageContext context, CancellationToken ct = default);

    /// <summary>
    ///     Point-to-point send: exactly one consumer on the queue receives the message.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> payload, string queue, MessageContext context, CancellationToken ct = default);

    /// <summary>
    ///     Subscribe to a topic. Returns a disposable subscription handle.
    ///     The handler receives raw bytes — deserialization happens in the dispatch layer.
    /// </summary>
    Task<IAsyncDisposable> SubscribeAsync(
        string topic,
        string subscriptionName,
        Func<ReadOnlyMemory<byte>, MessageContext, CancellationToken, Task> handler,
        CancellationToken ct = default);

    /// <summary>Establish connection to the transport infrastructure.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Gracefully disconnect, draining in-flight messages.</summary>
    Task DisconnectAsync(CancellationToken ct = default);
}
