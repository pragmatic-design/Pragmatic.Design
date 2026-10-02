namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Configuration options for the outbox delivery service.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    ///     Polling interval in seconds. Default is 5.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    ///     Maximum number of messages to process per batch. Default is 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    ///     Maximum number of retry attempts before dead-lettering. Default is 5.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    ///     How long a delivered outbox row is retained before the purge service deletes it.
    ///     Default: 3 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    ///     How often the purge service sweeps delivered rows older than <see cref="Retention"/>.
    ///     Default: 1 hour.
    /// </summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
}
