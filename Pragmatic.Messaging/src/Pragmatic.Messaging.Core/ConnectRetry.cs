namespace Pragmatic.Messaging;

/// <summary>
///     How a consumer service retries a transport whose connect failed: after <see cref="BaseDelay" />,
///     doubling up to <see cref="MaxDelay" />, at most <see cref="MaxAttempts" /> times (0 = until it connects).
/// </summary>
/// <param name="BaseDelay">The wait after the first failed attempt.</param>
/// <param name="MaxAttempts">Attempts before giving up, the first included; 0 for no limit.</param>
public sealed record ConnectRetry(TimeSpan BaseDelay, int MaxAttempts)
{
    /// <summary>The longest wait between two attempts, however many have failed.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>One second, doubling, without a limit on attempts.</summary>
    public static ConnectRetry Default { get; } = new(TimeSpan.FromSeconds(1), 0);

    /// <summary>The wait after failed attempt number <paramref name="attempt" /> (1-based).</summary>
    public TimeSpan DelayAfter(int attempt)
    {
        var doublings = Math.Min(attempt - 1, 30);
        var delay = TimeSpan.FromTicks(BaseDelay.Ticks * (1L << doublings));
        return delay > MaxDelay || delay < TimeSpan.Zero ? MaxDelay : delay;
    }
}
