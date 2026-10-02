namespace Pragmatic.Messaging.Routing;

/// <summary>
///     Represents a binding between an exchange and a queue with a routing key.
///     SG-generated in <c>PragmaticMessagingTopology.GetBindings()</c>.
/// </summary>
public readonly record struct TopologyBinding(string Exchange, string Queue, string RoutingKey, string? BusName = null);
