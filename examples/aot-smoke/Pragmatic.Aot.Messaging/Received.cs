using System.Collections.Concurrent;

namespace Pragmatic.Aot.Messaging;

/// <summary>What the handler received, read back by the smoke.</summary>
public sealed class Received
{
    private readonly ConcurrentQueue<string> _orders = new();

    /// <summary>The order ids received, in order.</summary>
    public IReadOnlyCollection<string> Orders => _orders;

    /// <summary>Records one.</summary>
    public void Add(string orderId) => _orders.Enqueue(orderId);
}
