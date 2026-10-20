using System.Text.Json;
using Pragmatic.Agent.Gossip;

namespace Pragmatic.Agent.Fuzz;

/// <summary>
///     A gossip datagram's payload as the receive loop reads it (#312). Without a shared key every datagram that
///     reaches the UDP port gets here; with one, only what verifies, which arbitrary bytes do not.
/// </summary>
/// <remarks>
///     The declared outcomes are a message, null, and a <see cref="JsonException" />, which the loop logs and drops.
///     Those return; anything else escapes and is a crash.
/// </remarks>
public static class GossipDatagramTarget
{
    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            GossipWire.Read(data);
        }
        catch (JsonException)
        {
        }
    }
}
