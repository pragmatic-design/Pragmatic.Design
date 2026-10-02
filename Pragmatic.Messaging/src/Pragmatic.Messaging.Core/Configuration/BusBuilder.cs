namespace Pragmatic.Messaging.Configuration;

/// <summary>
///     Fluent builder for configuring a named message bus.
///     Each bus runs an ISOLATED transport (its own broker connection/topology), selected via
///     <see cref="UseTransport"/>; transport packages can layer convenience extensions on top.
/// </summary>
public sealed class BusBuilder
{
    /// <summary>The bus name.</summary>
    public string Name { get; }

    /// <summary>The transport type selected for this bus (diagnostics only).</summary>
    public string? TransportType { get; internal set; }

    /// <summary>The isolated transport factory for this bus.</summary>
    internal Func<IServiceProvider, IMessageTransport>? TransportFactory { get; private set; }

    internal BusBuilder(string name) => Name = name;

    /// <summary>
    ///     Materializes an isolated <see cref="IMessageTransport"/> for this bus. The factory runs
    ///     once (keyed singleton); the bus gets its own consumer service binding only the
    ///     subscriptions of handlers assigned to it via <c>[OnBus]</c>.
    /// </summary>
    public BusBuilder UseTransport(
        Func<IServiceProvider, IMessageTransport> transportFactory,
        string? transportType = null)
    {
        TransportFactory = transportFactory;
        TransportType = transportType ?? "custom";
        return this;
    }
}
