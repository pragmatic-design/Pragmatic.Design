using System.Collections.Concurrent;

namespace PragmaticBoundaries.Tests;

/// <summary>What the handlers of a host were handed, in order, each with the tenant the delivery ran under.</summary>
public sealed class Deliveries
{
    public ConcurrentQueue<(object Event, string? Tenant)> Received { get; } = new();
}
