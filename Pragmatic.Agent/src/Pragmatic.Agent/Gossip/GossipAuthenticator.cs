using System.Security.Cryptography;

namespace Pragmatic.Agent.Gossip;

/// <summary>
///     Authenticates gossip datagrams with a cluster-shared HMAC-SHA256.
///     The gossip UDP socket is unauthenticated at the network layer (any host that can reach the
///     port can send packets), so every datagram carries a keyed MAC computed over the JSON payload.
///     Receivers verify the MAC with a constant-time compare and DROP packets that fail — this is the
///     trust boundary that stops an attacker from injecting routes/secrets or forging KV deletes.
/// </summary>
/// <remarks>
///     Wire framing of an authenticated datagram:
///     <c>[ 32-byte HMAC-SHA256(key, payload) ][ JSON payload bytes ]</c>.
///     The key is the cluster-shared secret (see <see cref="ClusterConfig.SharedKey"/>); all agents in
///     a cluster must be configured with the same key.
/// </remarks>
internal sealed class GossipAuthenticator
{
    /// <summary>Length of the HMAC-SHA256 tag prepended to each datagram.</summary>
    public const int TagSize = 32;

    private readonly byte[] _key;

    public GossipAuthenticator(byte[] key)
    {
        if (key.Length == 0)
            throw new ArgumentException("Gossip shared key must be non-empty.", nameof(key));
        _key = key;
    }

    /// <summary>
    ///     Frames a payload as <c>[tag][payload]</c>, signing it with the cluster-shared key.
    /// </summary>
    public byte[] Sign(ReadOnlySpan<byte> payload)
    {
        var framed = new byte[TagSize + payload.Length];
        payload.CopyTo(framed.AsSpan(TagSize));

        // HMAC the payload portion only; the tag region is the output target.
        HMACSHA256.HashData(_key, framed.AsSpan(TagSize), framed.AsSpan(0, TagSize));
        return framed;
    }

    /// <summary>
    ///     Verifies a framed datagram and returns the inner payload when the MAC matches.
    ///     Uses <see cref="CryptographicOperations.FixedTimeEquals"/> for a constant-time compare.
    ///     Returns <see langword="false"/> (and a default span) for any datagram that is too short or
    ///     whose tag does not verify — the caller MUST drop it.
    /// </summary>
    public bool TryVerify(ReadOnlySpan<byte> datagram, out ReadOnlySpan<byte> payload)
    {
        if (datagram.Length < TagSize)
        {
            payload = default;
            return false;
        }

        var receivedTag = datagram[..TagSize];
        var body = datagram[TagSize..];

        Span<byte> computed = stackalloc byte[TagSize];
        HMACSHA256.HashData(_key, body, computed);

        if (!CryptographicOperations.FixedTimeEquals(receivedTag, computed))
        {
            payload = default;
            return false;
        }

        payload = body;
        return true;
    }

    /// <summary>
    ///     Resolves the cluster-shared gossip key from configuration or environment.
    ///     Precedence: explicit <paramref name="configuredKey"/> → <c>PRAGMATIC_AGENT_GOSSIP_KEY</c> env.
    ///     The key may be supplied as base64 (decoded as-is) or any other string (UTF-8 bytes).
    ///     Returns <see langword="null"/> when no key is configured — the caller decides the
    ///     fail-closed posture (see <see cref="SwimProtocol"/>).
    /// </summary>
    public static GossipAuthenticator? CreateFromConfig(string? configuredKey)
    {
        var raw = !string.IsNullOrEmpty(configuredKey)
            ? configuredKey
            : Environment.GetEnvironmentVariable("PRAGMATIC_AGENT_GOSSIP_KEY");

        if (string.IsNullOrEmpty(raw))
            return null;

        var keyBytes = TryDecodeBase64(raw, out var decoded)
            ? decoded
            : System.Text.Encoding.UTF8.GetBytes(raw);

        return new GossipAuthenticator(keyBytes);
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        // Treat as base64 only when it both parses AND yields a key of reasonable strength (>= 16
        // bytes). Short tokens like "dev" parse as base64 but are clearly meant as raw UTF-8.
        Span<byte> buffer = stackalloc byte[256];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written >= 16)
        {
            bytes = buffer[..written].ToArray();
            return true;
        }

        bytes = [];
        return false;
    }
}
