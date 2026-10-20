using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using Pragmatic.Agent.Gossip;
using Xunit;

namespace Pragmatic.Agent.Tests.Gossip;

/// <summary>
///     A gossip datagram from anyone who can reach the UDP port (#158, #312). Without a shared key every payload is
///     read as a message; with one, only what verifies is. For any datagram the outcome is a dropped datagram, a
///     message, or a <see cref="JsonException" />, which the receive loop logs and drops.
/// </summary>
public class GossipWirePropertyTests
{
    private static readonly GossipAuthenticator Authenticator = new(Encoding.UTF8.GetBytes("cluster-shared-secret-key-0123456789"));

    private static readonly string[] Keys =
    [
        "type", "senderId", "members", "kvUpdates", "targetId", "id", "host", "port", "state", "incarnation", "key",
        "value", "version", "deleted", "updatedAt", "owner", "x",
    ];

    private static readonly string[] Scalars =
    [
        "0", "1", "7", "8", "-1", "2147483648", "9223372036854775808", "1.5", "1e400", "true", "false", "null", "\"\"",
        "\"node-a\"", "\"10.0.0.1\"", "\"2026-10-19T12:00:00Z\"", "\"9999-12-31T23:59:59-14:00\"", "\"Alive\"", "\"x\"",
    ];

    private static readonly Gen<byte[]> Payloads = Value(3).Select(json => Encoding.UTF8.GetBytes(json));

    private static Gen<string> Value(int depth)
    {
        var scalar = Gen.Elements(Scalars);
        if (depth == 0)
            return scalar;

        var inner = Value(depth - 1);
        return Gen.Frequency(
            (3, scalar),
            (1, inner.ListOf().Select(items => $"[{string.Join(",", items)}]")),
            (3, Gen.Elements(Keys).Zip(inner).Select(pair => $"\"{pair.Item1}\":{pair.Item2}")
                .ListOf().Select(members => $"{{{string.Join(",", members)}}}")));
    }

    [Fact]
    public void Any_payload_without_a_key_is_a_message_or_refused()
        => Prop.ForAll(Gen.Frequency((3, Payloads), (1, ArbMap.Default.ArbFor<byte[]>().Generator)).ToArbitrary(), IsDeclared)
            .Check(Config.QuickThrowOnFailure.WithMaxTest(2000));

    [Fact]
    public void Any_datagram_with_a_key_is_dropped_or_read_as_its_signed_payload()
        => Prop.ForAll(Payloads.ToArbitrary(), ArbMap.Default.ArbFor<bool>(), (payload, tamper) =>
        {
            var datagram = Authenticator.Sign(payload);
            if (tamper)
                datagram[^1] ^= 1;

            var verified = Authenticator.TryVerify(datagram, out var body);
            return tamper && payload.Length > 0
                ? !verified
                : verified && body.SequenceEqual(payload) && IsDeclared(body.ToArray());
        }).Check(Config.QuickThrowOnFailure.WithMaxTest(1000));

    private static bool IsDeclared(byte[] payload)
    {
        try
        {
            GossipWire.Read(payload);
        }
        catch (JsonException)
        {
        }

        return true;
    }
}
