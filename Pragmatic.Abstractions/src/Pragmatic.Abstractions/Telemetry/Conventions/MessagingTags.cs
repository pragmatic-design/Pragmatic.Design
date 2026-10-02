namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for Pragmatic.Messaging operations.
/// </summary>
/// <remarks>
///     The <c>messaging.*</c> namespace belongs to the OpenTelemetry semantic conventions, and none of
///     these are semconv attributes — hence the <c>pragmatic.</c> prefix, which keeps them from
///     colliding with a standard name that may later mean something else.
/// </remarks>
public static class MessagingTags
{
    /// <summary>The message type name.</summary>
    public const string MessageType = "pragmatic.messaging.message_type";

    /// <summary>The message instance ID, unique per published message.</summary>
    public const string MessageId = "pragmatic.messaging.message_id";

    /// <summary>The transport used (e.g., "channel", "rabbitmq", "in-memory").</summary>
    public const string Transport = "pragmatic.messaging.transport";

    /// <summary>The topic the message was published to.</summary>
    public const string Topic = "pragmatic.messaging.topic";

    /// <summary>Number of handlers invoked for the message.</summary>
    public const string HandlerCount = "pragmatic.messaging.handler_count";

    /// <summary>Number of handlers that threw while processing the message.</summary>
    public const string HandlerFailures = "pragmatic.messaging.handler_failures";

    /// <summary>Size of the outbox batch being delivered.</summary>
    public const string OutboxBatchSize = "pragmatic.messaging.outbox.batch_size";

    /// <summary>The boundary whose outbox is being delivered.</summary>
    public const string OutboxBoundary = "pragmatic.messaging.outbox.boundary";
}
