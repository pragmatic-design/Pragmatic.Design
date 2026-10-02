namespace Pragmatic.Messaging.Sql.Entities;

/// <summary>
///     Fan-out registry row: publishes to <see cref="Topic"/> insert one message row per
///     registered <see cref="SubscriptionName"/>. Rows are DURABLE — disposing a consumer stops
///     the local poll loop but keeps the registration, so messages accumulate while the consumer
///     is down (Azure Service Bus subscription semantics).
/// </summary>
public sealed class TransportSubscription
{
    public int Id { get; set; }

    public required string Topic { get; set; }

    public required string SubscriptionName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Refreshed on consumer bind — operational visibility on stale registrations.</summary>
    public DateTimeOffset LastActiveAt { get; set; }
}
