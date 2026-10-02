namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Configuration for message idempotency (deduplication).
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    ///     How long processed-message records are kept before being purged.
    ///     Must exceed the longest realistic redelivery window (broker redelivery,
    ///     outbox retries). Default: 7 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the purge runs. Default: 6 hours.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(6);
}
