namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Configuration options for the messaging infrastructure.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>
    ///     The name this application subscribes under — its consumer group, in the words every broker's
    ///     documentation uses. Null (the default) means the module that registered each subscription
    ///     names it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Set it when the module's own name is not the identity you want at the broker: two
    ///         deployments of one module that must not share a queue, or two processes that must.
    ///     </para>
    ///     <para>
    ///         ⚠️ A subscription name is <b>operational state</b>. Changing it on a running deployment
    ///         leaves the previous queue bound to its topic and holding whatever it had — the messages
    ///         are not lost, but nothing is reading them. Plan the rename, do not discover it.
    ///     </para>
    /// </remarks>
    public string? SubscriberName { get; set; }

    /// <summary>
    ///     Whether the outbox pattern is enabled. Default is false.
    ///     When enabled, messages are persisted to outbox before dispatch.
    /// </summary>
    public bool OutboxEnabled { get; set; }

    /// <summary>
    ///     Outbox polling interval in seconds. Default is 5.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    ///     Maximum number of outbox messages to process per batch. Default is 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    ///     Maximum number of retry attempts for outbox delivery. Default is 5.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    ///     How long a delivered (<c>ProcessedAt != null</c>) outbox row is retained before the
    ///     purge service deletes it. Keeps the <c>__OutboxMessages</c> table from growing without
    ///     bound while leaving a short forensic/replay window. Default: 3 days.
    /// </summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    ///     How often the outbox purge service sweeps delivered rows older than
    ///     <see cref="OutboxRetention"/>. Default: 1 hour.
    /// </summary>
    public TimeSpan OutboxPurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     Whether to register <see cref="InMemoryDeadLetterStore"/> by default.
    ///     Default is true. Set to false when providing a custom <see cref="IDeadLetterStore"/>.
    /// </summary>
    public bool UseInMemoryDeadLetter { get; set; } = true;

    /// <summary>
    ///     Named buses configured via AddBus().
    /// </summary>
    public List<string> NamedBuses { get; set; } = [];

    /// <summary>
    ///     Timeout for distributed request/reply (<c>IMessageBus.RequestAsync</c> over a transport)
    ///     before it fails with <c>RequestReplyException</c>. Default: 30 seconds.
    /// </summary>
    public TimeSpan RequestReplyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a consumer's claim on a message id holds before another consumer may take it over.
    ///     Default: 5 minutes.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Not a timeout on the handler</b> — nothing cancels it when this expires. It is how long
    ///     a message waits after the process handling it is <b>killed</b>: until then, a redelivery sees
    ///     a claim that is still held and steps aside. Set it above the slowest handler:
    ///     shorter and two workers can handle one message, longer and a crash delays it by that much.
    /// </remarks>
    public TimeSpan HandlerLease { get; set; } = TimeSpan.FromMinutes(5);
}
