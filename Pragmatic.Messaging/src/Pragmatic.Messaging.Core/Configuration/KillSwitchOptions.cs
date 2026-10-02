namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Consumer kill switch: after N consecutive dispatch failures on a subscription, consumption
///     pauses for a cool-down instead of hammering a broken downstream (half-open: after the
///     cool-down one message is attempted; success closes the switch). Enabled via
///     <c>MessagingBuilder.EnableKillSwitch()</c>.
/// </summary>
public sealed class KillSwitchOptions
{
    /// <summary>Consecutive failures on one subscription before tripping. Default: 10.</summary>
    public int ActivationThreshold { get; set; } = 10;

    /// <summary>
    ///     How long consumption pauses once tripped. Default: 60 seconds.
    /// </summary>
    /// <remarks>
    ///     On Kafka, keep this below <c>max.poll.interval.ms</c> (default 5 min): the cool-down stops
    ///     the subscription from polling, and a member that does not poll within that window is evicted
    ///     from the consumer group and its partitions are rebalanced away. A trip longer than the poll
    ///     interval therefore triggers a rebalance instead of a quiet cool-down.
    /// </remarks>
    public TimeSpan TripDuration { get; set; } = TimeSpan.FromSeconds(60);
}
