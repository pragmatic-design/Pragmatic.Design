namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>
///     One queued message (row = delivery). Publish fan-out inserts one row per subscription;
///     send inserts one row for the target queue. Ack deletes the row; failures push
///     <see cref="VisibleAt"/> forward (backoff) until <c>MaxDeliveryCount</c> moves the row to
///     <see cref="TransportDeadLetter"/>.
/// </summary>
public sealed class TransportMessage
{
    /// <summary>Identity PK — enqueue order and index locality (better than a GUID).</summary>
    public long Id { get; set; }

    /// <summary>Logical message id (from <see cref="MessageContext.MessageId"/>).</summary>
    public required string MessageId { get; set; }

    /// <summary>
    ///     Destination: the subscription name for published messages (one row per subscriber),
    ///     the queue name for sends — same convention as RabbitMQ, where the subscription name
    ///     IS the queue name (request/reply works with no special cases).
    /// </summary>
    public required string QueueName { get; set; }

    /// <summary>Origin topic (diagnostics only; null for point-to-point sends).</summary>
    public string? Topic { get; set; }

    /// <summary>Serialized message payload.</summary>
    public required byte[] Payload { get; set; }

    public string? CorrelationId { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? SourceBoundary { get; set; }
    public string? BusName { get; set; }

    /// <summary>Custom headers as JSON (source-generated context — zero reflection).</summary>
    public string? HeadersJson { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>Not claimable before this instant (delayed/scheduled messages, retry backoff).</summary>
    public DateTimeOffset VisibleAt { get; set; }

    /// <summary>Claim token ("machine:guid") of the consumer currently processing the row.</summary>
    public string? LockedBy { get; set; }

    /// <summary>Lease expiry — an expired lock makes the row reclaimable (crash recovery).</summary>
    public DateTimeOffset? LockExpiresAt { get; set; }

    /// <summary>Incremented AT CLAIM (a crash mid-handler consumes an attempt — no poison loop).</summary>
    public int DeliveryCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Groups the rows of one scheduled publish — restart-safe cancellation handle.</summary>
    public Guid? SchedulingTokenId { get; set; }
}
