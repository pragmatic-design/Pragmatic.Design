namespace Pragmatic.Events.EFCore.Outbox;

/// <summary>
///     Options for the transactional event outbox delivery loop.
/// </summary>
public sealed class EventOutboxOptions
{
    private int _batchSize = 100;
    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private int _maxAttempts = 5;
    private TimeSpan _claimLeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Maximum number of pending entries delivered per polling cycle.
    ///     Must be at least 1. Default: 100.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When set to a value less than 1.</exception>
    public int BatchSize
    {
        get => _batchSize;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(BatchSize)} must be at least 1.");
            _batchSize = value;
        }
    }

    /// <summary>
    ///     Delay between polling cycles. Must be positive. Default: 5 seconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When set to zero or a negative value.</exception>
    public TimeSpan PollingInterval
    {
        get => _pollingInterval;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(PollingInterval)} must be positive.");
            _pollingInterval = value;
        }
    }

    /// <summary>
    ///     Maximum delivery attempts before an entry is left as a poison message
    ///     (no longer retried). Must be at least 1. Default: 5.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When set to a value less than 1.</exception>
    public int MaxAttempts
    {
        get => _maxAttempts;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(MaxAttempts)} must be at least 1.");
            _maxAttempts = value;
        }
    }

    /// <summary>
    ///     How long a claimed entry is held before another worker may re-grab it (crash recovery).
    ///     Should comfortably exceed the time to deliver a batch. Must be positive. Default: 5 minutes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">When set to zero or a negative value.</exception>
    public TimeSpan ClaimLeaseDuration
    {
        get => _claimLeaseDuration;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(ClaimLeaseDuration)} must be positive.");
            _claimLeaseDuration = value;
        }
    }
}
