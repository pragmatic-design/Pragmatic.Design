namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>
///     Where an <see cref="InstanceAnnouncementPayload" /> lives on the Agent KV: <c>gateway/instances/{routeId}/{instanceId}</c>.
/// </summary>
/// <remarks>
///     Here and not in the gateway, because a host writes it and a host does not reference the gateway.
///     Under <c>gateway/</c>, the prefix the gateway reloads its routes on.
/// </remarks>
public static class InstanceAnnouncementKeys
{
    /// <summary>The prefix every instance announcement lives under.</summary>
    public const string Prefix = "gateway/instances/";

    /// <summary>The key one instance announces one route under.</summary>
    public static string Key(string routeId, string instanceId) => $"{Prefix}{routeId}/{instanceId}";
}
