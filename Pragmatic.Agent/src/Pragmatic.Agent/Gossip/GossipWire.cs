using System.Text;
using System.Text.Json;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     How the payload of a gossip datagram becomes a message, once the size guard and, with a shared key, the MAC
///     have let it through. Without a key every datagram that reaches the port gets here (#312).
/// </summary>
internal static class GossipWire
{
    /// <summary>
    ///     The message the payload carries, or null for a JSON <c>null</c>. Anything else it cannot read is a
    ///     <see cref="JsonException" />. Bytes that are not UTF-8 are read as U+FFFD, not refused.
    /// </summary>
    public static GossipMessage? Read(ReadOnlySpan<byte> payload)
        => JsonSerializer.Deserialize(Encoding.UTF8.GetString(payload), GossipJsonContext.Default.GossipMessage);
}
