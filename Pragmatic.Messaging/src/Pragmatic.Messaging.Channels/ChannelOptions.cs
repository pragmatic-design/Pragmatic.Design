using System.Threading.Channels;

namespace Pragmatic.Messaging.Channels;

/// <summary>
///     Configuration for the Channels-based in-process transport.
/// </summary>
public sealed class ChannelOptions
{
    /// <summary>Per-channel capacity before backpressure. Default: 1000.</summary>
    public int Capacity { get; set; } = 1000;

    /// <summary>Behavior when channel is full. Default: Wait (blocks producer).</summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.Wait;

    /// <summary>Number of consumer tasks per subscription. Default: 1.</summary>
    public int ConsumerCount { get; set; } = 1;
}
