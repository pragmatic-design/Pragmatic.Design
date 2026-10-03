namespace Pragmatic.Messaging.RabbitMQ;

/// <summary>
///     Configuration for the RabbitMQ transport.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>AMQP connection string (e.g., "amqp://guest:guest@localhost:5672").</summary>
    public required string ConnectionString { get; set; }

    /// <summary>Consumer prefetch count (QoS). Default: 10.</summary>
    public ushort ConsumerPrefetchCount { get; set; } = 10;

    /// <summary>Auto-reconnect on connection loss. Default: true.</summary>
    public bool AutoReconnect { get; set; } = true;

    /// <summary>
    ///     Delay in ms before the second connect attempt when the first fails at start-up, doubling after each
    ///     failure up to 30 seconds; also the client's recovery interval once connected. Default: 1000.
    /// </summary>
    public int ReconnectBaseDelayMs { get; set; } = 1000;

    /// <summary>
    ///     Connect attempts at start-up before giving up, the first included (0 = until it connects). Giving
    ///     up leaves the host running with the transport not connected, logged as an error. Default: 0.
    /// </summary>
    public int MaxReconnectAttempts { get; set; }

    /// <summary>
    ///     How long a publish, send or subscribe waits for a connect still in progress before it fails.
    ///     The consumer service connects in the background, so the application starts with its broker down;
    ///     an operation issued meanwhile waits for the connect rather than failing with "not connected".
    ///     Default: 30 seconds.
    /// </summary>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Declare queues as durable. Default: true.</summary>
    public bool DurableQueues { get; set; } = true;

    /// <summary>Mark published messages as persistent. Default: true.</summary>
    public bool PersistentMessages { get; set; } = true;

    /// <summary>Exchange type for boundary exchanges. Default: "topic".</summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>
    ///     Name of the dead-letter exchange. Consumer queues are declared with
    ///     <c>x-dead-letter-exchange</c>, so a message Nacked without requeue (a handler that threw)
    ///     is routed here instead of being discarded; a durable dead-letter queue
    ///     (<c>"{DeadLetterExchange}.dlq"</c>) is declared and bound to it.
    ///     Default: <c>"pragmatic.dlx"</c> — safe by default. Set to null/empty to opt out
    ///     (failed messages are then DROPPED permanently).
    /// </summary>
    /// <remarks>
    ///     RabbitMQ queue arguments are immutable: changing the DLX of a queue that already exists
    ///     requires deleting and re-declaring that queue.
    /// </remarks>
    public string? DeadLetterExchange { get; set; } = "pragmatic.dlx";

    /// <summary>
    ///     Enables publisher confirms on the publish channel: <c>BasicPublishAsync</c> completes only
    ///     after the broker confirms the message, and throws if the broker nacks it. Closes the
    ///     silent-loss window between the outbox and the broker. Default: true.
    /// </summary>
    public bool PublisherConfirms { get; set; } = true;

    /// <summary>
    ///     Queue type for declared consumer queues: <c>"classic"</c> (default) or <c>"quorum"</c>
    ///     (replicated, the production standard on RabbitMQ clusters; sets <c>x-queue-type</c>).
    /// </summary>
    /// <remarks>
    ///     Queue arguments are immutable: switching type on an existing queue requires deleting
    ///     and re-declaring it. Quorum queues require durable, non-exclusive queues (the default).
    /// </remarks>
    public string QueueType { get; set; } = "classic";
}
