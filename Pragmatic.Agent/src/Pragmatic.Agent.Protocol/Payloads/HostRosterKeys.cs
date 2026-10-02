namespace Pragmatic.Agent.Protocol.Payloads;

/// <summary>
///     Where a <see cref="HostDescriptorPayload" /> lives on the Agent KV: <c>state/app:{appId}/{instanceId}</c>,
///     one entry per running instance.
/// </summary>
/// <remarks>
///     <para>
///         Per instance and not per app: with one key per app id, the second instance of a host
///         overwrote the first, and the first to disconnect deleted the entry the other was still using.
///     </para>
///     <para>
///         A reader takes the app from the descriptor's <see cref="HostDescriptorPayload.AppId" />, not from
///         the key. What the key adds is only what makes it unique.
///     </para>
/// </remarks>
public static class HostRosterKeys
{
    /// <summary>The prefix every roster entry lives under.</summary>
    public const string Prefix = "state/app:";

    /// <summary>The key one instance of one app is listed under.</summary>
    public static string Key(string appId, string instanceId) => $"{Prefix}{appId}/{instanceId}";

    /// <summary>The prefix every instance of one app is listed under.</summary>
    public static string AppPrefix(string appId) => $"{Prefix}{appId}/";
}
