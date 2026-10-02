namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>
///     A message that exhausted <c>MaxDeliveryCount</c>, MOVED here from the queue table in the
///     same transaction (never copy+flag). Kept for inspection/replay.
/// </summary>
public sealed class TransportDeadLetter
{
    public long Id { get; set; }

    public required string MessageId { get; set; }
    public required string QueueName { get; set; }
    public string? Topic { get; set; }
    public required byte[] Payload { get; set; }

    public string? CorrelationId { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? SourceBoundary { get; set; }
    public string? BusName { get; set; }
    public string? HeadersJson { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }
    public int DeliveryCount { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset DeadLetteredAt { get; set; }

    /// <summary>Why the row moved here (e.g. "MaxDeliveryCountExceeded").</summary>
    public required string Reason { get; set; }
}
